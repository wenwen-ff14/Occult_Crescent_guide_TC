using System.Numerics;

namespace CrescentCompass.Core;

public enum PotFatePhase { Preparation, Running, Finished }
public sealed record PotFateDefinition(ushort Id, ushort Territory, string Side, string Name, ushort NextId, Vector3 Location);
public sealed record PotFateObservation(ushort Id, ushort Territory, PotFatePhase Phase, long StartTimeEpoch,
    int DurationSeconds, int Progress, Vector3 Position, string Name = "");
public sealed record PotFateLive(PotFateDefinition Definition, string Name, Vector3 Position, int Progress,
    bool Preparing, DateTimeOffset? EndsAt);
public sealed record PotFateSnapshot(IReadOnlyList<PotFateLive> Active, PotFateDefinition? Next,
    DateTimeOffset? ExpectedAt, bool UsesGameStart, bool ScanFresh, bool IsSharedEstimate = false);
public sealed record PotFateReminder(PotFateDefinition Definition, DateTimeOffset ExpectedAt);

/// <summary>Local observations with an optional entry-time seed. Estimates never prove a new spawn.</summary>
public sealed class PotFateTracker
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ReminderLead = TimeSpan.FromMinutes(5);
    public static IReadOnlyList<PotFateDefinition> Definitions { get; } = Array.AsReadOnly<PotFateDefinition>([
        // South: TC Fate.Location -> planevent.lgb InstanceId, mainland Map 967.
        new(1976, 1252, "北側", "幸福的魔法甕", 1977, new(200, 111.7266f, -215)),
        new(1977, 1252, "南側", "瑟瑟發抖的魔法甕", 1976, new(-481, 75, 528)),
        // North: retained BOCCHI zone data; not present in the audited TC client.
        new(2072, 1346, "北側", "Daylight Pottery", 2073, new(233, 7.729229f, -470)),
        new(2073, 1346, "南側", "In a Pot of Bother", 2072, new(-505.2822f, 53.14409f, 244.041f)),
    ]);
    private sealed class Occurrence(PotFateDefinition definition, DateTimeOffset start, bool gameTime)
    {
        public PotFateDefinition Definition { get; } = definition;
        public DateTimeOffset Start { get; set; } = start;
        public bool GameTime { get; set; } = gameTime;
        public bool Announced { get; set; }
        public bool ReminderHandled { get; set; }
        public bool Finished { get; set; }
    }
    private readonly Dictionary<ushort, Occurrence> latest = [];
    private Occurrence? sharedAnchor;
    public bool HasLocalAnchor => latest.Count > 0;
    private IReadOnlyList<PotFateLive> active = [];
    private DateTimeOffset? lastScan;
    private ushort territory;
    private uint instance;

    public static PotFateDefinition? Find(ushort id, ushort territory) => Definitions.FirstOrDefault(d => d.Id == id && d.Territory == territory);

    public void Reset()
    { latest.Clear(); sharedAnchor = null; active = []; lastScan = null; territory = 0; instance = 0; }

    public bool TrySeedSharedTime(ushort expectedTerritory, uint expectedInstance, ushort fateId, long spawnUnix, DateTimeOffset now)
    {
        if (SharedTimeRejection(expectedTerritory, expectedInstance, fateId, spawnUnix, now) is not null) return false;
        sharedAnchor = new(Find(fateId, territory)!, DateTimeOffset.FromUnixTimeSeconds(spawnUnix), true);
        return true;
    }

    public string? SharedTimeRejection(ushort expectedTerritory, uint expectedInstance, ushort fateId, long spawnUnix, DateTimeOffset now)
    {
        var current = now.ToUnixTimeSeconds();
        if (territory != expectedTerritory || instance != expectedInstance) return "計時器所在島嶼或分流已變更，拒絕舊場次回應。";
        if (HasLocalAnchor) return "已有本機觀測，不以共享資料覆蓋。";
        if (sharedAnchor is not null) return "本場已匯入共享時間，不重複匯入。";
        if (Find(fateId, territory) is null) return $"回應 FATE {fateId} 不是本島的魔法罐。";
        if (spawnUnix <= 0) return "回應開始時間無效（必須大於零）。";
        if (spawnUnix > current) return "回應開始時間在本機時間的未來；請確認系統時鐘。";
        if (spawnUnix <= current - (long)Interval.TotalSeconds) return "回應魔法罐紀錄已達 30 分鐘，剩餘時間已過期；不外推週期。";
        return null;
    }

    public IReadOnlyList<PotFateLive> Update(ushort currentTerritory, uint currentInstance,
        IEnumerable<PotFateObservation> observations, DateTimeOffset now, bool notify)
    {
        if (territory != currentTerritory || instance != currentInstance)
        { Reset(); territory = currentTerritory; instance = currentInstance; }
        if (!SpotCatalog.IsSupported(territory)) { Reset(); return []; }
        List<PotFateLive> live = [], notifications = [];
        foreach (var observation in observations.GroupBy(o => o.Id).Select(g => g.OrderByDescending(o => o.StartTimeEpoch).First()))
        {
            if (observation.Territory != territory || Find(observation.Id, territory) is not { } definition || !Enum.IsDefined(observation.Phase)) continue;
            var start = GameStart(observation.StartTimeEpoch, now);
            var ends = start is { } began && observation.DurationSeconds is > 0 and <= 3600 ? began.AddSeconds(observation.DurationSeconds) : (DateTimeOffset?)null;
            var running = observation.Phase != PotFatePhase.Finished && observation.Progress < 100 && (ends is null || ends > now);
            latest.TryGetValue(observation.Id, out var occurrence);
            // Invalid timestamps on a finished row cannot establish when that FATE started.
            if (occurrence is null && start is null && !running) continue;
            if (occurrence is not null && occurrence.GameTime && start is { } old && old < occurrence.Start) continue;
            var newRun = occurrence is null ||
                (occurrence.GameTime && start is { } newer && newer > occurrence.Start) ||
                (start is null && running && (occurrence.Finished || now - occurrence.Start >= Interval * 2));
            if (newRun)
            {
                occurrence = new Occurrence(definition, start ?? now, start is not null);
                occurrence.ReminderHandled = sharedAnchor is { ReminderHandled: true } shared &&
                    shared.Definition.Id == definition.Id && start == shared.Start;
                latest[observation.Id] = occurrence;
            }
            else if (start is { } corrected && !occurrence!.GameTime)
            { occurrence.Start = corrected; occurrence.GameTime = true; }
            occurrence!.Finished = !running;
            sharedAnchor = null;
            if (!running) continue;
            var item = new PotFateLive(definition, string.IsNullOrWhiteSpace(observation.Name) ? definition.Name : observation.Name,
                observation.Position, Math.Clamp(observation.Progress, 0, 100), observation.Phase == PotFatePhase.Preparation, ends);
            live.Add(item);
            if (occurrence.Announced) continue;
            occurrence.Announced = true; // Turning notifications on later must not replay a previously seen spawn.
            if (notify) notifications.Add(item);
        }
        active = live;
        lastScan = now;
        return notifications;
    }

    public PotFateSnapshot Snapshot(DateTimeOffset now)
    {
        var fresh = lastScan is { } scanned && now >= scanned && now - scanned <= TimeSpan.FromSeconds(3);
        var anchor = latest.Values.OrderByDescending(o => o.Start).FirstOrDefault() ?? sharedAnchor;
        return new(fresh ? active.Where(f => f.EndsAt is null || f.EndsAt > now).ToArray() : [],
            anchor is null ? null : Find(anchor.Definition.NextId, territory), anchor?.Start + Interval,
            anchor?.GameTime == true, fresh, anchor is not null && anchor == sharedAnchor);
    }

    /// <summary>Consume one reminder per observed cycle, including when muted; timestamp correction cannot replay it.</summary>
    public PotFateReminder? TakeUpcomingReminder(DateTimeOffset now, bool notify)
    {
        if (lastScan is not { } scanned || now < scanned || now - scanned > TimeSpan.FromSeconds(3)) return null;
        var anchor = latest.Values.OrderByDescending(o => o.Start).FirstOrDefault() ?? sharedAnchor;
        if (anchor is null || anchor.ReminderHandled) return null;
        var expected = anchor.Start + Interval;
        var remaining = expected - now;
        if (remaining <= TimeSpan.Zero || remaining > ReminderLead || Find(anchor.Definition.NextId, territory) is not { } next) return null;
        anchor.ReminderHandled = true;
        return notify ? new(next, expected) : null;
    }

    private static DateTimeOffset? GameStart(long epoch, DateTimeOffset now)
    {
        // Validate before conversion, including corrupted values outside DateTimeOffset's range.
        var current = now.ToUnixTimeSeconds();
        return epoch > 0 && epoch <= current && current - epoch <= 7200 ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
    }
}
