using System.Globalization;
using System.Text.Json;

namespace CrescentCompass.Core;

public enum PatrolRouteKind { Bocchi, Chart, Carrot }
public sealed record PatrolRegion(int Index, int Count, string Id);
public sealed record PatrolReferenceCost(double Distance, int KnownLegs, int TotalLegs);

/// <summary>Authored stop order and reference costs only; never supplies movement or absence-check geometry.</summary>
public sealed class BocchiPatrolRoute
{
    public const string SourceCommit = "400de1ca05a327f016978168560d511e74ff5eed";
    private readonly Spot[] stops;
    private readonly Dictionary<string, PatrolRegion> regions;
    private readonly Dictionary<(string From, string To), double> distances;
    private readonly Dictionary<int, int> chartIndices;
    public int SegmentCount { get; }
    public int FirstChartNumber => ChestChart.Number(stops[0])!.Value;

    private BocchiPatrolRoute(Spot[] stops, Dictionary<string, PatrolRegion> regions,
        Dictionary<(string, string), double> distances, int segmentCount)
    {
        this.stops = stops; this.regions = regions; this.distances = distances; SegmentCount = segmentCount;
        chartIndices = stops.Select((s, i) => (Number: ChestChart.Number(s)!.Value, Index: i)).ToDictionary(p => p.Number, p => p.Index);
    }

    public static BocchiPatrolRoute Load(Stream routeStream, Stream distanceStream, IEnumerable<Spot> catalog)
    {
        var chart = ChestChart.Order(catalog, 1);
        if (chart.Select(s => s.DataId).Distinct().Count() != ChestChart.Count)
            throw new InvalidDataException("Ambiguous BOCCHI node mapping.");
        var byNode = chart.ToDictionary(s => s.DataId);
        using var route = JsonDocument.Parse(routeStream);
        var root = route.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2 ||
            root.GetProperty("territoryId").GetUInt16() != ChestChart.Territory || root.GetProperty("zone").GetString() != "SouthHorn")
            throw new InvalidDataException("Unsupported BOCCHI route schema or territory.");
        var segments = root.GetProperty("segments").EnumerateArray().ToArray();
        if (segments.Length is < 1 or > ChestChart.Count) throw new InvalidDataException("Invalid BOCCHI segment count.");
        var segmentIds = new HashSet<string>(StringComparer.Ordinal);
        List<Spot> ordered = [];
        Dictionary<string, PatrolRegion> regions = [];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var id = segment.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || !segmentIds.Add(id))
                throw new InvalidDataException("Invalid or duplicate BOCCHI segment ID.");
            var nodes = segment.GetProperty("nodes").EnumerateArray().ToArray();
            if (nodes.Length == 0) throw new InvalidDataException("Empty BOCCHI segment.");
            foreach (var node in nodes)
            {
                if (!byNode.TryGetValue(node.GetUInt32(), out var spot) || !regions.TryAdd(spot.Id, new(i + 1, segments.Length, id)))
                    throw new InvalidDataException("Unknown or repeated BOCCHI node.");
                ordered.Add(spot);
            }
        }
        if (ordered.Count != ChestChart.Count) throw new InvalidDataException("Incomplete BOCCHI route.");

        using var costs = JsonDocument.Parse(distanceStream);
        Dictionary<(string, string), double> distances = [];
        var fromNodes = new HashSet<uint>();
        foreach (var row in costs.RootElement.GetProperty("NodeToNodeDistances").EnumerateObject())
        {
            if (!uint.TryParse(row.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var from) ||
                !byNode.TryGetValue(from, out var fromSpot) || !fromNodes.Add(from))
                throw new InvalidDataException("Invalid BOCCHI distance origin.");
            foreach (var edge in row.Value.EnumerateArray())
            {
                var to = edge.GetProperty("Id").GetUInt32();
                var distance = edge.GetProperty("Distance").GetDouble();
                if (from == to || !byNode.TryGetValue(to, out var toSpot) || !double.IsFinite(distance) || distance <= 0 ||
                    !distances.TryAdd((fromSpot.Id, toSpot.Id), distance))
                    throw new InvalidDataException("Invalid or duplicate BOCCHI distance.");
            }
        }
        // Upstream currently has only three outgoing costs for node 1856. Missing costs stay unknown.
        if (distances.Count == 0) throw new InvalidDataException("Empty BOCCHI distance table.");
        return new(ordered.ToArray(), regions, distances, segments.Length);
    }

    public IReadOnlyList<Spot> Order(int startChartNumber)
    {
        if (!chartIndices.TryGetValue(startChartNumber, out var start)) throw new ArgumentOutOfRangeException(nameof(startChartNumber));
        return Enumerable.Range(0, stops.Length).Select(i => stops[(start + i) % stops.Length]).ToArray();
    }

    public int NextChartNumber(int number)
    {
        if (!chartIndices.TryGetValue(number, out var index)) throw new ArgumentOutOfRangeException(nameof(number));
        return ChestChart.Number(stops[(index + 1) % stops.Length])!.Value;
    }

    public PatrolRegion? Region(Spot? spot) => ChestChart.Number(spot) is not null && regions.TryGetValue(spot!.Id, out var region) ? region : null;

    // Excludes the player's approach and the return to the first stop. This is not evidence of traversability.
    public double? ReferenceDistance(IReadOnlyList<Spot> remaining)
    {
        var cost = ReferenceCosts(remaining);
        return cost is not null && cost.KnownLegs == cost.TotalLegs ? cost.Distance : null;
    }

    public PatrolReferenceCost? ReferenceCosts(IReadOnlyList<Spot> remaining)
    {
        if (remaining.Any(s => Region(s) is null)) return null;
        var total = 0d;
        var known = 0;
        for (var i = 1; i < remaining.Count; i++)
        {
            if (distances.TryGetValue((remaining[i - 1].Id, remaining[i].Id), out var distance))
            { total += distance; known++; }
        }
        return new(total, known, Math.Max(0, remaining.Count - 1));
    }
}
