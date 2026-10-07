using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Session-local, directed mesh paths shared by planning, movement and inspection.</summary>
public sealed class PatrolPathCache
{
    public const int Capacity = 256;
    public const float JoinRadius = 1.5f;
    public const float ReuseTolerance = 0.05f;
    public const float ConnectorRadius = 6;
    private readonly object sync = new();
    private readonly SemaphoreSlim queryLock = new(1, 1);
    private readonly List<(Vector3 From, Vector3 To, Vector3[] Path)> entries = [];
    private int generation;
    public int Count { get { lock (sync) return entries.Count; } }

    public void Clear() { lock (sync) { entries.Clear(); generation++; } }

    public void Forget(Vector3 destination)
    {
        lock (sync) { entries.RemoveAll(e => e.To == destination); generation++; }
    }

    public bool TryGet(Vector3 from, Vector3 to, out IReadOnlyList<Vector3> path)
    {
        path = [];
        if (!Coordinates.IsFinite(from) || !Coordinates.IsFinite(to)) return false;
        lock (sync)
        {
            foreach (var entry in entries.Where(e => e.To == to).OrderBy(e => e.From == from ? 0 : 1))
            {
                if (entry.From == from) { path = entry.Path.ToArray(); return true; }
                // Proximity alone cannot prove a short connector is clear of walls or props.
                if (TryResume(from, entry.Path, out path, ReuseTolerance)) return true;
            }
        }
        path = []; return false;
    }

    public async Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to,
        Func<Vector3, Vector3, CancellationToken, Task<IReadOnlyList<Vector3>>> find, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Coordinates.IsFinite(from) || !Coordinates.IsFinite(to)) throw new ArgumentException("Invalid path endpoints.");
        // A speculative next-leg query must not delay a ready current-leg cache hit.
        if (TryGet(from, to, out var ready)) return ready;
        await queryLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            int version;
            lock (sync) { version = generation; if (TryGet(from, to, out var cached)) return cached; }
            Vector3[]? suffix = null;
            lock (sync)
            {
                suffix = entries.Where(e => e.To == to)
                    .Select(e => TryResume(from, e.Path, out var tail, ConnectorRadius) ? tail.ToArray() : null)
                    .Where(p => p is { Length: > 0 } && Math.Abs(from.Y - p[0].Y) <= JoinRadius)
                    .OrderBy(p => Vector3.DistanceSquared(from, p![0])).FirstOrDefault();
            }
            IReadOnlyList<Vector3>? connected = null;
            if (suffix is not null)
            {
                var join = await find(from, suffix[0], cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                // Only splice an actual mesh connector ending at the polyline, never a straight shortcut.
                if (WalkingRoutePlanner.ValidatePath("connector", from, suffix[0], join) is { Length: <= ConnectorRadius * 3 } &&
                    Vector3.DistanceSquared(join[^1], suffix[0]) <= 0.0625f)
                    connected = join.Concat(suffix).ToArray();
            }
            var path = (connected ?? await find(from, to, cancellation).ConfigureAwait(false)).ToArray();
            cancellation.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (version != generation) throw new OperationCanceledException("Navigation cache was invalidated.");
                if (WalkingRoutePlanner.ValidatePath("cache", from, to, path) is not null)
                {
                    if (entries.Count >= Capacity) entries.RemoveAt(0);
                    entries.Add((from, to, path));
                }
            }
            return path.ToArray();
        }
        finally { queryLock.Release(); }
    }

    // Join only on the original mesh polyline, never cut across corners or different floors.
    public static bool TryResume(Vector3 from, IReadOnlyList<Vector3> path, out IReadOnlyList<Vector3> remaining,
        float radius = JoinRadius)
    {
        remaining = [];
        if (!Coordinates.IsFinite(from) || path.Count == 0 || path.Any(p => !Coordinates.IsFinite(p))) return false;
        var best = float.MaxValue; var index = 0; var projected = path[0];
        for (var i = 0; i < Math.Max(1, path.Count - 1); i++)
        {
            var a = path[i]; var b = path[Math.Min(i + 1, path.Count - 1)];
            var delta = b - a;
            var t = delta.LengthSquared() == 0 ? 0 : Math.Clamp(Vector3.Dot(from - a, delta) / delta.LengthSquared(), 0, 1);
            var point = a + delta * t;
            var distance = Vector3.DistanceSquared(from, point);
            if (distance >= best) continue;
            best = distance; index = i; projected = point;
        }
        if (best > radius * radius) return false;
        remaining = new[] { projected }.Concat(path.Skip(index + 1)).ToArray();
        return true;
    }
}
