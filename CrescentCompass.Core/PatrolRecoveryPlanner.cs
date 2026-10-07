using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Queries alternative mesh paths around a failed approach; never moves along candidate anchors directly.</summary>
public static class PatrolRecoveryPlanner
{
    public const int MaximumQueries = 9;
    public static async Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to,
        IReadOnlyList<Vector3> failedPath, int attempt,
        Func<Vector3, Vector3, CancellationToken, Task<IReadOnlyList<Vector3>>> find,
        CancellationToken cancellation,
        Func<Vector3, CancellationToken, Task<Vector3?>>? snap = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Coordinates.IsFinite(from) || !Coordinates.IsFinite(to) || failedPath.Count == 0 ||
            failedPath.Any(p => !Coordinates.IsFinite(p)) || attempt is < 1 or > AutoChestPatrol.MaxRecoveryAttempts) return [];
        var forward = failedPath.Select(p => new Vector3(p.X - from.X, 0, p.Z - from.Z))
            .FirstOrDefault(v => v.LengthSquared() >= 0.25f);
        if (forward.LengthSquared() < 0.25f) return [];
        forward = Vector3.Normalize(forward);
        var blocked = from + forward * 1.5f;
        var side = new Vector3(-forward.Z, 0, forward.X);
        var radius = 8f + (attempt - 1) * 2;
        if (attempt % 2 == 0) side = -side;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(14));
        var token = timeout.Token;
        var queries = 0;
        var maxLength = AutoChestPatrol.RemainingDistance(from, failedPath) * 1.75f + 20;

        async Task<IReadOnlyList<Vector3>> Query(Vector3 start, Vector3 end)
        {
            token.ThrowIfCancellationRequested();
            if (++queries > MaximumQueries) return [];
            try
            {
                var pending = find(start, end, token);
                _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var path = await pending.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return Valid(path, start, end) ? path : [];
            }
            catch (InvalidOperationException) { return []; }
        }
        bool Accept(IReadOnlyList<Vector3> path) => path.Count > 0 &&
            AutoChestPatrol.RemainingDistance(from, path) <= maxLength && !CrossesBlockedApproach(from, path, blocked);

        var fresh = await Query(from, to).ConfigureAwait(false);
        if (Accept(fresh)) return fresh;
        Vector3[] anchors = [from + side * radius - forward, from - side * radius - forward,
            from - forward * radius + side * radius, from - forward * radius - side * radius];
        foreach (var candidate in anchors)
        {
            var anchor = candidate;
            if (snap is not null)
            {
                token.ThrowIfCancellationRequested();
                var snapped = await snap(candidate, token).WaitAsync(token).ConfigureAwait(false);
                if (snapped is not { } point || !ValidAnchor(candidate, point)) continue;
                anchor = point;
            }
            var first = await Query(from, anchor).ConfigureAwait(false);
            if (first.Count == 0 || CrossesBlockedApproach(from, first, blocked)) continue;
            // Query from the actual mesh endpoint, not the requested anchor, so joined legs have no invented gap.
            var second = await Query(first[^1], to).ConfigureAwait(false);
            if (second.Count == 0) continue;
            var path = first.Concat(second).ToArray();
            if (Accept(path)) return path;
        }
        return [];
    }

    public static bool ValidAnchor(Vector3 requested, Vector3 point) => Coordinates.IsFinite(point) &&
        Coordinates.IsFinite(requested) && Math.Abs(point.Y - requested.Y) <= 3 &&
        Vector2.DistanceSquared(new(requested.X, requested.Z), new(point.X, point.Z)) <= 9;

    private static bool Valid(IReadOnlyList<Vector3> path, Vector3 from, Vector3 to) => path.Count > 0 &&
        path.All(Coordinates.IsFinite) && Vector3.DistanceSquared(path[0], from) <= 0.25f &&
        Vector3.DistanceSquared(path[^1], to) <= AutoChestPatrol.ArrivalRadius * AutoChestPatrol.ArrivalRadius;

    private static bool CrossesBlockedApproach(Vector3 from, IReadOnlyList<Vector3> path, Vector3 blocked)
    {
        foreach (var to in path)
        {
            var delta = new Vector2(to.X - from.X, to.Z - from.Z);
            var offset = new Vector2(blocked.X - from.X, blocked.Z - from.Z);
            var t = delta.LengthSquared() < 0.0001f ? 0 : Math.Clamp(Vector2.Dot(offset, delta) / delta.LengthSquared(), 0, 1);
            var closest = Vector3.Lerp(from, to, t);
            if (Math.Abs(closest.Y - blocked.Y) <= 2 &&
                Vector2.DistanceSquared(new(closest.X, closest.Z), new(blocked.X, blocked.Z)) < 1.21f) return true;
            from = to;
        }
        return false;
    }
}
