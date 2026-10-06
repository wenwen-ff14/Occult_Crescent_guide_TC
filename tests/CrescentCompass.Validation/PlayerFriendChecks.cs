using CrescentCompass.Core;
using System.Text.Json;

internal static class PlayerFriendChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const ulong owner = 42;
        var roster = new PlayerFriendRoster();
        var friend = new FriendIdentity(123, "島上好友", 74);
        var noId = friend with { ContentId = 0 };
        check(!roster.HasSnapshot && !roster.Contains(owner, friend), "No friend data never pretends to be a loaded empty list");
        roster.Replace(owner, [friend]);
        check(roster.Contains(owner, friend), "Explicit roster identifies non-party friend without relationship flag");
        check(roster.Contains(owner, noId), "Non-party friend with no native content ID matches name and home world");
        check(!roster.Contains(owner, noId with { HomeWorld = 75 }), "Same name on different home world is not a friend");
        check(!roster.Contains(owner, noId with { Name = "其他玩家" }), "Same home world alone does not identify a friend");
        check(!roster.Contains(owner, friend with { ContentId = 124 }), "Conflicting nonzero content IDs do not fall back to same-name matching");
        check(roster.Contains(owner, friend with { Name = "改名好友" }), "Stable content ID identifies renamed friend");
        check(!roster.Contains(owner + 1, friend) && !roster.Contains(0, friend), "Friend snapshot cannot leak to another local character");
        check(!roster.Contains(owner, noId with { HomeWorld = 0 }) && !roster.Contains(owner, noId with { HomeWorld = ushort.MaxValue }), "Missing or sentinel home world cannot produce a name match");
        roster.Replace(owner, [noId]);
        check(roster.Contains(owner, friend), "Roster without content ID still allows exact name and home world");
        roster.Replace(owner, [friend]);
        var filter = new PlayerVisibilityFilter(2);
        var player = new PlayerVisibilitySample(new(1, 100, 101, 1), 0x80, true, false, false, false, false);
        filter.Update([player], true);
        var change = filter.Update([player with { RenderFlags = 0x82, IsFriend = roster.Contains(owner, noId) }], true);
        check(change.Count == 1 && change[0].RenderFlags == 0x80 && filter.HiddenCount == 0, "Roster match restores previously hidden non-party friend while preserving unrelated bits");

        var serialized = JsonSerializer.Serialize(roster.Snapshot);
        roster.Clear();
        roster.Replace(owner, JsonSerializer.Deserialize<FriendIdentity[]>(serialized)!);
        check(roster.HasSnapshot && roster.Contains(owner, noId), "Saved per-character roster works after plugin reload inside island");
        check(!roster.Observe(owner, null, false, 1000) && roster.Contains(owner, noId), "Unavailable island list preserves saved friends");
        check(!roster.Observe(owner, [], false, 2000) && roster.Contains(owner, noId), "Empty inaccessible island list cannot clear saved friends");
        check(!roster.Observe(owner, [], true, 3000) && roster.Contains(owner, noId), "First empty open-list sample does not erase friends during loading");
        check(roster.Observe(owner, [], true, 4000) && roster.HasSnapshot && roster.Count == 0, "Stable explicitly opened empty list clears removed friends");
        check(!roster.Contains(owner, noId), "Removed friend no longer matches");

        var other = new FriendIdentity(456, "另一好友", 75);
        check(!roster.Observe(owner, [friend], false, 5000), "Partial list needs another sample before replacing snapshot");
        check(!roster.Observe(owner, [friend, other], false, 6000), "Growing list restarts stability window");
        check(!roster.Observe(owner, [other, friend], false, 6500), "List reorder does not shorten the minimum stable interval");
        check(roster.Observe(owner, [other, friend, friend], false, 7000) && roster.Count == 2, "Stable list accepts reordering and duplicate entries once");
        check(!roster.Observe(owner, [friend, other], false, 8000), "Unchanged roster does not repeatedly save plugin config");
        check(!roster.Observe(owner, [new(0, "", 0)], true, 9000) && roster.Count == 2, "Malformed list cannot erase known friends");
        check(!roster.Observe(owner + 1, null, false, 10000) && !roster.HasSnapshot && !roster.Contains(owner, friend), "Character switch clears snapshot before reading new character data");
        roster.Replace(0, [friend]);
        check(!roster.HasSnapshot, "Logged-out state cannot own a persisted roster");
    }
}
