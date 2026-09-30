using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Persistent world centre with cursor-anchored zoom; advancing a stop does not refit the map.</summary>
public sealed class MapViewport
{
    public Vector2 Center { get; private set; }
    public Vector2 Extent { get; private set; } = new(2000);
    public float Zoom { get; private set; } = 1;
    public void Fit(IEnumerable<Vector2> points)
    {
        var finite = points.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).ToArray();
        var min = finite.Length > 0 ? finite.Aggregate(Vector2.Min) : new Vector2(-1000);
        var max = finite.Length > 0 ? finite.Aggregate(Vector2.Max) : new Vector2(1000);
        Center = (min + max) / 2; Extent = Vector2.Max(max - min, new Vector2(80)); Zoom = 1;
    }
    public float Scale(Vector2 size) => MathF.Max(0.0001f, MathF.Min(size.X / (Extent.X * 1.15f), size.Y / (Extent.Y * 1.15f))) * Zoom;
    public Vector2 Project(Vector2 world, Vector2 size) => size / 2 + (world - Center) * Scale(size);
    public Vector2 Unproject(Vector2 pixel, Vector2 size) => Center + (pixel - size / 2) / Scale(size);
    public void ZoomAt(float wheel, Vector2 pixel, Vector2 size)
    {
        if (!float.IsFinite(wheel) || !float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y)) return;
        var before = Unproject(pixel, size);
        Zoom = Math.Clamp(Zoom * MathF.Pow(1.2f, Math.Clamp(wheel, -20, 20)), 0.5f, 12);
        Center += before - Unproject(pixel, size);
    }
    public void Pan(Vector2 delta, Vector2 size)
    {
        if (float.IsFinite(delta.X) && float.IsFinite(delta.Y)) Center -= delta / Scale(size);
    }
}
