using System.Numerics;

namespace CrescentCompass.Core;

public enum RouteAdvanceReason { None, Opened, Empty }
public sealed record RouteUpdate(RouteAdvanceReason Reason, int Removed);

// Absence is only a conservative near-pad heuristic, never proof of collection or ownership.
public sealed class RouteAutomation
{
    public bool Paused { get; private set; }
    public void SetPaused(bool paused) { Reset(); Paused = paused; }
    public const float CheckRadius = 60;
    public const float MaxHeightDifference = 8;
    public const long EmptyWaitMs = 3000;
    private string? inspectedId;
    private long? missingSince;
    private long? lastScan;
    private int consecutiveScans;
    private string? pendingFlag;
    private long? lastFlagAttempt;
    private int flagAttempts;
    private string detail = "先規劃路線，再開始自動巡查。";
    public string Detail
    {
        get => flagAttempts >= 3 ? "自動插旗失敗 3 次，請按「在地圖插旗」重試。" : detail;
        private set => detail = value;
    }

    public void ResetInspection() { inspectedId = null; missingSince = null; lastScan = null; consecutiveScans = 0; }
    public void CancelFlag() { pendingFlag = null; lastFlagAttempt = null; flagAttempts = 0; }
    public void Reset() { ResetInspection(); CancelFlag(); Detail = "先規劃路線，再開始自動巡查。"; }
    public static float NormalizeRadius(float radius) => float.IsFinite(radius) ? Math.Clamp(radius, 20, 100) : CheckRadius;

    public RouteUpdate Update(SurveySession session, List<Spot> remaining, IReadOnlyList<Observation> observations,
        Vector3 player, long now, PointDisplayMode mode, bool enabled, bool canCheckAbsence = true, float radius = CheckRadius,
        string? absenceBlockReason = null, bool preservePlannedStops = false, long emptyWaitMs = EmptyWaitMs)
    {
        if (Paused) { Reset(); Detail = "巡查已暫停；保留目前站點。"; return new(RouteAdvanceReason.None, 0); }
        var head = remaining.FirstOrDefault();
        var tracked = head is null ? null : session.Get(head.Id);
        var reason = RouteAdvanceReason.None;
        if (enabled) reason = Inspect(tracked, observations, player, now, canCheckAbsence, radius, absenceBlockReason, emptyWaitMs);
        else { Reset(); Detail = "自動下一站已關閉。"; }
        if (reason == RouteAdvanceReason.Empty) session.Skip(head!.Id);

        // Keep a nearby current chest during the absence grace period even in visible-only mode.
        // Filtering other unloaded points never counts as collection or triggers an automatic flag.
        var removed = remaining.RemoveAll(s => !(preservePlannedStops ? session.CanPatrol(s.Id) : session.CanDisplay(s.Id, mode)) &&
            !(enabled && reason == RouteAdvanceReason.None && s.Id == head?.Id &&
              tracked is not null && tracked.Status is not (SpotStatus.Visited or SpotStatus.Skipped) &&
              CofferKinds.IsCoffer(s.Kind) && IsNear(s, player, radius)));
        if (reason != RouteAdvanceReason.None)
        {
            ResetInspection();
            if (remaining.FirstOrDefault() is { } next) ScheduleFlag(next.Id);
            else CancelFlag();
        }
        return new(reason, removed);
    }

    public static bool IsNear(Spot spot, Vector3 player, float radius = CheckRadius) => Coordinates.IsFinite(player) && Coordinates.IsFinite(spot.Position) &&
        MathF.Abs(player.Y - spot.Position.Y) <= MaxHeightDifference &&
        Vector2.DistanceSquared(new(player.X, player.Z), new(spot.Position.X, spot.Position.Z)) <= MathF.Pow(NormalizeRadius(radius), 2);

