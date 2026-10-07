using System.Numerics;

namespace CrescentCompass.Core;

public sealed class CarrotEmptyCheck
{
    private string? id;
    private long? since, lastScan;
    private int scans;
    public void Reset() { id = null; since = lastScan = null; scans = 0; }
    public bool Update(Spot pad, IReadOnlyList<Observation> observations, Vector3 player, RouteLeg? path, bool canCheck, long now)
    {
        if (!canCheck || CarrotRoute.Number(pad) is null || !Coordinates.IsFinite(player) || !Coordinates.IsFinite(pad.Position) ||
            Vector3.DistanceSquared(player, pad.Position) > 36 || path is null || path.DestinationId != pad.Id ||
            !double.IsFinite(path.Length) || path.Length < 0 || !Coordinates.IsFinite(path.From) ||
            path.Length + Vector3.Distance(player, path.From) > 6 ||
            observations.Any(o => (o.Kind is SpotKind.Carrot or SpotKind.RabbitGold) && Coordinates.IsFinite(o.Position) &&
                Vector3.DistanceSquared(pad.Position, o.Position) <= (o.Kind == SpotKind.Carrot ? 16 : 100)))
        { Reset(); return false; }
        if (id != pad.Id || lastScan is not { } last || now <= last || now - last > 1500)
        { Reset(); id = pad.Id; since = now; }
        lastScan = now; scans++;
        return since is { } began && now - began >= 1500 && scans >= 3;
    }
}
