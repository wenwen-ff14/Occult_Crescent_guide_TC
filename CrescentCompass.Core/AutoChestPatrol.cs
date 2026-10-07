using System.Numerics;

namespace CrescentCompass.Core;

public enum PatrolMovement { Idle, Owned, External }
public interface IChestPatrolNavigation
{
    bool Ready { get; }
    PatrolMovement Movement { get; }
    IReadOnlyList<Vector3> RemainingPath { get; }
    Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to, CancellationToken cancellation);
    Task<IReadOnlyList<Vector3>> RecoverPath(Vector3 from, Vector3 to, IReadOnlyList<Vector3> failedPath,
        int attempt, CancellationToken cancellation);
    bool TryJump();
    void Move(IReadOnlyList<Vector3> path);
    void Stop();
}

public sealed record ChestPatrolFrame(ChestInteractionContext Player, Vector3 Position, Spot? Target,
    bool Planning = false, bool EventNavigation = false, bool Suspended = false,
    bool ObservationsFresh = true, bool ChestInRange = false, bool ChestOpening = false,
    Spot? Following = null, bool CarrotMode = false);

/// <summary>Moves one chart stop at a time. Route completion still comes from observed chests or empty-pad checks.</summary>
public sealed class AutoChestPatrol(IChestPatrolNavigation navigation)
{
    public const float ArrivalRadius = 1.5f;
    public const long StuckTimeoutMs = 6_000;
    public const long JumpAfterMs = 3_000;
    public const long JumpIntervalMs = 2_000;
    public const int MaxJumpAttempts = 5;
    public const long ArrivalTimeoutMs = 20_000;
    public const int MaxRecoveryAttempts = 3;
    public const long SettleTimeMs = 250;
    private CancellationTokenSource? cancellation;
    private Task<IReadOnlyList<Vector3>>? query;
    private Spot? target;
    private Vector3 queryOrigin;
    private float bestRemaining;
    private long queryStarted;
    private long progressAt;
    private long? arrivedAt;
    private bool moving;
    private bool stopPending;
    private IReadOnlyList<Vector3>? activePath;
    private IReadOnlyList<Vector3>? failedPath;
    private int recoveryAttempts;
    private int jumpAttempts;
    private long? lastJumpAt;
    private long? airborneSince;
    private Vector3 progressPosition;
    private CancellationTokenSource? preloadCancellation;
    private Task<IReadOnlyList<Vector3>>? preload;
    private long? settledSince;
    private Vector3 settledPosition;
    private (ushort Territory, uint Instance, ulong Character)? identity;
    public bool Enabled { get; private set; }
    public bool Faulted { get; private set; }
    public bool Recovering => failedPath is not null;
    public bool CanOpenChest { get; private set; }
    public string Detail { get; private set; } = "未啟動";

    public IReadOnlyList<Vector3>? PathFrom(Vector3 position) => activePath is not null &&
        PatrolPathCache.TryResume(position, activePath, out var path) ? path : null;

    public void Start()
    {
        Stop("準備自動巡查。");
        if (stopPending) return;
        Enabled = true; Faulted = false; identity = null;
    }

    public void Stop(string reason = "已停止自動巡查；路線與紀錄保留。")
    {
        Enabled = false;
        Suspend(reason);
        identity = null;
    }

    public void Suspend(string reason)
    {
        try { ResetSegment(); Detail = reason; }
        catch (Exception error)
        {
            Enabled = false; Faulted = true; stopPending = true;
            Detail = $"停止導航失敗：{error.Message}";
        }
    }

    public void Fail(string reason)
    {
        Stop(reason); Faulted = true;
    }

