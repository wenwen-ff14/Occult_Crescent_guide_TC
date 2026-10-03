using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Static horizontal reference only; never used to authorize placement.</summary>
public sealed record TowerArena(string Name, Vector2 Center, Vector2 HalfSize, bool Circular)
{
    public bool Contains(Vector2 point) => Circular
        ? Vector2.DistanceSquared(point, Center) <= HalfSize.X * HalfSize.X
        : Math.Abs(point.X - Center.X) <= HalfSize.X && Math.Abs(point.Y - Center.Y) <= HalfSize.Y;
}

public static class WaymarkPreview
{
    // Geometry facts: awgil/ffxiv_bossmod, commit 40b0abdd35e81517409bdc81446910f943a75a49.
    // See docs/WAYMARK_PREVIEW.md for exact sources and limits.
    public static IReadOnlyList<TowerArena> Arenas { get; } = Array.AsReadOnly(new TowerArena[]
    {
        new("力之塔 1 王 · Demon Tablet", new(700, 379), new(15, 33), false),
        new("力之塔 2 王 · Dead Stars", new(-800, 360), new(35), true),
        new("力之塔 3 王 · Marble Dragon", new(-337, 157), new(30), true),
        new("力之塔 4 王 · Magitaur", new(700, -674), new(31.5f), true),
    });

    public static Vector2 Horizontal(SavedWaymark marker) => new(marker.X, marker.Z);

    public static int Detect(WaymarkPreset preset)
    {
        var points = preset.Markers.Where(m => m.Active).Select(Horizontal).ToArray();
        if (points.Length == 0 || preset.Territory != WaymarkPreset.SouthHorn) return -1;
        // Require every point near the same room, rather than guessing from a distant centroid.
        return Enumerable.Range(0, Arenas.Count).FirstOrDefault(i =>
            points.All(p => Vector2.Distance(p, Arenas[i].Center) <= 65), -1);
    }

    public static (Vector2 Center, float Span) Frame(WaymarkPreset preset, TowerArena? arena)
    {
        var points = preset.Markers.Where(m => m.Active).Select(Horizontal).ToList();
        if (arena is not null) { points.Add(arena.Center - arena.HalfSize); points.Add(arena.Center + arena.HalfSize); }
        if (points.Count == 0) return (Vector2.Zero, 20);
        var min = points.Aggregate(Vector2.Min); var max = points.Aggregate(Vector2.Max);
        return ((min + max) / 2, Math.Max(20, Math.Max(max.X - min.X, max.Y - min.Y)) * 1.2f);
    }
}
