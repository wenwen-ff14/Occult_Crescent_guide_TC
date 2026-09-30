using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Open route, fixed origin at matrix index 0; supports directed/unreachable edges and priority groups.</summary>
public static class DirectedRouteSolver
{
    public const int ExactLimit = 12;

    public static IReadOnlyList<int> SolveExact(double[,] costs, IReadOnlySet<int> priority, CancellationToken cancellation = default)
    {
        var count = costs.GetLength(0) - 1;
        if (count < 0 || count > ExactLimit || costs.GetLength(1) != count + 1)
            throw new ArgumentException("Exact ground routes support at most twelve stops.");
        if (count == 0) return [];
        if (priority.Any(p => p < 1 || p > count)) throw new ArgumentException("Invalid priority index.");
        foreach (var cost in costs)
            if (double.IsNaN(cost) || cost < 0) throw new ArgumentException("Costs must be non-negative or positive infinity.");
        var states = 1 << count;
        var dp = new double[states, count];
        var parent = new int[states, count];
        for (var mask = 0; mask < states; mask++)
        for (var last = 0; last < count; last++) { dp[mask, last] = double.PositiveInfinity; parent[mask, last] = -1; }
        for (var i = 0; i < count; i++) dp[1 << i, i] = costs[0, i + 1];
        for (var mask = 1; mask < states; mask++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var last = 0; last < count; last++)
            {
                if (!double.IsFinite(dp[mask, last])) continue;
                for (var next = 0; next < count; next++)
                {
                    if ((mask & (1 << next)) != 0 || !priority.Contains(last + 1) && priority.Contains(next + 1)) continue;
                    var nextMask = mask | (1 << next);
                    var length = dp[mask, last] + costs[last + 1, next + 1];
                    if (length >= dp[nextMask, next]) continue;
                    dp[nextMask, next] = length; parent[nextMask, next] = last;
                }
            }
        }
        // If a full tour is impossible, maximize visited priority stops, then total stops, then minimize distance.
        // This also avoids greedily entering a one-way dead end before other reachable stops.
        var priorityMask = priority.Aggregate(0, (mask, index) => mask | 1 << (index - 1));
        var bestMask = 0; var bestEnd = -1; var bestPriority = -1; var bestCount = -1; var bestCost = double.PositiveInfinity;
        for (var mask = 1; mask < states; mask++)
        for (var last = 0; last < count; last++)
        {
            if (!double.IsFinite(dp[mask, last])) continue;
            var priorityCount = BitOperations.PopCount((uint)(mask & priorityMask));
            var visitedCount = BitOperations.PopCount((uint)mask);
            if (priorityCount < bestPriority || priorityCount == bestPriority && visitedCount < bestCount ||
                priorityCount == bestPriority && visitedCount == bestCount && dp[mask, last] >= bestCost) continue;
            bestMask = mask; bestEnd = last; bestPriority = priorityCount; bestCount = visitedCount; bestCost = dp[mask, last];
        }
        var result = new List<int>();
        while (bestEnd >= 0)
        {
            result.Add(bestEnd + 1);
            var previous = parent[bestMask, bestEnd]; bestMask ^= 1 << bestEnd; bestEnd = previous;
        }
        result.Reverse();
        return result;
    }
}
