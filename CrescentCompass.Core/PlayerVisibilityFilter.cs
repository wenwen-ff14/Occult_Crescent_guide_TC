namespace CrescentCompass.Core;

// A recycled address or object-table slot is not the same player instance.
public readonly record struct PlayerVisibilityKey(ulong ObjectId, nint Address, uint EntityId, ushort ObjectIndex);
public sealed record PlayerVisibilitySample(PlayerVisibilityKey Key, ulong RenderFlags,
    bool IsPlayer, bool IsSelf, bool IsParty, bool IsFriend, bool IsDead);
public readonly record struct PlayerVisibilityChange(PlayerVisibilityKey Key, ulong RenderFlags);

public sealed class PlayerVisibilityFilter(ulong hideMask)
{
    private readonly Dictionary<PlayerVisibilityKey, ulong> ownedBits = [];
    public int HiddenCount => ownedBits.Count;

    public static bool ShouldHide(PlayerVisibilitySample player, bool enabled) => enabled && player.IsPlayer &&
        player.Key.Address != 0 && player.Key.ObjectId != 0 && player.Key.EntityId is not (0 or 0xE0000000) &&
        !player.IsSelf && !player.IsParty && !player.IsFriend && !player.IsDead;

    public IReadOnlyList<PlayerVisibilityChange> Update(IReadOnlyList<PlayerVisibilitySample> snapshot, bool enabled,
        Func<PlayerVisibilityChange, bool>? write = null)
    {
        var seen = new HashSet<PlayerVisibilityKey>();
        var changes = new List<PlayerVisibilityChange>();
        foreach (var player in snapshot)
        {
            if (!seen.Add(player.Key)) continue;
            if (!player.IsPlayer) { ownedBits.Remove(player.Key); continue; }
            var current = player.RenderFlags;
            var hide = ShouldHide(player, enabled);
            if (hide)
            {
                // Never take ownership of a bit already set by the game or another plugin.
                var added = hideMask & ~current;
                if (added != 0) ownedBits[player.Key] = ownedBits.GetValueOrDefault(player.Key) | added;
                current |= hideMask;
            }
            else if (ownedBits.TryGetValue(player.Key, out var owned)) current &= ~owned;
            if (current != player.RenderFlags)
            {
                var change = new PlayerVisibilityChange(player.Key, current);
                // Retain restoration ownership until the write succeeds; a failed frame can retry safely.
                if (write is not null && !write(change)) continue;
                changes.Add(change);
            }
            if (!hide) ownedBits.Remove(player.Key);
        }
        // Departed objects must not be dereferenced later, including when the feature is disabled.
        foreach (var key in ownedBits.Keys.Where(k => !seen.Contains(k)).ToArray()) ownedBits.Remove(key);
        return changes;
    }
}
