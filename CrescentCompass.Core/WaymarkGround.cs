using System.Numerics;

namespace CrescentCompass.Core;

public readonly record struct WaymarkGroundHit(Vector3 Point, Vector3 V1, Vector3 V2, Vector3 V3, Vector3 Normal);

public static class WaymarkGround
{
    public static SavedWaymark Snap(SavedWaymark marker, WaymarkGroundHit hit)
    {
        if (!marker.Active) return SavedWaymark.Off;
        if (!Coordinates.IsFinite(marker.Position) || !Coordinates.IsFinite(hit.Point) || Vector3.Distance(hit.Point, marker.Position) > 1.05f)
            throw new InvalidOperationException("地面高度與儲存位置不符，請確認標點樓層。");

        // Mesh raycasts populate the hit triangle but may leave Normal at zero.
        // Use its winding to retain the distinction between floor and underside.
        // Primitive colliders can instead supply a normal without a triangle.
        var triangle = Coordinates.IsFinite(hit.V1) && Coordinates.IsFinite(hit.V2) && Coordinates.IsFinite(hit.V3)
            ? Vector3.Cross(hit.V2 - hit.V1, hit.V3 - hit.V1) : Vector3.Zero;
        if (!TryNormalize(triangle, out var normal) && !TryNormalize(hit.Normal, out normal))
            throw new InvalidOperationException("無法確認標點地面方向，請走近場地後重試。");
        if (normal.Y < .7f)
            throw new InvalidOperationException("標點位置過陡或位於地面背面，已停止放置。");
        return marker with { Y = MathF.Round(hit.Point.Y, 3) };
    }

    private static bool TryNormalize(Vector3 vector, out Vector3 normal)
    {
        var lengthSquared = vector.LengthSquared();
        if (!Coordinates.IsFinite(vector) || !float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
        { normal = Vector3.Zero; return false; }
        normal = vector / MathF.Sqrt(lengthSquared);
        return true;
    }
}