    public void Update(ChestPatrolFrame frame, long now)
    {
        CanOpenChest = false;
        if (stopPending) { Suspend(Detail); return; }
        if (!Enabled) return;
        try
        {
            var player = frame.Player;
            if (frame.Suspended || !player.Ready) { Suspend("等待角色與地圖就緒；保留自動巡查。"); return; }
            var currentIdentity = (player.Territory, player.Instance, player.CharacterId);
            if (player.Territory != ChestChart.Territory || player.CharacterId == 0 || identity is { } old && old != currentIdentity)
            { Stop("區域、分流或角色已變更，停止自動巡查。"); return; }
            identity = currentIdentity;
            if (player.Dead) { Fail("角色倒地，已停止自動巡查。"); return; }
            if (player.GetBlockReason(allowCombat: true, allowJumping: moving) is { Length: > 0 } blocked) { Suspend(blocked); return; }
            if (frame.EventNavigation) { Suspend("魔法罐／FATE 優先，暫停自動移動。"); return; }
            if (frame.Planning) { Suspend("等待路線計算完成。"); return; }
            if (!frame.ObservationsFresh) { Suspend("等待新的目標觀測資料。"); return; }
            if (frame.Target is not { } next) { Stop("本輪自動巡查完成。"); return; }
            var number = frame.CarrotMode ? CarrotRoute.Number(next) : ChestChart.Number(next);
            if (number is null || !Coordinates.IsFinite(frame.Position) || !Coordinates.IsFinite(next.Position))
            { Fail("目前站點不是有效的南部巡查點，已停止。"); return; }
            if (!navigation.Ready) { Fail("vnavmesh 未就緒或移動被停用，已停止自動巡查。"); return; }
            if (target is null || target.Id != next.Id || Vector3.DistanceSquared(target.Position, next.Position) > 0.25f)
            { ResetSegment(); target = next; }
            var movement = navigation.Movement;
            if (movement == PatrolMovement.External)
            { Fail("其他導航正在使用 vnavmesh，已停止自動巡查。"); return; }
            var reached = frame.ChestOpening || frame.ChestInRange ||
                Vector3.DistanceSquared(frame.Position, next.Position) <= ArrivalRadius * ArrivalRadius;
            if (player.Jumping)
            {
                if (movement != PatrolMovement.Owned && !reached)
                { Fail("跳躍期間移動已中斷，保留目前站點。"); return; }
                airborneSince ??= now;
                if (now - airborneSince >= 10_000) { Fail("長時間未落地，已停止並保留箱點。"); return; }
                Detail = "跳躍通過障礙物；落地後確認路程進展。";
                return;
            }
            airborneSince = null;

            if (reached)
            {
                failedPath = null;
                CancelQuery(); navigation.Stop(); moving = false;
                arrivedAt ??= now;
                if (now - arrivedAt >= (frame.CarrotMode ? 60_000 : ArrivalTimeoutMs)) { Fail("到站後未能完成互動或空點確認，已暫停；請檢查背包與地形。"); return; }
                if (settledSince is null || Vector3.DistanceSquared(settledPosition, frame.Position) > 0.01f)
                { settledSince = now; settledPosition = frame.Position; }
                CanOpenChest = frame.ChestInRange && now - settledSince >= SettleTimeMs;
                Detail = frame.CarrotMode ? "已到蘿蔔點，等待使用物品／兔子寶箱或空點確認。" : frame.ChestOpening ? "寶箱正在開啟，等待遊戲確認。" :
                    frame.ChestInRange ? CanOpenChest ? "已停步，正在開啟寶箱。" : "靠近寶箱，等待角色停穩。" : "已到箱點，等待寶箱或空點確認。";
                return;
            }
            arrivedAt = settledSince = null;
            if (moving)
            {
                if (movement != PatrolMovement.Owned)
                { Fail("移動已中斷，保留目前站點；重新啟動後再尋路。"); return; }
                var remaining = navigation.RemainingPath;
                if (PathFrom(frame.Position) is { } suffix) activePath = suffix;
                var distanceLeft = RemainingDistance(frame.Position, activePath ?? remaining);
                var horizontalProgress = Vector2.DistanceSquared(new(frame.Position.X, frame.Position.Z),
                    new(progressPosition.X, progressPosition.Z)) > 1;
                if (distanceLeft < bestRemaining - 1 && horizontalProgress)
                { bestRemaining = distanceLeft; progressAt = now; progressPosition = frame.Position; }
                if (now - progressAt >= JumpAfterMs && now - progressAt < StuckTimeoutMs &&
                    jumpAttempts < MaxJumpAttempts && (lastJumpAt is null || now - lastJumpAt >= JumpIntervalMs))
                {
                    // Reserve failed attempts too; never spam native actions or reset the repath deadline by jumping.
                    jumpAttempts++; lastJumpAt = now;
                    if (navigation.TryJump())
                    { Detail = $"路程無進展，嘗試跳躍脫困（{jumpAttempts}/{MaxJumpAttempts}）。"; return; }
                }
                if (now - progressAt >= StuckTimeoutMs)
                {
                    if (recoveryAttempts >= MaxRecoveryAttempts)
                    { Fail("替代路徑仍無進展，已停止並保留箱點；請手動脫困。"); return; }
                    recoveryAttempts++;
                    failedPath = new[] { frame.Position }.Concat(remaining).ToArray();
                    CancelPreload(); navigation.Stop(); moving = false;
                    activePath = null;
                    Detail = $"移動 6 秒無進展，檢查側移／後退路徑（{recoveryAttempts}/{MaxRecoveryAttempts}）。";
                    return;
                }
                Preload(next, frame.Following);
                Detail = $"自動前往 #{number:00} · 距離 {Vector3.Distance(frame.Position, next.Position):F0} m";
                return;
            }
            if (query is { IsCompleted: true } finished)
            {
                var recovering = Recovering;
                var path = finished.GetAwaiter().GetResult();
                query = null; cancellation?.Dispose(); cancellation = null;
                if (Vector3.DistanceSquared(frame.Position, queryOrigin) > PatrolPathCache.ReuseTolerance * PatrolPathCache.ReuseTolerance)
                {
                    if (recovering) { Fail("恢復期間位置已變更，請重新啟動巡查。"); return; }
                    return;
                }
                // Use only returned mesh waypoints; never append a straight segment to the chest.
                if (path.Count == 0 || path.Any(p => !Coordinates.IsFinite(p)) ||
                    Vector3.DistanceSquared(path[0], frame.Position) > 9 ||
                    Vector3.DistanceSquared(path[^1], next.Position) > ArrivalRadius * ArrivalRadius)
                {
                    if (recovering) { Fail("找不到能繞開卡點的地面通路，已停止並保留箱點；請手動脫困。"); return; }
                    Fail("找不到可接近此箱點的完整地面路徑，保留站點並停止。"); return;
                }
                failedPath = null; activePath = new[] { frame.Position }.Concat(path).ToArray();
                navigation.Move(path);
                moving = true; bestRemaining = RemainingDistance(frame.Position, activePath); progressAt = now;
                progressPosition = frame.Position;
                Preload(next, frame.Following);
                Detail = $"{(recovering ? "脫困前往" : "自動前往")} #{number:00}";
                return;
            }
            if (query is not null)
            {
                if (now - queryStarted >= 16_000)
                {
                    Fail(Recovering ? "脫困尋路逾時，已停止並保留箱點。" : "尋路逾時，已停止自動巡查。");
                }
                return;
            }
            cancellation = new(); queryOrigin = frame.Position; queryStarted = now;
            CancelPreload();
            query = failedPath is not null ? navigation.RecoverPath(frame.Position, next.Position, failedPath, recoveryAttempts, cancellation.Token)
                : navigation.FindPath(frame.Position, next.Position, cancellation.Token);
            Detail = Recovering ? $"正在檢查替代路徑（{recoveryAttempts}/{MaxRecoveryAttempts}）。" : $"正在尋找前往 #{number:00} 的地面路徑。";
        }
        catch (Exception error) { Fail($"自動巡查中斷：{error.Message}"); }
    }

