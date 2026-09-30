using System.Numerics;

namespace CrescentCompass.Core;

public sealed record ChestOpenProgress(string ChartId, int Number, DateTimeOffset OpenedAt, bool Manual);

/// <summary>Requires a local-player cast at this exact chest followed by an observed opened state.
/// Disappearance, other players' opens, and manual patrol completion are not collection evidence.</summary>
public sealed class ChestOpenTracker
{
    private sealed record Attempt(Observation Chest, int Number, long LastCasting);
    private Attempt? attempt;
    public void Reset() => attempt = null;

    public ChestOpenProgress? Update(IEnumerable<Spot> catalog, IReadOnlyList<Observation> observations,
        Vector3 player, ulong castTarget, long now, DateTimeOffset time)
    {
        if (!Coordinates.IsFinite(player)) { Reset(); return null; }
        var targeted = observations.FirstOrDefault(o => o.ObjectId == castTarget && castTarget != 0 &&
            o.Available && (o.Targetable || o.Opening) && Vector3.DistanceSquared(o.Position, player) <= 36);
        if (targeted is not null)
        {
            var spot = catalog.FirstOrDefault(s => ChestChart.Number(s) is not null && s.Kind == targeted.Kind &&
                Vector3.DistanceSquared(s.Position, targeted.Position) <= 16);
            if (spot is not null) attempt = new(targeted, ChestChart.Number(spot)!.Value, now);
        }
        if (attempt is not { } current) return null;
        if (now < current.LastCasting || now - current.LastCasting > 8000 ||
            Vector3.DistanceSquared(player, current.Chest.Position) > 100)
        { Reset(); return null; }
        var chest = observations.FirstOrDefault(o => o.ObjectId == current.Chest.ObjectId &&
            o.DataId == current.Chest.DataId && o.Kind == current.Chest.Kind &&
            Vector3.DistanceSquared(o.Position, current.Chest.Position) <= 16);
        if (chest is { Available: true, Opening: false } && castTarget != current.Chest.ObjectId)
        { Reset(); return null; } // Stopped casting while still unopened: do not retain a cancelled interaction.
        if (chest is not { Available: false }) return null;
        Reset();
        return new(ChestChart.Id, current.Number, time, false);
    }
}
