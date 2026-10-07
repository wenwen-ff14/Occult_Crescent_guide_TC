using System.Numerics;
using CrescentCompass.Core;

internal static class PatrolRecoveryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        Vector3 from = new(0, 0, 0), to = new(100, 0, 0);
        Vector3[] failed = [from, to];
        var calls = 0;
        Task<IReadOnlyList<Vector3>> Straight(Vector3 a, Vector3 b, CancellationToken _)
        { calls++; return Task.FromResult<IReadOnlyList<Vector3>>([a, b]); }
        var path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, Straight, default);
        check(calls == 3 && path.Count > 2 && path[0] == from && path[^1] == to && path.Any(p => Math.Abs(p.Z) >= 8),
            "Identical fresh mesh route is rejected and a queried side approach is selected");
        check(!path.SequenceEqual(failed), "Recovery never replays the failed straight approach");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            calls = 0;
            path = await PatrolRecoveryPlanner.FindPath(from, to, failed, attempt, Straight, default);
            check(calls <= PatrolRecoveryPlanner.MaximumQueries && path.Any(p => Math.Abs(p.Z) >= 8 + (attempt - 1) * 2),
                "Later bounded recovery widens the side step");
            check(path.First(p => p.Z != 0).Z * (attempt % 2 == 0 ? -1 : 1) > 0,
                "Repeated recovery alternates the first tested side");
        }
        calls = 0;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (a, b, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Vector3>>([a, new(0, 0, 5), new(100, 0, 5), b]);
        }, default);
        check(calls == 1 && path.Count == 4, "A genuinely different fresh path needs no anchor queries and keeps mesh bends");
        calls = 0;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (a, b, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Vector3>>(b.Z > 0 ? [] : [a, b]);
        }, default);
        check(path.Any(p => p.Z < 0) && calls == 4, "An unreachable first side falls back to the opposite side");
        calls = 0;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (a, b, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Vector3>>(b == to ? [a, from, to] : [a, b]);
        }, default);
        check(path.Count == 0 && calls == PatrolRecoveryPlanner.MaximumQueries,
            "All connectors that return through the same blocked approach are rejected within a fixed query budget");
        foreach (var invalid in new Vector3[][] { [], [new(float.NaN, 0, 0)], [new(0, 2, 0), to], [from, to + Vector3.UnitY * 5] })
        {
            calls = 0;
            path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (_, _, _) =>
            { calls++; return Task.FromResult<IReadOnlyList<Vector3>>(invalid); }, default);
            check(path.Count == 0 && calls <= PatrolRecoveryPlanner.MaximumQueries, "Invalid, wrong-floor and incomplete recovery legs never become movement");
        }
        foreach (var attempt in new[] { 0, 4 })
        {
            calls = 0;
            path = await PatrolRecoveryPlanner.FindPath(from, to, failed, attempt, Straight, default);
            check(path.Count == 0 && calls == 0, "Recovery attempt guard rejects invalid budgets before querying");
        }
        calls = 0;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (_, _, _) =>
        { calls++; throw new InvalidOperationException("No mesh at anchor"); }, default);
        check(path.Count == 0 && calls <= PatrolRecoveryPlanner.MaximumQueries, "Unreachable anchors cannot create an unbounded retry");
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        var recovery = PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (_, _, _) => pending.Task, cancellation.Token);
        cancellation.Cancel();
        var cancelled = false;
        try { await recovery; } catch (OperationCanceledException) { cancelled = true; }
        pending.SetException(new InvalidOperationException("Late provider failure"));
        check(cancelled, "Cancellation also bounds a mesh provider that ignores cancellation");
        calls = 0;
        try { await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, Straight, cancellation.Token); }
        catch (OperationCanceledException) { }
        check(calls == 0, "An already cancelled recovery sends no query");

        var snappedAnchor = new Vector3(-2, 0, 8);
        var anchors = new List<Vector3>();
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (a, b, _) =>
        {
            anchors.Add(b);
            return Task.FromResult<IReadOnlyList<Vector3>>([a, b]);
        }, default, (_, _) => Task.FromResult<Vector3?>(snappedAnchor));
        check(anchors.Count == 3 && anchors[1] == snappedAnchor && path.Contains(snappedAnchor),
            "Recovery uses the snapped mesh anchor for both path queries, never the raw candidate");
        foreach (var invalid in new Vector3?[] { null, new(float.NaN, 0, 0), new(-1, 4, 8), new(-1, 0, 50) })
        {
            calls = 0;
            path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, Straight, default,
                (_, _) => Task.FromResult(invalid));
            check(path.Count == 0 && calls == 1, "Missing, nonfinite, wrong-floor or distant mesh snaps cannot create recovery paths");
        }
        calls = 0;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, Straight, default,
            (anchor, _) => Task.FromResult<Vector3?>(anchor));
        check(path.Count > 0 && calls == 3, "Legacy providers still require complete queried paths for raw anchors");
        var actualEnd = Vector3.Zero;
        var matchedJoin = false;
        path = await PatrolRecoveryPlanner.FindPath(from, to, failed, 1, (a, b, _) =>
        {
            if (b == to) { matchedJoin |= a == actualEnd && a != from; return Task.FromResult<IReadOnlyList<Vector3>>([a, b]); }
            actualEnd = b + Vector3.UnitZ * 0.5f;
            return Task.FromResult<IReadOnlyList<Vector3>>([a, actualEnd]);
        }, default);
        check(matchedJoin && path.Contains(actualEnd), "The second recovery leg starts at the first leg's actual mesh endpoint");
    }
}
