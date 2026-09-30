using System.Numerics;
using CrescentCompass.Core;

internal static class WalkingRouteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static Spot Point(string id, int x, int z) => new(id, 1252, SpotKind.Silver, 1, new(x, 0, z));
        var start = Vector3.Zero;
        var acrossWall = Point("across-wall", 2, 0);
        var sameSide = Point("same-side", 0, 5);
        var calls = new HashSet<(Vector3, Vector3)>();
        var progress = new List<WalkingProgress>();
        Task<IReadOnlyList<Vector3>> Grid(Vector3 from, Vector3 to, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            check(calls.Add((from, to)), "A directed ground query is cached within the request");
            var queue = new Queue<Vector3>(); queue.Enqueue(from);
            var parents = new Dictionary<Vector3, Vector3?> { [from] = null };
            while (queue.TryDequeue(out var here))
            {
                if (here == to)
                {
                    var path = new List<Vector3>();
                    for (Vector3? p = to; p is not null; p = parents[p.Value]) path.Add(p.Value);
                    path.Reverse(); return Task.FromResult<IReadOnlyList<Vector3>>(path);
                }
                foreach (var step in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                {
                    var next = here + step;
                    if (next.X < -1 || next.X > 4 || next.Z < -1 || next.Z > 10 || next.X == 1 && next.Z < 10 || parents.ContainsKey(next)) continue;
                    parents[next] = here; queue.Enqueue(next);
                }
            }
            return Task.FromResult<IReadOnlyList<Vector3>>([]);
        }
        var route = await WalkingRoutePlanner.PlanAsync(start, [acrossWall, sameSide], new HashSet<string>(), Grid, progress.Add);
        check(route.Route.Stops[0] == sameSide && RoutePlanner.Plan(start, [acrossWall, sameSide]).Stops[0] == acrossWall,
            "A nearby chest across a wall no longer beats the accessible side of the wall");
        check(route.Route.Length == 22 && route.Legs.Sum(l => l.Length) == 22, "Distance sums the walk around the wall, not endpoint chords");
        check(route.Legs.All(l => l.Path.All(p => p.X != 1 || p.Z == 10)), "Rendered paths respect the same wall used for ordering");
        check(progress.Count > 0 && progress[^1].Phase == "完成" && route.Route.Exact, "Small complete route is exact under queried ground-path costs");
        calls.Clear();
        var priority = await WalkingRoutePlanner.PlanAsync(start, [acrossWall, sameSide], new HashSet<string> { acrossWall.Id }, Grid);
        check(priority.Route.Stops[0] == acrossWall, "Unfinished priority survives terrain optimization even when farther to walk");

        static Task<IReadOnlyList<Vector3>> Missing(Vector3 from, Vector3 to, CancellationToken token) => Task.FromResult<IReadOnlyList<Vector3>>([]);
        var missing = await WalkingRoutePlanner.PlanAsync(start, [acrossWall], new HashSet<string>(), Missing);
        check(missing.Route.Stops.Count == 0 && missing.Unreachable.Single() == acrossWall && missing.Legs.Count == 0,
            "Missing ground paths are listed explicitly without straight-line fallback");
        check(WalkingRoutePlanner.ValidatePath("partial", start, new(30, 0, 0), [start, new(5, 0, 0)]) is null,
            "A partial path is not accepted as arrival");
        check(WalkingRoutePlanner.ValidatePath("floor", start, new(0, 20, 0), [start, new(0, 1, 0)]) is null,
            "Wrong-floor path is rejected");
        check(WalkingRoutePlanner.ValidatePath("invalid", start, start, [start, new(float.NaN, 0, 0), start]) is null,
            "Nonfinite navigation data never enters the solver");
        check(WalkingRoutePlanner.ValidatePath("no-start", start, new(20, 0, 0), [new(10, 0, 0), new(20, 0, 0)]) is null,
            "Missing origin is not connected with a fabricated long straight segment");

        // Synthetic directed roads: the same endpoints have different lengths on the return trip.
        var nodes = Enumerable.Range(1, 18).Select(i => Point(i.ToString(), i, 0)).ToArray();
        var directedCosts = new Dictionary<(Vector3, Vector3), double>();
        Task<IReadOnlyList<Vector3>> Directed(Vector3 from, Vector3 to, CancellationToken token)
        {
            var distance = Vector3.Distance(from, to);
            var detour = from.X > to.X ? 60 : (int)(from.X + 2 * to.X) % 7 * 3;
            var path = new[] { from, from + Vector3.UnitZ * detour, to + Vector3.UnitZ * detour, to };
            directedCosts[(from, to)] = distance + 2 * detour;
            return Task.FromResult<IReadOnlyList<Vector3>>(path);
        }
        var directed = await WalkingRoutePlanner.PlanAsync(start, nodes, new HashSet<string> { "17", "15", "13" }, Directed);
        check(directed.Route.Stops.Count == 18 && directed.Route.Stops.Select(s => s.Id).Distinct().Count() == 18,
            "Directed improvement preserves every destination once");
        check(directed.Route.Stops.Take(3).Select(s => s.Id).ToHashSet().SetEquals(["17", "15", "13"]), "Local relocation cannot cross priority groups");
        var previous = start; var total = 0d;
        foreach (var leg in directed.Legs)
        {
            var destination = nodes.Single(s => s.Id == leg.DestinationId);
            check(leg.From == previous && leg.Path[^1] == destination.Position, "Directed path legs join in displayed order");
            total += directedCosts[(previous, destination.Position)]; previous = destination.Position;
        }
        check(Math.Abs(total - directed.Route.Length) < 0.001, "Directed length is recomputed with forward edges, never reversed cached costs");
        var baselineAt = start; var baseline = 0d;
        foreach (var priorityGroup in new[] { true, false })
        {
            var pendingStops = nodes.Where(s => new[] { "17", "15", "13" }.Contains(s.Id) == priorityGroup).ToList();
            while (pendingStops.Count > 0)
            {
                double Cost(Spot to) => Vector3.Distance(baselineAt, to.Position) +
                    2 * (baselineAt.X > to.Position.X ? 60 : (int)(baselineAt.X + 2 * to.Position.X) % 7 * 3);
                var next = pendingStops.OrderBy(Cost).ThenBy(s => s.Id, StringComparer.Ordinal).First();
                baseline += Cost(next); baselineAt = next.Position; pendingStops.Remove(next);
            }
        }
        check(directed.Route.Length <= baseline + 0.001, "Directed relocate and segment-reversal optimization never lengthens the greedy ground route");

        var straightQueries = 0;
        var large = await WalkingRoutePlanner.PlanAsync(start, Enumerable.Range(1, 120).Select(i => Point($"line-{i}", i * 5, 0)), new HashSet<string>(),
            (from, to, _) => { straightQueries++; return Task.FromResult<IReadOnlyList<Vector3>>([from, to]); });
        check(large.Route.Stops.Count == 120 && large.Route.Length == 600 && straightQueries == 120,
            "Lower-bound pruning avoids an all-pairs query storm on a 120-stop road");
        var empty = await WalkingRoutePlanner.PlanAsync(start, [], new HashSet<string>(), Missing);
        check(empty.Route.Length == 0 && empty.Queries == 0, "Empty terrain route needs no navigation queries");

        var session = new SurveySession(); session.Reset(1252, [acrossWall]);
        var automation = new RouteAutomation();
        var remaining = new List<Spot> { acrossWall };
        foreach (var tick in new long[] { 1000, 2000, 3000, 4000, 5000 })
            automation.Update(session, remaining, [], start, tick, PointDisplayMode.Candidates, true, absenceBlockReason: "仍需繞路");
        check(remaining.Count == 1 && session.Get(acrossWall.Id)!.Status != SpotStatus.Skipped && automation.Detail == "仍需繞路",
            "Geometrically nearby chest across a wall does not accumulate an empty-point timer");
        foreach (var tick in new long[] { 6000, 7000, 8000, 9000 })
            automation.Update(session, remaining, [], start, tick, PointDisplayMode.Candidates, true);
        check(remaining.Count == 0 && session.Get(acrossWall.Id)!.Status == SpotStatus.Skipped,
            "Fresh reachable ground distance allows the existing sustained-absence skip");
        session.RestartSurvey(); session.Visit(acrossWall.Id); remaining.Add(acrossWall);
        var opened = automation.Update(session, remaining, [], start, 10000, PointDisplayMode.Candidates, true, absenceBlockReason: "導航準備中");
        check(opened.Reason == RouteAdvanceReason.Opened && remaining.Count == 0, "Confirmed open chest still advances while navigation is unavailable");

        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = WalkingRoutePlanner.PlanAsync(start, [sameSide], new HashSet<string>(), async (_, _, token) =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return []; }, cancellation: cancel.Token);
        await entered.Task; cancel.Cancel();
        try { await pending; check(false, "Cancelled query must not publish a route"); }
        catch (OperationCanceledException) { check(true, "Cancellation interrupts an in-flight query without publishing a partial route"); }
        try
        {
            await WalkingRoutePlanner.PlanAsync(start, [sameSide], new HashSet<string>(), (_, _, _) => throw new TimeoutException());
            check(false, "Provider failure must propagate");
        }
        catch (TimeoutException) { check(true, "A timed-out provider is not misreported as unreachable treasure"); }
    }
}