    public static bool OwnsPath(IReadOnlyList<Vector3>? submitted, IReadOnlyList<Vector3> remaining) =>
        submitted is not null && remaining.Count > 0 && remaining.Count <= submitted.Count &&
        remaining.SequenceEqual(submitted.Skip(submitted.Count - remaining.Count));

    private void CancelQuery()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        if (query is { } pending)
            _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        query = null;
    }

    public static float RemainingDistance(Vector3 from, IReadOnlyList<Vector3> path)
    {
        if (PatrolPathCache.TryResume(from, path, out var suffix)) path = suffix;
        var distance = 0f;
        foreach (var point in path) { distance += Vector3.Distance(from, point); from = point; }
        return distance;
    }

    private void Preload(Spot current, Spot? following)
    {
        if (preload is not null || following is null || following.Id == current.Id ||
            following.Territory != current.Territory || !Coordinates.IsFinite(following.Position)) return;
        preloadCancellation = new();
        // A failed speculative query never affects the active route. Movement validates it on use.
        try { preload = navigation.FindPath(current.Position, following.Position, preloadCancellation.Token); }
        catch { preload = Task.FromResult<IReadOnlyList<Vector3>>([]); }
        _ = preload.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CancelPreload()
    {
        preloadCancellation?.Cancel(); preloadCancellation?.Dispose(); preloadCancellation = null; preload = null;
    }

    private void ResetSegment()
    {
        CancelQuery(); CancelPreload(); target = null; moving = false; arrivedAt = settledSince = null;
        CanOpenChest = false; recoveryAttempts = 0;
        jumpAttempts = 0; lastJumpAt = airborneSince = null;
        activePath = failedPath = null;
        navigation.Stop(); stopPending = false;
    }
}
