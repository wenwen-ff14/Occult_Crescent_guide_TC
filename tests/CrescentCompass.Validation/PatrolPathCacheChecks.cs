using System.Numerics;
using CrescentCompass.Core;

internal static class PatrolPathCacheChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var cache = new PatrolPathCache();
        var queries = 0;
        Task<IReadOnlyList<Vector3>> Find(Vector3 from, Vector3 to, CancellationToken token)
        { queries++; return Task.FromResult<IReadOnlyList<Vector3>>([from, to]); }
        var start = Vector3.Zero; var end = new Vector3(100, 0, 0);
        var original = await cache.FindPath(start, end, Find, default);
        await cache.FindPath(start, end, Find, default);
        check(queries == 1 && cache.Count == 1, "Repeated directed legs reuse one mesh query");
        var suffix = await cache.FindPath(new(50, 0, 0), end, Find, default);
        check(queries == 1 && suffix[0] == new Vector3(50, 0, 0) && suffix[^1] == end, "Resuming midway reuses the remaining segment without returning to its start");
        ((Vector3[])original)[0] = new(999, 0, 0);
        check(cache.TryGet(start, end, out var unmodified) && unmodified[0] == start, "Returned arrays cannot mutate cached geometry");
        await cache.FindPath(end, start, Find, default);
        check(queries == 2, "Reverse directions never infer reachability from the forward path");
        await cache.FindPath(new(50, 5, 0), end, Find, default);
        check(queries == 3, "Different-floor starts require a new mesh query");
        await cache.FindPath(start, end + Vector3.UnitZ, Find, default);
        check(queries == 4, "A changed chest position uses its own destination key");
        cache.Clear(); await cache.FindPath(start, end, Find, default);
        check(queries == 5 && cache.Count == 1, "Invalidation forces a fresh mesh path");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await cache.FindPath(start, end, Find, cancelled.Token); check(false, "Cancelled hit must throw"); }
        catch (OperationCanceledException) { check(queries == 5, "Cancellation also applies to cache hits"); }

        cache.Clear();
        var pending = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        var loading = cache.FindPath(start, end, (_, _, _) => pending.Task, default);
        cache.Clear(); pending.SetResult([start, end]);
        try { await loading; check(false, "Old generation must not return"); }
        catch (OperationCanceledException) { check(cache.Count == 0, "A late result cannot refill an invalidated navigation cache"); }

        pending = new(); var parallelQueries = 0;
        Task<IReadOnlyList<Vector3>> Delayed(Vector3 a, Vector3 b, CancellationToken token) { parallelQueries++; return pending.Task; }
        var first = cache.FindPath(start, end, Delayed, default);
        var second = cache.FindPath(start, end, Delayed, default);
        using var waitingCancellation = new CancellationTokenSource();
        var cancelledWaiter = cache.FindPath(start, end, Delayed, waitingCancellation.Token);
        waitingCancellation.Cancel();
        try { await cancelledWaiter; check(false, "Waiting cancellation must throw"); }
        catch (OperationCanceledException) { check(!first.IsCompleted, "Cancelling one waiter does not cancel another caller's request"); }
        pending.SetResult([start, end]); await first; await second;
        check(parallelQueries == 1, "Concurrent planning and inspection share the same completed leg");

        cache.Clear();
        foreach (var bad in new Vector3[][] { [], [start, new(50, 0, 0)], [new(5, 0, 0), end], [start, new(float.NaN, 0, 0)] })
        {
            await cache.FindPath(start, end, (_, _, _) => Task.FromResult<IReadOnlyList<Vector3>>(bad), default);
            check(cache.Count == 0, "Incomplete or invalid paths are not cached");
        }
        try { await cache.FindPath(start, end, (_, _, _) => throw new InvalidOperationException(), default); }
        catch (InvalidOperationException) { }
        await cache.FindPath(start, end, Find, default);
        check(cache.Count == 1, "Failed queries release the cache lock for retry");
        for (var i = 1; i <= PatrolPathCache.Capacity; i++)
            await cache.FindPath(start, new(i, 0, 100), Find, default);
        check(cache.Count == PatrolPathCache.Capacity && !cache.TryGet(start, end, out _), "The cache is bounded and evicts oldest legs");

        Vector3[] corner = [start, new(0, 0, 20), new(20, 0, 20)];
        check(PatrolPathCache.TryResume(new(0, 0, 10), corner, out var bent) &&
            bent.SequenceEqual(new Vector3[] { new(0, 0, 10), new(0, 0, 20), new(20, 0, 20) }), "Cached suffix preserves corners rather than drawing a shortcut");
        check(!PatrolPathCache.TryResume(new(10, 0, 10), corner, out _) && !PatrolPathCache.TryResume(new(0, 5, 10), corner, out _),
            "Off-route and wrong-floor positions cannot attach to cached geometry");

        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data/SouthHorn/treasure_locations.json"));
        var chart = ChestChart.Order(SpotCatalog.Load(stream), 1).ToArray();
        cache.Clear(); queries = 0;
        Task<IReadOnlyList<Vector3>> Cached(Vector3 a, Vector3 b, CancellationToken token) => cache.FindPath(a, b, Find, token);
        await WalkingRoutePlanner.PlanOrderedAsync(chart[0].Position, chart, Cached);
        var initialQueries = queries;
        await WalkingRoutePlanner.PlanOrderedAsync(chart[0].Position, chart, Cached);
        check(initialQueries == chart.Length && queries == initialQueries, "A complete repeated 68-stop plan makes zero additional mesh queries");
        for (var i = 1; i < chart.Length; i++)
        {
            await Cached(chart[i - 1].Position, chart[i].Position, default);
            for (var fraction = 1; fraction <= 9; fraction++)
                await Cached(Vector3.Lerp(chart[i - 1].Position, chart[i].Position, fraction / 10f), chart[i].Position, default);
        }
        check(queries == initialQueries, "Movement and repeated along-route inspection reuse the preplanned 68-stop geometry");

        cache.Clear();
        await cache.FindPath(start, end, Find, default);
        List<(Vector3 From, Vector3 To)> connectors = [];
        Task<IReadOnlyList<Vector3>> Connect(Vector3 a, Vector3 b, CancellationToken token)
        { connectors.Add((a, b)); return Task.FromResult<IReadOnlyList<Vector3>>([a, b]); }
        var offset = new Vector3(40, 0, 4);
        var joined = await cache.FindPath(offset, end, Connect, default);
        check(connectors.Count == 1 && connectors[0].To == new Vector3(40, 0, 0) && joined[^1] == end,
            "An off-route stop finds only a short mesh connector then keeps the cached suffix");
        check(joined.Contains(new Vector3(40, 0, 0)) && cache.TryGet(offset, end, out _),
            "The combined connector path is reusable without inventing a straight route to the chest");
        cache.Forget(end);
        check(!cache.TryGet(offset, end, out _) && cache.Count == 0, "Stuck recovery invalidates all cached variants for its destination");

        await cache.FindPath(start, end, Find, default);
        connectors.Clear();
        var recovered = await cache.FindPath(offset, end, (a, b, _) =>
        {
            connectors.Add((a, b));
            return Task.FromResult<IReadOnlyList<Vector3>>(b == end ? [a, b] : []);
        }, default);
        check(connectors.Count == 2 && connectors[0].To != end && connectors[1].To == end && recovered[^1] == end,
            "An unwalkable short join falls back to a complete mesh query, never a guessed connection");

        cache.Clear(); await cache.FindPath(start, end, Find, default);
        pending = new();
        var warming = cache.FindPath(end, new(200, 0, 0), (_, _, _) => pending.Task, default);
        var hit = cache.FindPath(start, end, Find, default);
        check(hit.IsCompletedSuccessfully && !warming.IsCompleted, "Ready path hits are not queued behind a slow speculative query");
        pending.SetResult([end, new(200, 0, 0)]); await warming; await hit;

        cache.Clear(); await cache.FindPath(start, end, Find, default);
        using var joinCancel = new CancellationTokenSource();
        var joinPending = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        var cancelledJoin = cache.FindPath(offset, end, (_, _, _) => joinPending.Task, joinCancel.Token);
        joinCancel.Cancel(); joinPending.SetResult([offset, new(40, 0, 0)]);
        try { await cancelledJoin; check(false, "Cancelled connector must throw"); }
        catch (OperationCanceledException) { check(cache.Count == 1, "Cancelled connectors do not enter the fixed-route cache"); }

        foreach (var close in new[] { new Vector3(40, 0, 0.5f), new Vector3(-0.5f, 0, 0), new Vector3(40, 0.5f, 0) })
        {
            cache.Clear(); await cache.FindPath(start, end, Find, default);
            check(!cache.TryGet(close, end, out _), "Even a nearby start or vertical offset needs a verified mesh connection");
            connectors.Clear();
            var aroundWall = close + new Vector3(-2, 0, 2);
            joined = await cache.FindPath(close, end, (a, b, _) =>
            {
                connectors.Add((a, b));
                return Task.FromResult<IReadOnlyList<Vector3>>([a, aroundWall, b]);
            }, default);
            check(connectors.Count == 1 && connectors[0].To != end && joined.Contains(aroundWall) && joined[^1] == end,
                "A short connector keeps actual bends around nearby obstacles and reuses the long cached suffix");
        }
    }
}
