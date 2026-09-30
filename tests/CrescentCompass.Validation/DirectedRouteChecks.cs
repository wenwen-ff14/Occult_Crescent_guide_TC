using System.Numerics;
using CrescentCompass.Core;

internal static class DirectedRouteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static double Length(IReadOnlyList<int> order, double[,] costs) => order.Select((p, i) => costs[i == 0 ? 0 : order[i - 1], p]).Sum();
        static double Brute(int at, int[] remaining, double[,] costs, HashSet<int> priority)
        {
            if (remaining.Length == 0) return 0;
            var eligible = remaining.Any(priority.Contains) ? remaining.Where(priority.Contains) : remaining;
            return eligible.Min(next => costs[at, next] + Brute(next, remaining.Where(p => p != next).ToArray(), costs, priority));
        }
        var random = new Random(6293);
        for (var trial = 0; trial < 18; trial++)
        {
            var costs = new double[8, 8];
            for (var i = 0; i < 8; i++)
            for (var j = 0; j < 8; j++) costs[i, j] = i == j ? 0 : random.Next(1, 150);
            var priority = trial % 2 == 0 ? new HashSet<int> { 2, 5 } : [];
            var route = DirectedRouteSolver.SolveExact(costs, priority);
            check(route.Count == 7 && Math.Abs(Length(route, costs) - Brute(0, Enumerable.Range(1, 7).ToArray(), costs, priority)) < 0.0001,
                $"Directed exact route including group boundary equals exhaustive permutations {trial}");
        }
        var trap = new double[,] { { 0, 1, 5 }, { 0, 0, double.PositiveInfinity }, { 0, 3, 0 } };
        check(DirectedRouteSolver.SolveExact(trap, new HashSet<int>()).SequenceEqual([2, 1]), "One-way dead end is visited last so no reachable stop is lost");
        var isolated = new double[,] { { 0, double.PositiveInfinity, double.PositiveInfinity }, { 0, 0, 2 }, { 0, 2, 0 } };
        check(DirectedRouteSolver.SolveExact(isolated, new HashSet<int>()).Count == 0, "An entirely unreachable tour returns empty, never a fake one-stop route");

        // Exercise the full async planner and stored polylines, not just the matrix solver.
        var spots = Enumerable.Range(1, 6).Select(i => new Spot(i.ToString(), 1252, SpotKind.Silver, (uint)i, new(i * 2, 0, 0))).ToArray();
        var roadCosts = new double[7, 7];
        var queries = new HashSet<(int, int)>();
        Task<IReadOnlyList<Vector3>> Roads(Vector3 from, Vector3 to, CancellationToken token)
        {
            var a = (int)from.X / 2; var b = (int)to.X / 2;
            if (!queries.Add((a, b))) throw new InvalidOperationException("Duplicate directed path query");
            var detour = (a * 7 + b * 13) % 11;
            roadCosts[a, b] = Vector3.Distance(from, to) + detour * 2;
            return Task.FromResult<IReadOnlyList<Vector3>>([from, from + Vector3.UnitZ * detour, to + Vector3.UnitZ * detour, to]);
        }
        var planned = await WalkingRoutePlanner.PlanAsync(Vector3.Zero, spots, new HashSet<string> { "3", "5" }, Roads);
        check(planned.Route.Exact && planned.Queries == 36 && planned.Unreachable.Count == 0, "Six-stop planning queries each directed ground pair once");
        check(Math.Abs(planned.Route.Length - Brute(0, Enumerable.Range(1, 6).ToArray(), roadCosts, [3, 5])) < 0.001,
            "Async route minimizes total actual polyline length under priority constraints");
        var first = Vector3.Zero;
        foreach (var leg in planned.Legs)
        {
            check(leg.From == first && leg.Path[0] == first && leg.Path[^1] == spots.Single(p => p.Id == leg.DestinationId).Position,
                "Shortest tour polylines connect consecutively in display order");
            first = leg.Path[^1];
        }
        var afterTeleport = await WalkingRoutePlanner.PlanAsync(new(500, 0, 0), spots, new HashSet<string>(),
            (from, to, _) => Task.FromResult<IReadOnlyList<Vector3>>([from, to]));
        check(afterTeleport.Route.Stops.First().Id == "6" && afterTeleport.Legs.First().From == new Vector3(500, 0, 0),
            "Landing on the opposite side changes the optimal first stop and path origin");
        foreach (var count in new[] { 12, 13 })
        {
            var boundary = await WalkingRoutePlanner.PlanAsync(Vector3.Zero,
                Enumerable.Range(1, count).Select(i => new Spot($"boundary-{i}", 1252, SpotKind.Exploration, 0, new(i * 3, 0, 0))), new HashSet<string>(),
                (from, to, _) => Task.FromResult<IReadOnlyList<Vector3>>([from, to]));
            check(boundary.Route.Stops.Count == count && boundary.Route.Length == count * 3 && boundary.Route.Exact == (count == 12) &&
                boundary.Queries == (count == 12 ? 144 : 13), $"Exact/heuristic boundary at {count} stops preserves result and query limits");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { DirectedRouteSolver.SolveExact(roadCosts, new HashSet<int>(), canceled.Token); check(false, "Canceled exact solver must abort"); }
        catch (OperationCanceledException) { check(true, "Exact optimization honors cancellation"); }
    }
}
