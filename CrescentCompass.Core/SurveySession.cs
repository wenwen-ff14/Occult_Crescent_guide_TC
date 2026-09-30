using System.Numerics;

namespace CrescentCompass.Core;

public sealed class SurveySession
{
    private readonly Dictionary<string, Spot> spots = [];
    private readonly Dictionary<string, DateTimeOffset> lastSeen = [];
    private readonly HashSet<string> visible = [];
    private readonly HashSet<string> observedTargets = [];
    private readonly HashSet<string> visited = [];
    private readonly HashSet<string> spent = [];
    private readonly HashSet<string> skipped = [];
    private readonly Dictionary<string, ulong> objectIds = [];
    private int discoveryId;

    public ushort Territory { get; private set; }
    public int Revision { get; private set; }
    public ExplorationProgress Exploration { get; } = new();
    public bool OnlyUnexplored { get; set; }

    public void Reset(ushort territory, IEnumerable<Spot> catalog)
    {
        Territory = territory;
        spots.Clear();
        lastSeen.Clear();
        visible.Clear();
        observedTargets.Clear();
        visited.Clear();
        spent.Clear();
        skipped.Clear();
        objectIds.Clear();
        Exploration.Reset();
        discoveryId = 0;
        foreach (var spot in catalog.Where(s => s.Territory == territory)) spots.Add(spot.Id, spot);
        Revision++;
    }

    public void Observe(IEnumerable<Observation> observations, DateTimeOffset now)
    {
        if (!SpotCatalog.IsSupported(Territory)) return;
        var nextVisible = new HashSet<string>();
        foreach (var observation in observations)
        {
            if (!Coordinates.IsFinite(observation.Position)) continue;
            var match = spots.Values
                .Where(s => s.Kind == observation.Kind && Vector3.DistanceSquared(s.Position, observation.Position) <= 16f)
                .OrderBy(s => Vector3.DistanceSquared(s.Position, observation.Position)).FirstOrDefault();
            if (match is null)
            {
                match = new Spot($"{Territory}:discovered:{++discoveryId}", Territory, observation.Kind, observation.DataId, observation.Position);
                spots[match.Id] = match;
                Revision++;
            }
            if (match.Position != observation.Position)
                spots[match.Id] = match with { Position = observation.Position };
            // A new object at a previously visited pad may be a respawn. Unloading alone is not evidence of a respawn.
            if (objectIds.TryGetValue(match.Id, out var previousId) && previousId != observation.ObjectId)
            {
                spent.Remove(match.Id);
                if (visited.Remove(match.Id)) Revision++;
            }
            objectIds[match.Id] = observation.ObjectId;
            lastSeen[match.Id] = now;
            if (observation.Available && observation.Targetable)
            {
                nextVisible.Add(match.Id); observedTargets.Add(match.Id);
                if (skipped.Remove(match.Id)) Revision++;
                if (spent.Remove(match.Id) && visited.Remove(match.Id)) Revision++;
            }
            else if (!observation.Available)
            {
                spent.Add(match.Id);
                if (visited.Add(match.Id)) Revision++;
            }
        }
        if (!visible.SetEquals(nextVisible)) Revision++;
        visible.Clear();
        visible.UnionWith(nextVisible);
    }

    private TrackedSpot Track(Spot s) => new(s,
        visited.Contains(s.Id) ? SpotStatus.Visited : skipped.Contains(s.Id) ? SpotStatus.Skipped : visible.Contains(s.Id) ? SpotStatus.Visible :
        s.Kind == SpotKind.Exploration ? Exploration.State(s) switch
        { ExplorationState.Explored => SpotStatus.Explored, ExplorationState.Unexplored => SpotStatus.Unexplored, _ => SpotStatus.Location }
        : lastSeen.ContainsKey(s.Id) ? SpotStatus.LastSeen : SpotStatus.Candidate,
        lastSeen.TryGetValue(s.Id, out var time) ? time : null);

    public IReadOnlyList<TrackedSpot> Snapshot() => spots.Values.Select(Track).ToArray();
    public TrackedSpot? Get(string id) => spots.TryGetValue(id, out var spot) ? Track(spot) : null;
    public bool CanPatrol(string id) => Get(id) is { Status: not (SpotStatus.Visited or SpotStatus.Skipped) } tracked && Exploration.Allows(tracked.Spot, OnlyUnexplored);
    public bool CanDisplay(string id, bool visibleOnly) => CanDisplay(id, visibleOnly ? PointDisplayMode.Visible : PointDisplayMode.Candidates);
    public bool CanDisplay(string id, PointDisplayMode mode) => Get(id) is { } tracked && CanPatrol(id) &&
        (tracked.Spot.Kind == SpotKind.Exploration || mode switch
        {
            PointDisplayMode.Observed => observedTargets.Contains(id),
            PointDisplayMode.Visible => tracked.Status == SpotStatus.Visible,
            PointDisplayMode.Candidates => true,
            _ => false,
        });
    public void Visit(string id) { if (spots.ContainsKey(id) && visited.Add(id)) Revision++; }
    public bool UpdateExploration(IReadOnlyDictionary<uint, bool> snapshot)
    {
        if (!Exploration.Update(snapshot)) return false;
        Revision++;
        return true;
    }
    public void Skip(string id) { if (spots.TryGetValue(id, out var spot) && CofferKinds.IsCoffer(spot.Kind) && !visited.Contains(id) && skipped.Add(id)) Revision++; }
    // A new patrol can revisit manually completed/empty pads, but never re-advertises a confirmed opened chest.
    public void RestartSurvey() { visited.IntersectWith(spent); skipped.Clear(); Revision++; }
}
