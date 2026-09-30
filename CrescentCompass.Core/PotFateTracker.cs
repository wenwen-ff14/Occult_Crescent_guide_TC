using System.Numerics;

namespace CrescentCompass.Core;

public enum PotFatePhase { Preparation, Running, Finished }
public sealed record PotFateDefinition(ushort Id, ushort Territory, string Side, string Name, ushort NextId, Vector3 Location);
public sealed record PotFateObservation(ushort Id, ushort Territory, PotFatePhase Phase, long StartTimeEpoch,
    int DurationSeconds, int Progress, Vector3 Position, string Name = "");
public sealed record PotFateLive(PotFateDefinition Definition, string Name, Vector3 Position, int Progress,
    bool Preparing, DateTimeOffset? EndsAt);
public sealed record PotFateSnapshot(IReadOnlyList<PotFateLive> Active, PotFateDefinition? Next,
    DateTimeOffset? ExpectedAt, bool UsesGameStart, bool ScanFresh);

/// <summary>Local observations only. A 30-minute estimate never becomes evidence of a new spawn.</summary>
public sealed class PotFateTracker
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
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
        public bool Finished { get; set; }
    }
    private readonly Dictionary<ushort, Occurrence> latest = [];
    private IReadOnlyList<PotFateLive> active = [];
    private DateTimeOffset? lastScan;
    private ushort territory;
    private uint instance;

    public static PotFateDefinition? Find(ushort id, ushort territory) => Definitions.FirstOrDefault(d => d.Id == id && d.Territory == territory);

    public void Reset()
    { latest.Clear(); active = []; lastScan = null; territory = 0; instance = 0; }

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
                latest[observation.Id] = occurrence;
            }
            else if (start is { } corrected && !occurrence!.GameTime)
            { occurrence.Start = corrected; occurrence.GameTime = true; }
            occurrence!.Finished = !running;
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
        var anchor = latest.Values.OrderByDescending(o => o.Start).FirstOrDefault();
        return new(fresh ? active.Where(f => f.EndsAt is null || f.EndsAt > now).ToArray() : [],
            anchor is null ? null : Find(anchor.Definition.NextId, territory), anchor?.Start + Interval,
            anchor?.GameTime == true, fresh);
    }

    private static DateTimeOffset? GameStart(long epoch, DateTimeOffset now)
    {
        // Validate before conversion, including corrupted values outside DateTimeOffset's range.
        var current = now.ToUnixTimeSeconds();
        return epoch > 0 && epoch <= current && current - epoch <= 7200 ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
    }
}
