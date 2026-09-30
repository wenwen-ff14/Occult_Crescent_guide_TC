using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Keep an early hint until the buff scan catches up; merge duplicate chat/toast deliveries.</summary>
public sealed class PotHintQueue
{
    private readonly object gate = new();
    private readonly Queue<Entry> entries = new();
    private readonly Dictionary<(PotHint Hint, ushort Territory, uint Instance), DateTimeOffset> recent = new();
    private sealed record Entry(PotHint Hint, Vector3 Origin, ushort Territory, uint Instance, DateTimeOffset Time);

    public void Clear() { lock (gate) { entries.Clear(); recent.Clear(); } }

    public void Enqueue(PotHint hint, Vector3 origin, ushort territory, uint instance, DateTimeOffset time)
    {
        if (!SpotCatalog.IsSupported(territory) || !Coordinates.IsFinite(origin)) return;
        lock (gate)
        {
            foreach (var key in recent.Where(p => time - p.Value >= TimeSpan.FromSeconds(1)).Select(p => p.Key).ToArray()) recent.Remove(key);
            var keyNow = (hint, territory, instance);
            if (recent.ContainsKey(keyNow)) return;
            recent[keyNow] = time;
            if (entries.Count >= 32) entries.Dequeue();
            entries.Enqueue(new(hint, origin, territory, instance, time));
        }
    }

    public void ApplyTo(PotSession pot, ushort territory, uint instance, DateTimeOffset now)
    {
        lock (gate)
        {
            while (entries.TryPeek(out var entry))
            {
                if (entry.Territory != territory || entry.Instance != instance || now - entry.Time >= TimeSpan.FromSeconds(3))
                { entries.Dequeue(); continue; }
                if (!pot.Active || pot.Territory != territory) return;
                entries.Dequeue();
                pot.Apply(entry.Hint, entry.Origin, entry.Time);
            }
        }
    }
}
