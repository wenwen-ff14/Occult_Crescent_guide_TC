using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Near-pad absence or nearby streamed coffers can establish a bounded early inspection window.</summary>
public static class PatrolEmptyCheck
{
    public const float HeightTolerance = 6;
    public const float NearRadius = 20;
    public const float EarlyRadius = 60;
    public const float StreamRadius = 25;
    public const long ConfirmationMs = 1000;

    public static string? BlockReason(Vector3 player, Spot target, IReadOnlyList<Observation> observations,
        RouteLeg? groundPath, float configuredRadius)
    {
        var radius = Math.Min(EarlyRadius, RouteAutomation.NormalizeRadius(configuredRadius));
        if (!Coordinates.IsFinite(player) || !Coordinates.IsFinite(target.Position) ||
            Math.Abs(player.Y - target.Position.Y) > HeightTolerance ||
            Vector3.DistanceSquared(player, target.Position) > radius * radius)
            return "尚未進入同層提前空點確認範圍。";
        if (groundPath is null || groundPath.DestinationId != target.Id ||
            !double.IsFinite(groundPath.Length) || groundPath.Length < 0 ||
            groundPath.Length + Vector3.Distance(player, groundPath.From) > radius)
            return "等待可通行路徑；不以直線距離判定空點。";
        if (Vector3.DistanceSquared(player, target.Position) <= NearRadius * NearRadius) return null;
        var streamed = observations.Any(o => o.Kind is SpotKind.Bronze or SpotKind.Silver &&
            o.ObjectId != 0 && o.ObjectId != 0xE0000000 && Coordinates.IsFinite(o.Position) &&
            Math.Abs(o.Position.Y - target.Position.Y) <= HeightTolerance &&
            Vector3.DistanceSquared(o.Position, target.Position) <= StreamRadius * StreamRadius &&
            Vector3.DistanceSquared(o.Position, player) <= EarlyRadius * EarlyRadius);
        return streamed ? null : "附近箱體尚未提供載入證據；接近 20 公尺內再確認空點。";
    }
}
