using System.Numerics;

namespace CrescentCompass.Core;

public sealed record FateFlagObservation(ushort Id, ushort Territory, string Name, Vector3 Position,
    long StartTimeEpoch, int DurationSeconds, int Progress, bool Running, bool Preparing = false);
public sealed record FateFlagTarget(ushort Id, long Occurrence, string Name, Vector3 Position,
    int Progress, bool Preparing, DateTimeOffset? EndsAt, DateTimeOffset StartedAt);

/// <summary>One map flag per observed FATE occurrence; no predicted spawns or character movement.</summary>
public sealed class FateAutoFlagger
{
    private sealed class Seen(DateTimeOffset start, bool gameTime, long serial)
    {
        public DateTimeOffset Start = start;
        public bool GameTime = gameTime, Finished;
        public long Serial = serial;
    }
    private readonly Dictionary<ushort, Seen> seen = [];
    private IReadOnlyList<FateFlagTarget> active = [];
    private (ushort Id, long Serial)? pending, held;
    private DateTimeOffset? lastScan, lastAttempt;
    private ushort territory;
    private uint instance;
    private long serial;
    private int attempts;
    public string Detail { get; private set; } = "等待偵測新月島 FATE。";

    public void Reset()
    { seen.Clear(); active = []; pending = held = null; lastScan = lastAttempt = null; attempts = 0; territory = 0; instance = 0; Detail = "等待偵測新月島 FATE。"; }
    public void Suspend() { lastScan = null; }
    public void Release()
    { pending = held = null; attempts = 0; lastAttempt = null; Detail = "已解除本次 FATE 標點；下次新事件仍會自動標點。"; }
    public void HoldManual(FateFlagTarget target, DateTimeOffset now)
    {
        Release();
        if (Find((target.Id, target.Occurrence), now) is null) return;
        held = (target.Id, target.Occurrence); Detail = $"已手動標點：{target.Name}；事件結束或解除後接續巡查。";
    }
    public IReadOnlyList<FateFlagTarget> Active(DateTimeOffset now) => lastScan is { } scan && now >= scan && now - scan <= TimeSpan.FromSeconds(3)
        ? active.Where(f => f.EndsAt is null || f.EndsAt > now).ToArray() : [];
    private FateFlagTarget? Find((ushort Id, long Serial)? key, DateTimeOffset now) => key is { } k
        ? Active(now).FirstOrDefault(f => f.Id == k.Id && f.Occurrence == k.Serial) : null;
    public bool OwnsNavigation(DateTimeOffset now) => Find(held, now) is not null || Find(pending, now) is not null;

    public void Observe(ushort currentTerritory, uint currentInstance, IEnumerable<FateFlagObservation> rows,
        DateTimeOffset now, bool enabled, Vector3 player)
    {
        if (territory != currentTerritory || instance != currentInstance)
        { Reset(); territory = currentTerritory; instance = currentInstance; }
        if (!SpotCatalog.IsSupported(territory)) { Reset(); return; }
        List<FateFlagTarget> live = [], spawned = [];
        foreach (var row in rows.Where(r => r.Id != 0 && r.Territory == territory && r.Progress is >= 0 and <= 100)
                     .GroupBy(r => r.Id).Select(g => g.OrderByDescending(r => r.StartTimeEpoch).First()))
        {
            var epoch = now.ToUnixTimeSeconds();
            DateTimeOffset? start = row.StartTimeEpoch > 0 && row.StartTimeEpoch <= epoch && epoch - row.StartTimeEpoch <= 7200
                ? DateTimeOffset.FromUnixTimeSeconds(row.StartTimeEpoch) : null;
            var end = start is { } s && row.DurationSeconds is > 0 and <= 3600 ? s.AddSeconds(row.DurationSeconds) : (DateTimeOffset?)null;
            var running = row.Running && row.Progress < 100 && (end is null || end > now);
            seen.TryGetValue(row.Id, out var record);
            if (record is null && !running) continue;
            if (record is { GameTime: true } && start is { } old && old < record.Start) continue;
            var isNew = record is null || record.GameTime && start is { } next && next > record.Start || start is null && running && record.Finished;
            if (isNew) seen[row.Id] = record = new(start ?? now, start is not null, ++serial);
            else if (!record!.GameTime && start is { } corrected) { record.Start = corrected; record.GameTime = true; }
            record!.Finished = !running;
            if (!running) continue;
            var target = new FateFlagTarget(row.Id, record.Serial, row.Name, row.Position, row.Progress, row.Preparing, end, record.Start);
            live.Add(target);
            if (isNew && enabled) spawned.Add(target);
        }
        var hadNavigation = held is not null || pending is not null;
        active = live; lastScan = now;
        if (Find(held, now) is null) held = null;
        if (Find(pending, now) is null) pending = null;
        if (!enabled) { Release(); Detail = "自動標點已關閉；清單仍會更新。"; return; }
        // The game has one flag: newest start wins; simultaneous starts use proximity then ID.
        var newest = spawned.OrderByDescending(f => f.StartedAt)
            .ThenBy(f => Coordinates.IsFinite(f.Position) && Coordinates.IsFinite(player) ? Vector3.DistanceSquared(f.Position, player) : float.MaxValue)
            .ThenBy(f => f.Id).FirstOrDefault();
        if (newest is not null)
        { pending = (newest.Id, newest.Occurrence); attempts = 0; lastAttempt = null; Detail = $"待標點：{newest.Name}"; }
        else if (hadNavigation && pending is null && held is null) Detail = "FATE 已結束或不再可見，解除標點優先；等待下一個新事件。";
    }

    public void Tick(DateTimeOffset now, bool enabled, bool occupied, bool potSearching, Func<FateFlagTarget, bool> flag)
    {
        if (!enabled) { Release(); Detail = "自動標點已關閉；清單仍會更新。"; return; }
        if (potSearching)
        { held = null; Detail = pending is not null ? "魔法罐尋寶優先；結束後若 FATE 仍在進行再標點。" : "魔法罐尋寶優先；等待下一個新 FATE。"; return; }
        if (Find(pending, now) is not { } target) return;
        if (occupied) { Detail = "等待讀條或互動結束後標點。"; return; }
        if (!Coordinates.IsFinite(target.Position)) { Detail = "等待有效 FATE 座標。"; return; }
        if (lastAttempt is { } attempt && now - attempt < TimeSpan.FromSeconds(2)) return;
        lastAttempt = now; attempts++;
        bool ok;
        try { ok = flag(target); } catch { ok = false; }
        if (ok) { held = pending; pending = null; Detail = $"已標點：{target.Name}；巡查保留，事件結束或解除後接續。"; }
        else if (attempts >= 3) { pending = null; Detail = "FATE 插旗重試 3 次仍失敗，可從清單手動標點。"; }
        else Detail = "FATE 插旗暫未成功，稍後重試。";
    }
}
