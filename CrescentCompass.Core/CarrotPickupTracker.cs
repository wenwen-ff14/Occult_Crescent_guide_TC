using System.Numerics;

namespace CrescentCompass.Core;

public sealed record CarrotPickup(string PadId, ulong Sequence);

/// <summary>Observe local item use independently of route selection, movement, and automatic gathering.</summary>
public sealed class CarrotPickupTracker
{
    private sealed class Pending(Spot pad, Observation carrot, int count, long now, IEnumerable<Observation> observations)
    {
        public readonly string PadId = pad.Id;
        public readonly Observation Carrot = carrot;
        public readonly int Count = count;
        public long StartedAt = now;
        public readonly HashSet<ulong> Bunnies = observations.Where(o => o.Kind == SpotKind.RabbitGold).Select(o => o.ObjectId).ToHashSet();
        public bool UseObserved;
        public bool Reported;
        public long? DecrementAt;
    }

    private Pending? pending;
    private long? lastScan;
    private ulong sequence;
    public void Reset() { pending = null; lastScan = null; }
    public void RecordUse(Spot pad, Observation carrot, int count, long now, IReadOnlyList<Observation> observations)
    {
        if (pending?.Carrot.ObjectId != carrot.ObjectId || pending.PadId != pad.Id || pending.Count != count)
            pending = new(pad, carrot, count, now, observations);
        pending.UseObserved = true; pending.StartedAt = now;
    }
    public void RecordManual(string padId)
    {
        if (pending is { } candidate && candidate.PadId == padId && (candidate.UseObserved || candidate.DecrementAt is not null))
            candidate.Reported = true;
    }

    public CarrotPickup? Update(IEnumerable<Spot> pads, IReadOnlyList<Observation> observations, Vector3 player,
        int? itemCount, bool casting, long now)
    {
        if (lastScan is { } last && (now < last || now - last > 1500)) pending = null;
        lastScan = now;
        if (!Coordinates.IsFinite(player)) { pending = null; return null; }
        if (pending is { } previous && (now - previous.StartedAt > 30_000 || Vector3.DistanceSquared(player, previous.Carrot.Position) > 36))
            pending = null;
        if (pending is { } candidate)
        {
            if (!candidate.UseObserved && candidate.DecrementAt is null && itemCount == candidate.Count) candidate.StartedAt = now;
            candidate.UseObserved |= casting && CarrotGathering.InRange(player, candidate.Carrot);
            if (itemCount is { } count)
            {
                if (count == candidate.Count - 1)
                {
                    candidate.DecrementAt ??= now;
                    var newBunny = observations.Any(o => o.Kind == SpotKind.RabbitGold && o.Available && ValidObject(o.ObjectId) &&
                        !candidate.Bunnies.Contains(o.ObjectId) && Coordinates.IsFinite(o.Position) &&
                        Vector3.DistanceSquared(o.Position, candidate.Carrot.Position) <= 100);
                    // A cast/request alone, marker disappearance, or an inventory edit alone is not a pickup.
                    if (candidate.UseObserved || newBunny)
                    {
                        pending = null;
                        return candidate.Reported ? null : new(candidate.PadId, ++sequence);
                    }
                    if (now - candidate.DecrementAt > 3000) pending = null;
                }
                else if (count != candidate.Count || candidate.DecrementAt is not null) pending = null;
            }
        }
        if (pending is null && itemCount is > 0)
        {
            var live = observations.Where(o => o.Kind == SpotKind.Carrot && o.Available && ValidObject(o.ObjectId) &&
                CarrotGathering.InRange(player, o)).OrderBy(o => Vector3.DistanceSquared(player, o.Position)).FirstOrDefault();
            if (live is not null)
            {
                var pad = pads.Where(s => CarrotRoute.Number(s) is not null && Coordinates.IsFinite(s.Position) &&
                    Vector3.DistanceSquared(s.Position, live.Position) <= 16).OrderBy(s => Vector3.DistanceSquared(s.Position, live.Position)).FirstOrDefault();
                if (pad is not null) pending = new(pad, live, itemCount.Value, now, observations) { UseObserved = casting };
            }
        }
        return null;
    }

    private static bool ValidObject(ulong id) => id != 0 && id != 0xE0000000;
}
