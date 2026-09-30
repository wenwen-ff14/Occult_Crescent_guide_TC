using System.Numerics;

namespace CrescentCompass.Core;

public sealed record RouteLeg(string DestinationId, Vector3 From, IReadOnlyList<Vector3> Path, double Length);
public sealed record WalkingRoute(PlannedRoute Route, IReadOnlyList<RouteLeg> Legs, IReadOnlyList<Spot> Unreachable, int Queries);
public sealed record WalkingProgress(string Phase, int Stops, int Total, int Queries);

/// <summary>Orders stops using directed ground paths. Never substitutes a straight line for a missing path.</summary>
public static class WalkingRoutePlanner
{
    public delegate Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to, CancellationToken cancellation);

    // Missing segments remain in the numbered itinerary: they are neither reordered nor silently skipped.
    public static async Task<WalkingRoute> PlanOrderedAsync(Vector3 start, IEnumerable<Spot> ordered,
        FindPath findPath, Action<WalkingProgress>? progress = null, CancellationToken cancellation = default)
    {
        var points = ordered.ToArray();
        if (!Coordinates.IsFinite(start) || points.Any(s => !Coordinates.IsFinite(s.Position)) ||
            points.Length > 256 || points.Select(s => s.Id).Distinct().Count() != points.Length ||
            points.Select(s => s.Territory).Distinct().Count() > 1)
            throw new ArgumentException("Invalid ordered route.");
        var legs = new List<RouteLeg>();
        var missing = new List<Spot>();
        var from = start;
        for (var i = 0; i < points.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = points[i];
            var path = await findPath(from, point.Position, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var leg = ValidatePath(point.Id, from, point.Position, path);
            if (leg is not null) legs.Add(leg); else missing.Add(point);
            from = point.Position;
            progress?.Invoke(new("依圖表順序計算路段", i + 1, points.Length, i + 1));
        }
        return new(new(points, legs.Sum(l => l.Length), false), legs, missing, points.Length);
    }

    public static RouteLeg? ValidatePath(string id, Vector3 from, Vector3 to, IReadOnlyList<Vector3> path)
    {
        // Mesh projection may differ slightly from object height, but a truncated path or a different floor is not arrival.
        if (path.Count == 0 || path.Any(p => !Coordinates.IsFinite(p)) ||
            Vector3.Distance(path[0], from) > 3 || Vector3.Distance(path[^1], to) > 3) return null;
        var normalized = path.Prepend(from).Append(to).ToArray();
        var length = 0d;
        for (var i = 1; i < normalized.Length; i++) length += Vector3.Distance(normalized[i - 1], normalized[i]);
        return new(id, from, normalized, length);
    }

    public static async Task<WalkingRoute> PlanAsync(Vector3 start, IEnumerable<Spot> candidates,
        IReadOnlySet<string> priorityIds, FindPath findPath, Action<WalkingProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var points = candidates.DistinctBy(s => s.Id).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (!Coordinates.IsFinite(start) || points.Any(s => !Coordinates.IsFinite(s.Position)) ||
            points.Length > 256 || points.Select(s => s.Territory).Distinct().Count() > 1)
            throw new ArgumentException("Routes need finite coordinates, one territory and at most 256 stops.");
        var positions = points.Select(s => s.Position).Prepend(start).ToArray();
        var cache = new Dictionary<(int From, int To), RouteLeg?>();
        var order = new List<int>();
        var unreachable = new List<Spot>();
        var queries = 0;
        var phase = "選擇可達站點";
        async Task<RouteLeg?> Path(int from, int to)
        {
            cancellation.ThrowIfCancellationRequested();
            if (cache.TryGetValue((from, to), out var found)) return found;
            queries++;
            progress?.Invoke(new(phase, order.Count, points.Length, queries));
            var path = await findPath(positions[from], positions[to], cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var leg = ValidatePath(points[to - 1].Id, positions[from], positions[to], path);
            cache[(from, to)] = leg;
            return leg;
        }
        double LowerBound(int from, int to) => Vector3.Distance(positions[from], positions[to]);
        bool Priority(int index) => priorityIds.Contains(points[index - 1].Id);
        if (points.Length <= DirectedRouteSolver.ExactLimit)
        {
            phase = "比較全部站間步行路程";
            var costs = new double[points.Length + 1, points.Length + 1];
            for (var from = 0; from <= points.Length; from++)
            for (var to = 1; to <= points.Length; to++)
                costs[from, to] = from == to ? 0 : (await Path(from, to).ConfigureAwait(false))?.Length ?? double.PositiveInfinity;
            order = DirectedRouteSolver.SolveExact(costs, Enumerable.Range(1, points.Length).Where(Priority).ToHashSet(), cancellation).ToList();
            var exactLegs = Edges(order).Select(e => cache[e]!).ToArray();
            progress?.Invoke(new("完成", order.Count, points.Length, queries));
            return new(new(order.Select(i => points[i - 1]).ToArray(), exactLegs.Sum(l => l.Length), order.Count == points.Length), exactLegs,
                Enumerable.Range(1, points.Length).Except(order).Select(i => points[i - 1]).ToArray(), queries);
        }
        var groups = new[] { true, false };
        foreach (var priority in groups)
        {
            var pending = Enumerable.Range(1, points.Length).Where(i => Priority(i) == priority).ToHashSet();
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var from = order.LastOrDefault();
                var best = 0;
                var cost = double.PositiveInfinity;
                // Euclidean distance is only a lower bound for pruning; every selected edge has a queried ground path.
                foreach (var next in pending.OrderBy(i => LowerBound(from, i)).ThenBy(i => i))
                {
                    if (LowerBound(from, next) > cost + 0.001) break;
                    if (await Path(from, next).ConfigureAwait(false) is not { } leg || leg.Length >= cost) continue;
                    best = next; cost = leg.Length;
                }
                if (best == 0) { unreachable.AddRange(pending.Order().Select(i => points[i - 1])); break; }
                order.Add(best); pending.Remove(best);
            }
        }

        // Bounded directed relocation avoids the invalid symmetric 2-opt assumption at one-way drops.
        // Queries are cached within this request only; mesh/instance changes cannot reuse stale paths.
        phase = "改善繞路順序";
        var queryBudget = queries + Math.Max(128, points.Length * 16);
        for (var pass = 0; pass < 4 && queries < queryBudget; pass++)
        {
            var changed = false;
            for (var index = 0; index < order.Count && queries < queryBudget; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var moving = order[index];
                var shorter = order.Where((_, i) => i != index).ToList();
                var slots = Enumerable.Range(0, shorter.Count + 1)
                    .Where(slot => slot != index &&
                        (slot == 0 || !Priority(moving) || Priority(shorter[slot - 1])) &&
                        (slot == shorter.Count || Priority(moving) || !Priority(shorter[slot])))
                    .OrderBy(slot => LowerBound(slot == 0 ? 0 : shorter[slot - 1], moving)).Take(20);
                foreach (var slot in slots)
                {
                    if (queries >= queryBudget) break;
                    var proposal = shorter.ToList(); proposal.Insert(slot, moving);
                    var oldEdges = Edges(order).ToHashSet();
                    var newEdges = Edges(proposal).ToHashSet();
                    var removed = oldEdges.Except(newEdges).ToArray();
                    var added = newEdges.Except(oldEdges).ToArray();
                    var before = removed.Sum(e => cache[e]!.Length);
                    if (added.Sum(e => LowerBound(e.From, e.To)) >= before - 0.001) continue;
                    var after = 0d;
                    foreach (var edge in added)
                    {
                        after += (await Path(edge.From, edge.To).ConfigureAwait(false))?.Length ?? double.PositiveInfinity;
                        if (after >= before - 0.001) break;
                    }
                    if (after >= before - 0.001) continue;
                    order = proposal; changed = true; break;
                }
            }
            // Directed segment reversal removes crossings that a single-stop move cannot fix.
            // Every reversed internal edge is queried and counted; no symmetric-distance shortcut is valid here.
            for (var i = 0; i < order.Count - 1 && queries < queryBudget; i++)
            for (var j = i + 1; j < Math.Min(order.Count, i + 20) && queries < queryBudget; j++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Priority(order[i]) != Priority(order[j])) continue;
                var proposal = order.ToList(); proposal.Reverse(i, j - i + 1);
                var oldEdges = Edges(order).ToHashSet(); var newEdges = Edges(proposal).ToHashSet();
                var removed = oldEdges.Except(newEdges).ToArray(); var added = newEdges.Except(oldEdges).ToArray();
                var before = removed.Sum(e => cache[e]!.Length);
                if (added.Sum(e => LowerBound(e.From, e.To)) >= before - 0.001) continue;
                var after = 0d;
                foreach (var edge in added)
                {
                    if (queries >= queryBudget && !cache.ContainsKey(edge)) { after = double.PositiveInfinity; break; }
                    after += (await Path(edge.From, edge.To).ConfigureAwait(false))?.Length ?? double.PositiveInfinity;
                    if (after >= before - 0.001) break;
                }
                if (after >= before - 0.001) continue;
                order = proposal; changed = true;
            }
            if (!changed) break;
        }
        cancellation.ThrowIfCancellationRequested();
        var legs = Edges(order).Select(e => cache[e]!).ToArray();
        progress?.Invoke(new("完成", order.Count, points.Length, queries));
        return new(new(order.Select(i => points[i - 1]).ToArray(), legs.Sum(l => l.Length), false), legs, unreachable, queries);
    }

    private static IEnumerable<(int From, int To)> Edges(IReadOnlyList<int> order)
    {
        for (var i = 0; i < order.Count; i++) yield return (i == 0 ? 0 : order[i - 1], order[i]);
    }
}
