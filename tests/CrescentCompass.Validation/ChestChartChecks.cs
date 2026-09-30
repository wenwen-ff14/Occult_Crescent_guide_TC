using System.Numerics;
using System.Text.Json;
using CrescentCompass.Core;

internal static class ChestChartChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data/SouthHorn/treasure_locations.json"));
        var catalog = SpotCatalog.Load(stream);
        var chart = ChestChart.Order(catalog, 1);
        check(chart.Count == 68 && chart.Select(s => s.Id).Distinct().Count() == 68 && chart.Select(s => s.Id).ToHashSet().SetEquals(catalog.Select(s => s.Id)), "Chart covers each of the 68 TC outdoor chests exactly once");
        check(chart.Where(s => s.Kind == SpotKind.Silver).Select(s => ChestChart.Number(s)).SequenceEqual(new int?[] { 10, 24, 29, 40, 43, 49, 61, 66 }), "All eight blue chart numbers match silver chests");
        check(chart[0].DataId == 1797 && chart[14].DataId == 1856 && chart[52].DataId == 1833 && chart[^1].DataId == 1843, "Chart corner and close-neighbour labels map to the verified game IDs");
        for (var start = 1; start <= 68; start++)
        {
            var rotation = ChestChart.Order(catalog.Reverse(), start);
            check(rotation.Select(s => ChestChart.Number(s)).SequenceEqual(Enumerable.Range(0, 68).Select(i => (int?)((start - 1 + i) % 68 + 1))), $"Chart starting at {start} follows one complete fixed-order rotation");
        }
        check(ChestChart.Number(chart[0] with { Territory = 1346 }) is null && ChestChart.Number(null) is null, "North and missing points have no south chart number");
        check(ChestChart.Next(68) == 1 && ChestChart.Next(24) == 25, "Last-open continuation wraps exactly once");
        try { ChestChart.Order(catalog, 0); check(false, "Reject chart zero"); } catch (ArgumentOutOfRangeException) { check(true, "Reject chart zero"); }
        try { ChestChart.Order(catalog.Skip(1), 1); check(false, "Reject incomplete chart"); } catch (InvalidDataException) { check(true, "Reject incomplete chart"); }

        var ordered = new[] { chart[23], chart[24], chart[25] };
        var calls = new List<(Vector3 From, Vector3 To)>();
        var route = await WalkingRoutePlanner.PlanOrderedAsync(Vector3.Zero, ordered, (from, to, _) =>
        {
            calls.Add((from, to));
            return Task.FromResult<IReadOnlyList<Vector3>>(calls.Count == 2 ? [] : [from, from + Vector3.UnitY * 20, to]);
        });
        check(route.Route.Stops.SequenceEqual(ordered) && calls.Count == 3, "Ordered walking route never optimizes away chart order");
        check(route.Unreachable.Single() == ordered[1] && route.Route.Stops.Contains(ordered[1]) && route.Legs.Count == 2, "Missing ground segment retains the numbered stop without a fake straight line");
        check(calls[2].From == ordered[1].Position && calls[2].To == ordered[2].Position, "Missing segment cannot silently bridge or skip a point");
        check(!route.Route.Exact, "Chart order never advertised as global shortest tour");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await WalkingRoutePlanner.PlanOrderedAsync(Vector3.Zero, ordered, (_, _, _) => throw new Exception("Must not query"), cancellation: cancelled.Token); check(false, "Cancelled plan aborts"); }
        catch (OperationCanceledException) { check(true, "Cancelled ordered plan aborts before navigation"); }

        var session = new SurveySession(); session.Reset(1252, ordered);
        var remaining = ordered.ToList(); var automation = new RouteAutomation();
        automation.ScheduleFlag(ordered[0].Id); automation.SetPaused(true);
        session.Visit(ordered[0].Id);
        var paused = automation.Update(session, remaining, [], ordered[0].Position, 9000, PointDisplayMode.Candidates, true);
        check(paused.Removed == 0 && remaining.Count == 3 && !automation.CanAttemptFlag(ordered[0].Id, 9000), "Pause freezes pruning, current stop, absence checks and flags");
        automation.Reset();
        check(automation.Paused, "Transit/reset of automation does not undo user pause");
        automation.SetPaused(false);
        automation.Update(session, remaining, [], ordered[0].Position, 9500, PointDisplayMode.Candidates, true);
        check(remaining[0] == ordered[1] && automation.CanAttemptFlag(ordered[1].Id, 9500), "Resume processes observations only after unpausing");
        session.Reset(1252, ordered); remaining = ordered.ToList();
        automation.Update(session, remaining, [], ordered[0].Position, 10000, PointDisplayMode.Candidates, true);
        automation.SetPaused(true); automation.SetPaused(false);
        check(automation.Update(session, remaining, [], ordered[0].Position, 13000, PointDisplayMode.Candidates, true).Reason == RouteAdvanceReason.None, "Time spent paused cannot count toward empty-point timer");

        var tracker = new ChestOpenTracker();
        var chest = new Observation(77, chart[23].DataId, SpotKind.Silver, chart[23].Position);
        var now = DateTimeOffset.UtcNow;
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 0, now) is null, "Initially spent chest is not a personal open");
        tracker.Update(catalog, [chest], chest.Position, 77, 500, now);
        check(tracker.Update(catalog, [], chest.Position, 0, 1000, now) is null, "Disappearance does not record opening");
        var progress = tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 1500, now);
        check(progress is { Number: 24, Manual: false, ChartId: ChestChart.Id }, "Own cast followed by exact chest spent transition records fixed chart number");
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 2000, now) is null, "Same spent chest records only once");
        tracker.Update(catalog, [chest], chest.Position, 77, 3000, now);
        check(tracker.Update(catalog, [chest with { Available = false, ObjectId = 78 }], chest.Position, 0, 3500, now) is null, "Recycled/different object cannot confirm opening");
        tracker.Reset();
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 4000, now) is null, "Teleport resets personal opening evidence");
        tracker.Update(catalog, [chest], chest.Position, 77, 4100, now);
        tracker.Update(catalog, [chest], chest.Position, 0, 4200, now);
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 4400, now) is null, "Cancelled local cast cannot claim a subsequently opened chest");
        tracker.Update(catalog, [chest], chest.Position, 77, 5000, now);
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 14000, now) is null, "Expired/cancelled attempt cannot later claim another open");
        tracker.Update(catalog, [chest], chest.Position + Vector3.UnitX * 30, 77, 15000, now);
        check(tracker.Update(catalog, [chest with { Available = false }], chest.Position, 0, 15500, now) is null, "Distant cast cannot claim opening");
        var saved = new Dictionary<string, ChestOpenProgress> { ["character-a"] = progress! };
        var restored = JsonSerializer.Deserialize<Dictionary<string, ChestOpenProgress>>(JsonSerializer.Serialize(saved))!;
        check(restored["character-a"] == progress && !restored.ContainsKey("character-b"), "Persistent progress retains number, time, provenance and separates character keys");

        var map = new MapViewport(); var size = new Vector2(700, 240); var cursor = new Vector2(120, 70);
        map.Fit(chart.Select(s => new Vector2(s.Position.X, s.Position.Z)));
        var world = map.Unproject(cursor, size);
        map.ZoomAt(3, cursor, size);
        check(Vector2.Distance(map.Project(world, size), cursor) < 0.001f && map.Zoom > 1, "Ctrl-wheel zoom remains anchored at cursor");
        map.Pan(new(40, -20), size);
        check(Vector2.Distance(map.Project(world, size), cursor + new Vector2(40, -20)) < 0.001f, "Map drag translates screen coordinates precisely");
        for (var i = 0; i < 10; i++) map.ZoomAt(20, cursor, size);
        check(map.Zoom == 12, "Map zoom upper bound");
        for (var i = 0; i < 10; i++) map.ZoomAt(-20, cursor, size);
        check(map.Zoom == 0.5f, "Map zoom lower bound");
        map.Fit([]); check(map.Zoom == 1 && float.IsFinite(map.Project(Vector2.Zero, size).X), "Full-map reset handles empty routes");
        map.Fit([new(1, 1)]); check(float.IsFinite(map.Project(new(1, 1), size).Y), "Single-point map has finite projection");
    }
}
