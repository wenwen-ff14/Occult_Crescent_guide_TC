using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CrescentCompass.Core;

internal static class BocchiRouteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Data/SouthHorn");
        using var catalogStream = File.OpenRead(Path.Combine(directory, "treasure_locations.json"));
        var catalog = SpotCatalog.Load(catalogStream);
        var authored = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "treasure_route.json")))!;
        var costs = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "precomputed_treasure_hunt_data.json")))!;
        BocchiPatrolRoute Load(JsonNode? route = null, JsonNode? distances = null, IEnumerable<Spot>? spots = null)
        {
            using var routeStream = new MemoryStream(Encoding.UTF8.GetBytes((route ?? authored).ToJsonString()));
            using var costStream = new MemoryStream(Encoding.UTF8.GetBytes((distances ?? costs).ToJsonString()));
            return BocchiPatrolRoute.Load(routeStream, costStream, spots ?? catalog);
        }
        var bocchi = Load();
        var ordered = bocchi.Order(bocchi.FirstChartNumber);
        var ids = authored["segments"]!.AsArray().SelectMany(s => s!["nodes"]!.AsArray()).Select(n => n!.GetValue<uint>()).ToArray();
        check(bocchi.SegmentCount == 7 && ordered.Count == 68 && ordered.Select(s => s.DataId).SequenceEqual(ids),
            "Imported route follows all seven upstream segments and exactly 68 authored node IDs");
        check(ordered.Select(s => s.Id).ToHashSet().SetEquals(catalog.Select(s => s.Id)) && ordered.All(s => !s.RequiresTower),
            "BOCCHI nodes map one-to-one to the existing verified outdoor catalog");
        check(ordered[0].DataId == 1804 && ordered[^1].DataId == 1796 && bocchi.FirstChartNumber == 21,
            "Native BOCCHI entry and end map to unchanged chart IDs");
        var shuffled = Load(spots: catalog.Reverse());
        for (var number = 1; number <= 68; number++)
        {
            var rotation = bocchi.Order(number);
            var index = Array.FindIndex(ordered.ToArray(), s => ChestChart.Number(s) == number);
            check(rotation.Select(s => s.Id).SequenceEqual(ordered.Skip(index).Concat(ordered.Take(index)).Select(s => s.Id)),
                $"Start #{number} rotates the BOCCHI order without sorting by chart number");
            check(rotation.Select(s => s.Id).Distinct().Count() == 68 && ChestChart.Number(rotation[0]) == number,
                "Every selected chart start makes one complete tour without duplicates");
            check(bocchi.NextChartNumber(number) == ChestChart.Number(rotation[1]) &&
                shuffled.Order(number).SequenceEqual(rotation), "Continuation and shuffled catalog mapping preserve authored successors");
        }
        check(bocchi.NextChartNumber(49) == 21 && bocchi.NextChartNumber(21) != 22,
            "Last-open continuation wraps the BOCCHI cycle rather than incrementing chart labels");
        for (var i = 0; i < ordered.Count; i++)
        {
            var region = bocchi.Region(ordered[i]);
            check(region is { Count: 7 } && region.Index is >= 1 and <= 7 &&
                authored["segments"]![region.Index - 1]!["nodes"]!.AsArray().Any(n => n!.GetValue<uint>() == ordered[i].DataId),
                "Region display follows the actual node even when a tour is rotated");
        }
        var forward = bocchi.ReferenceDistance([catalog[0], catalog[1]]);
        var backward = bocchi.ReferenceDistance([catalog[1], catalog[0]]);
        check(forward == 1286.7151 && backward == 1275.9323, "Reference distances preserve upstream directionality");
        check(bocchi.ReferenceDistance([]) == 0 && bocchi.ReferenceDistance([ordered[0]]) == 0,
            "Station-to-station reference excludes approach and closing-loop costs");
        var triple = ordered.Take(3).ToArray();
        check(bocchi.ReferenceDistance(triple) == bocchi.ReferenceDistance(triple[..2]) + bocchi.ReferenceDistance(triple[1..]),
            "Reference route cost sums only remaining consecutive legs");
        var unknown = ordered[0] with { Id = "unknown", Territory = 1346 };
        check(bocchi.ReferenceDistance([unknown]) is null && bocchi.Region(unknown) is null && bocchi.Region(null) is null,
            "Unmapped references remain unknown rather than inventing reachability");
        var returned = (Spot[])bocchi.Order(1); returned[0] = unknown;
        check(ChestChart.Number(bocchi.Order(1)[0]) == 1, "Callers cannot mutate the stored authored order");
        var session = new SurveySession(); session.Reset(1252, catalog);
        session.Visit(ordered[0].Id); session.Visit(ordered[5].Id);
        var remaining = bocchi.Order(bocchi.FirstChartNumber).Where(s => session.CanPatrol(s.Id)).ToArray();
        check(remaining.Length == 66 && remaining.SequenceEqual(ordered.Where(s => s != ordered[0] && s != ordered[5])),
            "Existing visited evidence is honored without reordering unvisited BOCCHI stops");
        var queries = 0; var cache = new PatrolPathCache();
        Task<IReadOnlyList<Vector3>> Find(Vector3 a, Vector3 b, CancellationToken token) => cache.FindPath(a, b, (from, to, _) =>
        { queries++; return Task.FromResult<IReadOnlyList<Vector3>>([from, to]); }, token);
        var plan = await WalkingRoutePlanner.PlanOrderedAsync(ordered[0].Position, ordered, Find);
        await WalkingRoutePlanner.PlanOrderedAsync(ordered[0].Position, ordered, Find);
        check(plan.Route.Stops.SequenceEqual(ordered) && queries == 68,
            "BOCCHI order uses actual mesh queries and reuses fixed leg cache on repeat");
        var noMesh = await WalkingRoutePlanner.PlanOrderedAsync(Vector3.Zero, triple,
            (_, _, _) => Task.FromResult<IReadOnlyList<Vector3>>([]));
        check(noMesh.Legs.Count == 0 && noMesh.Unreachable.Count == 3 && bocchi.ReferenceDistance(triple) > 0,
            "Positive imported distance never substitutes for a missing mesh path");
        var sparse = costs.DeepClone(); sparse["NodeToNodeDistances"]!.AsObject().Remove("1789");
        var sparseRoute = Load(distances: sparse);
        check(sparseRoute.Order(1).Count == 68 && sparseRoute.ReferenceDistance([catalog[0], catalog[1]]) is null,
            "Missing upstream reference costs do not discard route stops or become made-up distances");
        check(bocchi.ReferenceDistance([catalog.Single(s => s.DataId == 1856), catalog[3]]) is null,
            "The upstream sparse 1856 row remains unknown outside its three recorded connections");
        check(bocchi.ReferenceCosts(ordered) is { TotalLegs: 67, KnownLegs: 66, Distance: > 0 },
            "Partial upstream totals explicitly retain the count of known versus total legs");

        void Reject(Action<JsonNode> mutate, bool distance = false)
        {
            var changed = (distance ? costs : authored).DeepClone(); mutate(changed);
            try { Load(distance ? authored : changed, distance ? changed : costs); check(false, "Malformed import must fail closed"); }
            catch (InvalidDataException) { check(true, "Malformed BOCCHI data fails closed"); }
        }
        Reject(n => n["schemaVersion"] = 1);
        Reject(n => n["territoryId"] = 1346);
        Reject(n => n["zone"] = "NorthHorn");
        Reject(n => n["segments"] = new JsonArray());
        Reject(n => n["segments"]![0]!["id"] = "");
        Reject(n => n["segments"]![1]!["id"] = n["segments"]![0]!["id"]!.DeepClone());
        Reject(n => n["segments"]![0]!["nodes"] = new JsonArray());
        Reject(n => n["segments"]![0]!["nodes"]!.AsArray().RemoveAt(0));
        Reject(n => n["segments"]![0]!["nodes"]![0] = 9999);
        Reject(n => n["segments"]![0]!["nodes"]![0] = n["segments"]![0]!["nodes"]![1]!.DeepClone());
        Reject(n => n["NodeToNodeDistances"] = new JsonObject(), true);
        Reject(n => n["NodeToNodeDistances"]!["1789"]![0]!["Distance"] = -1, true);
        Reject(n => n["NodeToNodeDistances"]!["1789"]![0]!["Id"] = 1789, true);
        Reject(n => n["NodeToNodeDistances"]!["1789"]![1]!["Id"] = 1790, true);
        foreach (var badStart in new[] { 0, 69 })
        {
            try { bocchi.Order(badStart); check(false, "Invalid start must throw"); }
            catch (ArgumentOutOfRangeException) { check(true, "Invalid chart start is rejected"); }
            try { bocchi.NextChartNumber(badStart); check(false, "Invalid continuation must throw"); }
            catch (ArgumentOutOfRangeException) { check(true, "Invalid continuation is rejected"); }
        }
        try { Load(spots: catalog.Skip(1)); check(false, "Missing catalog point must throw"); }
        catch (InvalidDataException) { check(true, "Partial catalog cannot silently shorten the imported tour"); }
        try { Load(spots: catalog.Select((s, i) => i == 0 ? s with { DataId = catalog[1].DataId } : s)); check(false, "Ambiguous node must throw"); }
        catch (InvalidDataException) { check(true, "Duplicate live node IDs cannot ambiguously map to chart positions"); }
    }
}
