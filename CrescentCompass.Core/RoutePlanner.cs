using System.Numerics;

namespace CrescentCompass.Core;

public sealed record PlannedRoute(IReadOnlyList<Spot> Stops, double Length, bool Exact);

public static class RoutePlanner
{
    public static PlannedRoute PlanPrioritized(Vector3 start, IEnumerable<Spot> candidates, IReadOnlySet<string> priorityIds)
    {
        var points = candidates.DistinctBy(s => s.Id).ToArray();
        if (points.Length > 256 || points.Select(s => s.Territory).Distinct().Count() > 1)
            throw new ArgumentException("A patrol must stay in one territory and contain at most 256 stops.", nameof(candidates));
        var first = Plan(start, points.Where(s => priorityIds.Contains(s.Id)));
        var rest = Plan(first.Stops.LastOrDefault()?.Position ?? start, points.Where(s => !priorityIds.Contains(s.Id)));
        // Optimize each group independently. Their boundary is not a globally optimal unconstrained tour.
        return new([.. first.Stops, .. rest.Stops], first.Length + rest.Length,
            first.Stops.Count == 0 ? rest.Exact : rest.Stops.Count == 0 && first.Exact);
    }
    public static PlannedRoute Plan(Vector3 start, IEnumerable<Spot> candidates)
    {
        if (!Coordinates.IsFinite(start)) throw new ArgumentException("Invalid start position.", nameof(start));
        var points = candidates.DistinctBy(s => s.Id).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (points.Any(s => !Coordinates.IsFinite(s.Position))) throw new ArgumentException("Invalid stop position.", nameof(candidates));
        if (points.Select(s => s.Territory).Distinct().Count() > 1) throw new ArgumentException("Route cannot cross territories.", nameof(candidates));
        if (points.Length > 256) throw new ArgumentException("At most 256 stops are supported.", nameof(candidates));
        if (points.Length == 0) return new PlannedRoute([], 0, true);
        var costs = new double[points.Length + 1, points.Length + 1];
        for (var i = 0; i <= points.Length; i++)
        for (var j = 0; j <= points.Length; j++)
            costs[i, j] = Vector3.Distance(i == 0 ? start : points[i - 1].Position, j == 0 ? start : points[j - 1].Position);
        var exact = points.Length <= 12;
        var order = exact ? SolveExact(costs, points.Length) : SolveHeuristic(costs, points.Length);
        var stops = order.Select(i => points[i - 1]).ToArray();
        return new PlannedRoute(stops, Length(order, costs), exact);
    }

    private static List<int> SolveExact(double[,] costs, int count)
    {
        var states = 1 << count;
        var dp = new double[states, count];
        var previous = new int[states, count];
        for (var mask = 0; mask < states; mask++)
        for (var last = 0; last < count; last++) { dp[mask, last] = double.PositiveInfinity; previous[mask, last] = -1; }
        for (var i = 0; i < count; i++) dp[1 << i, i] = costs[0, i + 1];
        for (var mask = 1; mask < states; mask++)
        for (var last = 0; last < count; last++)
        {
            if ((mask & (1 << last)) == 0) continue;
            for (var next = 0; next < count; next++)
            {
                if ((mask & (1 << next)) != 0) continue;
                var nextMask = mask | (1 << next);
                var distance = dp[mask, last] + costs[last + 1, next + 1];
                if (distance >= dp[nextMask, next]) continue;
                dp[nextMask, next] = distance;
                previous[nextMask, next] = last;
            }
        }
        var finalMask = states - 1;
        var end = Enumerable.Range(0, count).MinBy(i => dp[finalMask, i]);
        var result = new List<int>();
        while (end >= 0)
        {
            result.Add(end + 1);
            var parent = previous[finalMask, end];
            finalMask ^= 1 << end;
            end = parent;
        }
        result.Reverse();
        return result;
    }

    private static List<int> SolveHeuristic(double[,] costs, int count)
    {
        List<int>? best = null;
        var bestLength = double.PositiveInfinity;
        // Multiple starting choices reduce the nearest-neighbour heuristic's sensitivity to its first stop.
        foreach (var first in Enumerable.Range(1, count).OrderBy(i => costs[0, i]).Take(8))
        {
            var remaining = Enumerable.Range(1, count).Where(i => i != first).ToHashSet();
            var order = new List<int> { first };
            while (remaining.Count > 0)
            {
                var next = remaining.OrderBy(i => costs[order[^1], i]).ThenBy(i => i).First();
                order.Add(next);
                remaining.Remove(next);
            }
            for (var pass = 0; pass < 40; pass++)
            {
                var improved = false;
                for (var i = 0; i < count - 1; i++)
                for (var j = i + 1; j < count; j++)
                {
                    var before = i == 0 ? 0 : order[i - 1];
                    var oldCost = costs[before, order[i]] + (j + 1 < count ? costs[order[j], order[j + 1]] : 0);
                    var newCost = costs[before, order[j]] + (j + 1 < count ? costs[order[i], order[j + 1]] : 0);
                    if (newCost >= oldCost - 0.0001) continue;
                    order.Reverse(i, j - i + 1);
                    improved = true;
                }
                if (!improved) break;
            }
            var length = Length(order, costs);
            if (length < bestLength) { bestLength = length; best = order; }
        }
        return best!;
    }

    private static double Length(IReadOnlyList<int> order, double[,] costs)
    {
        var result = 0d;
        var previous = 0;
        foreach (var next in order) { result += costs[previous, next]; previous = next; }
        return result;
    }
}
