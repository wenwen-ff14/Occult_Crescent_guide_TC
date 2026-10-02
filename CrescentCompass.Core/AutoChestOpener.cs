using System.Numerics;

namespace CrescentCompass.Core;

public readonly record struct ChestInteractionContext(ushort Territory, uint Instance, ulong CharacterId,
    bool Ready, bool InCombat = false, bool Occupied = false, bool Mounted = false, bool Dead = false, bool Paused = false,
    bool InFlight = false, bool RidingPillion = false)
{
    public string BlockReason => !Ready || CharacterId == 0 || !SpotCatalog.IsSupported(Territory) ? "等待進入新月島且角色可操作。" :
        Dead ? "角色倒地，暫停開箱。" : InCombat ? "戰鬥中，暫停開箱。" : Occupied ? "互動、讀條或過場中，暫停開箱。" :
        InFlight ? "飛行中，請落地後靠近寶箱。" : RidingPillion ? "乘坐他人坐騎中，暫停開箱。" :
        Paused ? "巡查已暫停，自動開箱同步暫停。" : "";
}

/// <summary>Only operates on currently loaded observations. Submitting an interaction is not proof of collection.</summary>
public sealed class AutoChestOpener
{
    public const float Radius = 3;
    public const float RearmRadius = 6;
    public const int AttemptIntervalMs = 5000;
    public const int MaxAttempts = 3;
    private sealed class Attempt(Vector3 position, long seen)
    {
        public Vector3 Position { get; } = position;
        public long Seen { get; set; } = seen;
        public int Count { get; set; }
        public bool Spent { get; set; }
    }
    private readonly Dictionary<(ulong Object, uint Data), Attempt> attempts = [];
    private (ushort Territory, uint Instance, ulong Character)? session;
    private long? lastAttempt;
    private long? lastTick;
    public string Detail { get; private set; } = "關閉；勾選後自動開啟 3 公尺內的寶箱。";

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
        Vector3 player, long now, Func<Observation, bool> interact)
    {
        if (!enabled) { Reset(); Detail = "關閉；勾選後自動開啟 3 公尺內的寶箱。"; return; }
        var identity = (context.Territory, context.Instance, context.CharacterId);
        if (session != identity || lastTick is { } previous && now < previous) Reset();
        session = identity; lastTick = now;
        if (context.BlockReason is { Length: > 0 } reason) { Detail = reason; return; }
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
        if (lastAttempt is { } last && now - last < AttemptIntervalMs)
        { Detail = "已嘗試互動，等待遊戲更新；不代表已取得寶物。"; return; }

        var eligible = nearby.Where(o => Eligible(o, player) && !attempts[(o.ObjectId, o.DataId)].Spent).ToArray();
        var target = eligible.Where(o => attempts[(o.ObjectId, o.DataId)].Count < MaxAttempts)
            .OrderBy(o => Vector3.DistanceSquared(o.Position, player)).ThenBy(o => o.ObjectId).FirstOrDefault();
        if (target is null)
        {
            Detail = eligible.Length > 0 ? "附近寶箱已達 3 次嘗試上限；可手動開啟，或離開 6 公尺再靠近。" : "偵測中；請靠近可開啟的寶箱（3 公尺內）。";
            return;
        }
        // Reserve the attempt before calling native code, including failures, to prevent repeated calls each frame.
        lastAttempt = now;
        var attempt = attempts[(target.ObjectId, target.DataId)];
        attempt.Count++;
        Detail = interact(target) ? $"已嘗試開啟附近寶箱（{attempt.Count}/{MaxAttempts}）；等待遊戲確認。" :
            "目標或角色狀態已改變；稍後重新檢查。";
    }
}