    public RouteAdvanceReason Inspect(TrackedSpot? target, IReadOnlyList<Observation> observations, Vector3 player, long now, bool canCheckAbsence = true, float radius = CheckRadius,
        string? absenceBlockReason = null, long emptyWaitMs = EmptyWaitMs)
    {
        if (target is null) return Wait("目前沒有路線站點；請先規劃或重新巡查。");
        if (!CofferKinds.IsCoffer(target.Spot.Kind)) return Wait("目前站是蘿蔔或探索地點，請手動完成巡查。");
        if (!SpotCatalog.IsSupported(target.Spot.Territory)) return Wait("等待進入新月島。");
        if (inspectedId != target.Spot.Id) { ResetInspection(); inspectedId = target.Spot.Id; }
        if (target.Status == SpotStatus.Visited) { ResetInspection(); Detail = "已確認開啟，切換下一站。"; return RouteAdvanceReason.Opened; }
        if (!canCheckAbsence) return Wait("互動、讀條或過場中，暫停空點確認。");
        if (!Coordinates.IsFinite(player)) return Wait("等待有效玩家位置。");
        radius = NormalizeRadius(radius);
        emptyWaitMs = Math.Clamp(emptyWaitMs, PatrolEmptyCheck.ConfirmationMs, EmptyWaitMs);
        var distance = Vector2.Distance(new(player.X, player.Z), new(target.Spot.Position.X, target.Spot.Position.Z));
        var height = MathF.Abs(player.Y - target.Spot.Position.Y);
        if (!IsNear(target.Spot, player, radius)) return Wait($"距目標 {distance:F0} m／判定 {radius:F0} m，高差 {height:F1} m／上限 {MaxHeightDifference:F0} m。");
        var nearby = observations.Where(o => CofferKinds.IsCoffer(o.Kind) && Vector3.DistanceSquared(o.Position, target.Spot.Position) <= 16).ToArray();
        if (nearby.Any(o => o.Opening)) return Wait("寶箱正在開啟，等待完成。");
        if (target.Status == SpotStatus.Visible || nearby.Any(o => o.Available && o.Targetable)) return Wait("目前站有可選取寶箱，等待開啟。");
        if (target.Status == SpotStatus.Skipped) return Wait("此站已略過。");
        if (absenceBlockReason is not null) return Wait(absenceBlockReason);
        // Untargetable placeholders can remain loaded for chests this player cannot use.
        // Only a usable or opening coffer blocks the timer; lack of usability is never called collection.
        // Only consecutive successful scans count. A stall, exception, cutscene or reset restarts the grace period.
        if (lastScan is not { } previous || now <= previous || now - previous > emptyWaitMs)
        { missingSince = now; consecutiveScans = 0; }
        missingSince ??= now;
        lastScan = now;
        consecutiveScans++;
        var elapsed = now - missingSince.Value;
        Detail = $"{(nearby.Length > 0 ? "箱體持續不可選取" : "未偵測到可用寶箱")} · 確認 {Math.Min(elapsed, emptyWaitMs) / 1000f:F1} / {emptyWaitMs / 1000f:F1} 秒";
        if (elapsed < emptyWaitMs || emptyWaitMs == PatrolEmptyCheck.ConfirmationMs && consecutiveScans < 3) return RouteAdvanceReason.None;
        ResetInspection();
        Detail = $"連續 {emptyWaitMs / 1000f:F1} 秒沒有可用寶箱，已略過並切換下一站。";
        return RouteAdvanceReason.Empty;
    }

    private RouteAdvanceReason Wait(string detail)
    { missingSince = null; lastScan = null; consecutiveScans = 0; Detail = detail; return RouteAdvanceReason.None; }

    public void ScheduleFlag(string id) { CancelFlag(); pendingFlag = id; }
    public bool CanAttemptFlag(string? currentHeadId, long now)
    {
        if (pendingFlag != currentHeadId) CancelFlag();
        return !Paused && pendingFlag is not null && flagAttempts < 3 && (lastFlagAttempt is not { } previous || now - previous >= 1500);
    }
    public void RecordFlagAttempt(long now, bool success)
    {
        lastFlagAttempt = now; flagAttempts++;
        if (success) CancelFlag();
        else if (flagAttempts >= 3) Detail = "自動插旗失敗 3 次，請按「在地圖插旗」重試。";
    }
}
