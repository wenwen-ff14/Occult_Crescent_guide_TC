namespace CrescentCompass.Core;

public readonly record struct FriendIdentity(ulong ContentId, string Name, ushort HomeWorld);

// Content IDs identify renamed characters; name + home world covers non-party
// players whose loaded character has no content ID. Snapshots belong to one local character.
public sealed class PlayerFriendRoster
{
    private ulong owner;
    private readonly HashSet<ulong> contentIds = [];
    private readonly HashSet<(string Name, ushort World)> names = [];
    private readonly HashSet<(string Name, ushort World)> namesWithoutContentId = [];
    private FriendIdentity[]? candidate;
    private long candidateSince;
    public FriendIdentity[] Snapshot { get; private set; } = [];
    public bool HasSnapshot { get; private set; }
    public int Count { get; private set; }

    public void Clear()
    {
        owner = 0;
        Count = 0;
        HasSnapshot = false;
        Snapshot = [];
        candidate = null;
        contentIds.Clear(); names.Clear(); namesWithoutContentId.Clear();
    }

    public void Replace(ulong localContentId, IEnumerable<FriendIdentity> entries)
    {
        Clear();
        if (localContentId == 0) return;
        owner = localContentId;
        HasSnapshot = true;
        Snapshot = Normalize(entries);
        foreach (var entry in Snapshot)
        {
            var hasName = !string.IsNullOrWhiteSpace(entry.Name) && entry.HomeWorld is > 0 and < ushort.MaxValue;
            if (entry.ContentId == 0 && !hasName) continue;
            Count++;
            if (entry.ContentId != 0) contentIds.Add(entry.ContentId);
            if (!hasName) continue;
            var key = (entry.Name, entry.HomeWorld);
            names.Add(key);
            if (entry.ContentId == 0) namesWithoutContentId.Add(key);
        }
    }

    // Require the same complete-looking sample for a second poll before persisting it.
    // A missing/empty instance list must not erase the last usable outside-island snapshot.
    public bool Observe(ulong localContentId, FriendIdentity[]? entries, bool allowEmpty, long now)
    {
        if (owner != localContentId) { Clear(); owner = localContentId; }
        if (localContentId == 0 || entries is null) { candidate = null; return false; }
        var normalized = Normalize(entries);
        if (normalized.Length == 0 && (!allowEmpty || entries.Length != 0)) { candidate = null; return false; }
        if (HasSnapshot && Snapshot.SequenceEqual(normalized)) { candidate = null; return false; }
        if (candidate is null || !candidate.SequenceEqual(normalized))
        { candidate = normalized; candidateSince = now; return false; }
        if (now - candidateSince < 1000) return false;
        Replace(localContentId, normalized);
        return true;
    }

    private static FriendIdentity[] Normalize(IEnumerable<FriendIdentity> entries) => entries
        .Where(e => e.ContentId != 0 || (!string.IsNullOrWhiteSpace(e.Name) && e.HomeWorld is > 0 and < ushort.MaxValue))
        .Distinct().OrderBy(e => e.ContentId).ThenBy(e => e.HomeWorld).ThenBy(e => e.Name, StringComparer.Ordinal).ToArray();

    public bool Contains(ulong localContentId, FriendIdentity player)
    {
        if (owner == 0 || owner != localContentId) return false;
        if (player.ContentId != 0 && contentIds.Contains(player.ContentId)) return true;
        if (string.IsNullOrWhiteSpace(player.Name) || player.HomeWorld is 0 or ushort.MaxValue) return false;
        var key = (player.Name, player.HomeWorld);
        return player.ContentId == 0 ? names.Contains(key) : namesWithoutContentId.Contains(key);
    }
}
