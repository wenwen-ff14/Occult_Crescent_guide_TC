using System.Numerics;

namespace CrescentCompass.Core;

public readonly record struct ChestInteractionContext(ushort Territory, uint Instance, ulong CharacterId,
    bool Ready, bool InCombat = false, bool Occupied = false, bool Mounted = false, bool Dead = false, bool Paused = false,
    bool InFlight = false, bool RidingPillion = false, bool Jumping = false)
{
    public string BlockReason => GetBlockReason();
    public string GetBlockReason(bool allowCombat = false, bool allowJumping = false) => !Ready || CharacterId == 0 || !SpotCatalog.IsSupported(Territory) ? "等待進入新月島且角色可操作。" :
        Dead ? "角色倒地，暫停開箱。" : InCombat && !allowCombat ? "戰鬥中，暫停開箱。" : Occupied ? "互動、讀條或過場中，暫停開箱。" :
        InFlight ? "飛行中，請落地後靠近寶箱。" : RidingPillion ? "乘坐他人坐騎中，暫停開箱。" :
        Jumping && !allowJumping ? "跳躍中，落地後再開箱。" :
        Paused ? "巡查已暫停，自動開箱同步暫停。" : "";
}

/// <summary>Only operates on currently loaded observations. Submitting an interaction is not proof of collection.</summary>
public sealed class AutoChestOpener
{
    public const float Radius = 2;
    public const float RearmRadius = 6;
    public const int AttemptIntervalMs = 1000;
    public const int PatrolAttemptIntervalMs = 500;
    private sealed class Attempt(Vector3 position, long seen)
    {
        public Vector3 Position { get; } = position;
        public long Seen { get; set; } = seen;
        public long? LastAttempt { get; set; }
        public bool Spent { get; set; }
    }
    private readonly Dictionary<(ulong Object, uint Data), Attempt> attempts = [];
    private (ushort Territory, uint Instance, ulong Character)? session;
    private long? lastAttempt;
    private long? lastTick;
    public string Detail { get; private set; } = "關閉；勾選後自動開啟 2 公尺內的寶箱。";

    public void Reset()
    {
        attempts.Clear(); session = null; lastAttempt = null; lastTick = null;
        Detail = "等待附近可開啟的寶箱。";
    }

    public static bool Eligible(Observation chest, Vector3 player) =>
        chest.ObjectId != 0 && chest.ObjectId != 0xE0000000 && CofferKinds.IsCoffer(chest.Kind) &&
        chest.Available && chest.Targetable && !chest.Opening && Coordinates.IsFinite(chest.Position) &&
        Coordinates.IsFinite(player) && Vector3.DistanceSquared(chest.Position, player) <= Radius * Radius;

    public void Update(bool enabled, ChestInteractionContext context, IReadOnlyList<Observation> observations,
        Vector3 player, long now, Func<Observation, bool> interact, int attemptIntervalMs = AttemptIntervalMs, bool allowCombat = false)
    {
        if (!enabled) { Reset(); Detail = "關閉；勾選後自動開啟 2 公尺內的寶箱。"; return; }
        var identity = (context.Territory, context.Instance, context.CharacterId);
        if (session != identity || lastTick is { } previous && now < previous) Reset();
        session = identity; lastTick = now;
        if (context.GetBlockReason(allowCombat) is { Length: > 0 } reason) { Detail = reason; return; }
        if (!Coordinates.IsFinite(player)) { Detail = "等待角色座標。"; return; }

        var nearby = observations.Where(o => CofferKinds.IsCoffer(o.Kind) && Coordinates.IsFinite(o.Position) &&
            Vector3.DistanceSquared(o.Position, player) <= RearmRadius * RearmRadius).ToArray();
        // Expire before observing again, so resuming after a long gap cannot revive stale suppression.
        foreach (var (key, entry) in attempts.ToArray())
            if (Vector3.DistanceSquared(entry.Position, player) > RearmRadius * RearmRadius || now - entry.Seen > 30_000)
                attempts.Remove(key);
        foreach (var chest in nearby)
        {
            var key = (chest.ObjectId, chest.DataId);
            if (!attempts.TryGetValue(key, out var entry) || Vector3.DistanceSquared(entry.Position, chest.Position) > 1)
                attempts[key] = entry = new(chest.Position, now);
            entry.Seen = now;
            entry.Spent |= !chest.Available;
        }
        if (nearby.Any(o => o.Opening && Vector3.DistanceSquared(o.Position, player) <= Radius * Radius))
        { Detail = "附近寶箱正在開啟，等待完成。"; return; }
        attemptIntervalMs = Math.Clamp(attemptIntervalMs, PatrolAttemptIntervalMs, AttemptIntervalMs);
        if (lastAttempt is { } last && now - last < attemptIntervalMs)
        { Detail = "已送出互動，等待遊戲更新。"; return; }

        var eligible = nearby.Where(o => Eligible(o, player) && !attempts[(o.ObjectId, o.DataId)].Spent).ToArray();
        // Rotate between nearby chests so one rejected interaction cannot starve the others.
        var target = eligible.OrderBy(o => attempts[(o.ObjectId, o.DataId)].LastAttempt ?? long.MinValue)
            .ThenBy(o => Vector3.DistanceSquared(o.Position, player)).ThenBy(o => o.ObjectId).FirstOrDefault();
        if (target is null)
        {
            Detail = "偵測中；靠近可開啟的寶箱（2 公尺內）便持續嘗試。";
            return;
        }
        // Reserve the attempt before calling native code, including failures, to prevent repeated calls each frame.
        lastAttempt = now;
        var attempt = attempts[(target.ObjectId, target.DataId)];
        attempt.LastAttempt = now;
        Detail = interact(target) ? "已嘗試開啟；仍在附近便持續嘗試，直到遊戲確認開啟。" :
            "目標或角色狀態已改變；仍在附近便持續檢查。";
    }
}
