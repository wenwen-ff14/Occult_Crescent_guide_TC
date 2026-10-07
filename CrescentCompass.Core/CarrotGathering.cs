using System.Numerics;

namespace CrescentCompass.Core;

public sealed record CarrotGatherUpdate(ulong PickedObject = 0, bool Finished = false, bool Failed = false, ulong PickupSequence = 0);

/// <summary>Use one inventory carrot at a live pad, verify consumption, then wait for its bunny coffer.</summary>
public sealed class CarrotGathering
{
    public const uint ItemId = 48096;
    public const float UseRadius = 2;
    private string? padId;
    private readonly HashSet<ulong> used = [];
    private HashSet<ulong> priorBunnies = [];
    private Observation? carrot;
    private Observation? bunny;
    private int baseline;
    private int attempts;
    private long? issuedAt;
    private long? consumedAt;
    private long? missingSince;
    private long? lastScan;
    private bool bunnyInteracted;
    private ulong pickupSequence; // Survives resets; respawns may reuse an object's identity.
    public bool Busy => issuedAt is not null || consumedAt is not null;
    public bool PickupConfirmed => consumedAt is not null;
    public Observation? Bunny => bunny;
    public string Detail { get; private set; } = "等待蘿蔔點。";

    public void Reset()
    {
        padId = null; used.Clear(); priorBunnies.Clear(); carrot = bunny = null;
        baseline = attempts = 0; issuedAt = consumedAt = missingSince = lastScan = null; bunnyInteracted = false;
        Detail = "等待蘿蔔點。";
    }
    public Observation? LiveCarrot(Spot pad, IEnumerable<Observation> observations) => observations
        .Where(o => o.Kind == SpotKind.Carrot && o.Available && ValidObject(o.ObjectId) && !used.Contains(o.ObjectId) &&
            Coordinates.IsFinite(o.Position) && Vector3.DistanceSquared(pad.Position, o.Position) <= 16)
        .OrderBy(o => Vector3.DistanceSquared(pad.Position, o.Position)).FirstOrDefault();
    public static bool InRange(Vector3 player, Observation target) => Coordinates.IsFinite(player) && Coordinates.IsFinite(target.Position) &&
        Vector3.DistanceSquared(player, target.Position) <= UseRadius * UseRadius;
    public void RecordBunnyInteraction(ulong id) { if (bunny?.ObjectId == id) bunnyInteracted = true; }
    private static bool ValidObject(ulong id) => id != 0 && id != 0xE0000000;

    public CarrotGatherUpdate Update(Spot pad, IReadOnlyList<Observation> observations, Vector3 player, int? itemCount,
        bool canUse, bool casting, long now, Func<Observation, bool> use)
    {
        if (padId != pad.Id) { Reset(); padId = pad.Id; }
        if (lastScan is { } last && (now < last || now - last > 1500))
        {
            missingSince = null;
            if (Busy) { Detail = "蘿蔔處理期間觀測中斷，請確認物品與寶箱後再巡查。"; return new(Failed: true); }
        }
        lastScan = now;
        if (CarrotRoute.Number(pad) is null || !Coordinates.IsFinite(player)) return new();
        if (Busy && carrot is { } source && Vector3.DistanceSquared(player, source.Position) > 144)
        { Detail = "已離開蘿蔔互動範圍，停止並保留站點。"; return new(Failed: true); }
        if (consumedAt is { } consumed)
        {
            var current = bunny is { } known
                ? observations.FirstOrDefault(o => o.ObjectId == known.ObjectId && o.Kind == SpotKind.RabbitGold)
                : observations.FirstOrDefault(o => o.Kind == SpotKind.RabbitGold && ValidObject(o.ObjectId) && !priorBunnies.Contains(o.ObjectId) &&
                    Coordinates.IsFinite(o.Position) && Vector3.DistanceSquared(o.Position, carrot!.Position) <= 100);
            if (current is not null)
            {
                bunny = current; missingSince = null;
                if (!current.Available) return FinishBunny(pad, observations);
            }
            else if (bunny is not null && bunnyInteracted && !casting && Vector3.DistanceSquared(player, bunny.Position) <= 36)
            {
                missingSince ??= now;
                if (now - missingSince >= 1000) return FinishBunny(pad, observations);
            }
            else missingSince = null;
            if (now - consumed >= 30_000) { Detail = "未能確認兔子寶箱處理完成，已停止；請手動確認。"; return new(Failed: true); }
            Detail = bunny is null ? "已確認使用蘿蔔，等待兔子寶箱。" : "等待兔子寶箱開啟。";
            return new();
        }
        if (issuedAt is { } issued)
        {
            if (itemCount is { } changed && (changed < baseline - 1 || changed > baseline))
            { Detail = "背包數量變動不符單次蘿蔔使用，請手動確認。"; return new(Failed: true); }
            if (itemCount is { } count && count == baseline - 1)
            {
                consumedAt = now; issuedAt = null; used.Add(carrot!.ObjectId);
                Detail = "已確認蘿蔔消耗，更新搜尋權重。";
                return new(carrot.ObjectId, PickupSequence: ++pickupSequence);
            }
            if (now - issued >= 30_000)
            { Detail = "蘿蔔使用或背包確認逾時，請手動確認；未增加搜尋權重。"; return new(Failed: true); }
            if (itemCount is null) { Detail = "等待背包資料恢復，暫不重送蘿蔔使用。"; return new(); }
            if (casting) { Detail = "蘿蔔使用讀條中，等待物品數量確認。"; return new(); }
            if (now - issued < 5000) { Detail = "已送出蘿蔔使用，等待遊戲確認。"; return new(); }
            issuedAt = null;
            if (attempts >= 3) { Detail = "蘿蔔使用未確認，停止巡查；未增加搜尋權重。"; return new(Failed: true); }
        }
        var live = LiveCarrot(pad, observations);
        if (live is null || !canUse || !InRange(player, live)) return new();
        if (itemCount is null) { Detail = "背包資料尚未就緒。"; return new(); }
        if (itemCount <= 0) { Detail = "背包沒有幸運胡蘿蔔，停止自動巡查。"; return new(Failed: true); }
        carrot = live; baseline = itemCount.Value; issuedAt = now; attempts++;
        priorBunnies = observations.Where(o => o.Kind == SpotKind.RabbitGold).Select(o => o.ObjectId).ToHashSet();
        use(live); // Native acceptance is never collection evidence; only a later inventory decrement is.
        Detail = "已嘗試使用幸運胡蘿蔔，等待遊戲確認。";
        return new();
    }

    private CarrotGatherUpdate FinishBunny(Spot pad, IReadOnlyList<Observation> observations)
    {
        consumedAt = issuedAt = missingSince = null; bunny = null; bunnyInteracted = false; attempts = 0;
        Detail = "兔子寶箱已處理。";
        // Two separate live carrots can share a pad. Do not abandon the second one.
        return new(Finished: LiveCarrot(pad, observations) is null);
    }
}
