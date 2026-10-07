namespace CrescentCompass.Core;

public static class CarrotRoute
{
    public const ushort Territory = 1252;
    public const int Count = 25;
    // User's 1-25 chart -> existing catalog IDs. Catalog identity and coordinates stay unchanged.
    private static readonly int[] CatalogIds = [22, 11, 1, 10, 21, 9, 8, 3, 4, 7, 12, 5, 24, 6, 13, 20, 17, 2, 14, 19, 18, 15, 23, 25, 16];
    public static int? Number(Spot? spot)
    {
        if (spot is not { Territory: Territory, Kind: SpotKind.Carrot }) return null;
        var index = Array.FindIndex(CatalogIds, id => spot.Id == $"{Territory}:Carrot:{id}");
        return index < 0 ? null : index + 1;
    }

    public static IReadOnlyList<Spot> Order(IEnumerable<Spot> catalog, int start = 1)
    {
        var points = catalog.Where(s => Number(s) is not null).OrderBy(s => Number(s)).ToArray();
        if (points.Length != Count || points.Select(s => s.Id).Distinct().Count() != Count || points.Any(s => !Coordinates.IsFinite(s.Position)))
            throw new InvalidDataException("Expected all 25 unique South Horn carrot pads.");
        var index = Math.Clamp(start, 1, Count) - 1;
        return points.Skip(index).Concat(points.Take(index)).ToArray();
    }
}

/// <summary>Local search scores under the user's two-carrot respawn model, not normalized probabilities.</summary>
public sealed class CarrotSearchWeights
{
    private readonly Dictionary<string, int> weights = [];
    private readonly HashSet<ulong> confirmedEvents = [];
    public int Pickups { get; private set; }
    public int Revision { get; private set; }
    public void Reset(IEnumerable<Spot> pads)
    {
        weights.Clear(); confirmedEvents.Clear(); Pickups = 0;
        foreach (var pad in pads.Where(s => CarrotRoute.Number(s) is not null)) weights[pad.Id] = 2;
        Revision++;
    }
    public int? Weight(string id) => weights.TryGetValue(id, out var value) ? value : null;
    public void CheckEmpty(string id)
    {
        if (weights.TryGetValue(id, out var weight) && weight != 0) { weights[id] = 0; Revision++; }
    }
    public bool ConfirmReportedPickup(string id, int expectedRevision) => expectedRevision == Revision && ConfirmPickup(id);
    public bool ConfirmPickup(string id, ulong eventToken = 0)
    {
        if (!weights.ContainsKey(id) || eventToken != 0 && !confirmedEvents.Add(eventToken)) return false;
        weights[id] = 0;
        foreach (var key in weights.Keys.ToArray()) weights[key] = Math.Min(2, weights[key] + 1);
        Pickups++; Revision++;
        return true;
    }
}
