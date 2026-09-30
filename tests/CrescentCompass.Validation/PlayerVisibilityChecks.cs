using CrescentCompass.Core;

internal static class PlayerVisibilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const ulong model = 2;
        var filter = new PlayerVisibilityFilter(model);
        var stranger = new PlayerVisibilitySample(new(1, 100, 101, 1), 0x80, true, false, false, false, false);
        PlayerVisibilitySample Person(ulong id) => stranger with { Key = new(id, (nint)(100 + id), (uint)(100 + id), (ushort)id) };
        var self = Person(2) with { IsSelf = true };
        var party = Person(3) with { IsParty = true };
        var friend = Person(4) with { IsFriend = true };
        var dead = Person(5) with { IsDead = true };
        var npc = Person(6) with { IsPlayer = false };
        PlayerVisibilitySample[] people = [stranger, self, party, friend, dead, npc];
        check(filter.Update(people, false).Count == 0 && filter.HiddenCount == 0, "Visibility opt-out leaves every model untouched");
        var changes = filter.Update(people, true);
        check(changes.Count == 1 && changes[0].Key == stranger.Key && changes[0].RenderFlags == 0x82, "Only living strangers are hidden; party, friend, self, corpse and NPC stay visible");
        check(filter.HiddenCount == 1, "Only models hidden by this feature are counted");
        var hidden = stranger with { RenderFlags = 0x82 };
        check(filter.Update([hidden], true).Count == 0, "Stable hidden model does not get redundant writes");
        var restored = filter.Update([hidden with { IsDead = true }], true);
        check(restored.Single().RenderFlags == 0x80 && filter.HiddenCount == 0, "A hidden player's death restores its model without clearing unrelated render flags");
        check(filter.Update([stranger], true).Single().RenderFlags == 0x82, "Resurrected living stranger is hidden again");
        check(filter.Update([hidden with { IsParty = true }], true).Single().RenderFlags == 0x80, "Joining the local party restores an already hidden model");
        filter.Update([stranger], true);
        check(filter.Update([hidden with { IsFriend = true }], true).Single().RenderFlags == 0x80, "New friend relationship restores an already hidden model");
        filter.Update([stranger], true);
        check(filter.Update([hidden with { IsSelf = true }], true).Single().RenderFlags == 0x80, "Local player is restored even if previously tracked as another character");
        filter.Update([stranger], true);
        var otherGameFlags = hidden with { RenderFlags = 0x80000882 };
        check(filter.Update([otherGameFlags], false).Single().RenderFlags == 0x80000880, "Disabling preserves nameplate and high render bits changed while hidden");
        check(filter.HiddenCount == 0 && filter.Update([stranger], false).Count == 0, "Restore completes once and releases ownership");
        check(filter.Update([hidden], true).Count == 0 && filter.HiddenCount == 0, "Models already hidden by the game are never claimed");
        check(filter.Update([hidden with { IsDead = true }], true).Count == 0, "Corpse exception does not forcibly reveal models hidden by something else");
        check(filter.Update([hidden], false).Count == 0, "Disabling does not reveal preexisting hidden models");

        filter.Update([stranger], true);
        check(filter.Update([], false).Count == 0 && filter.HiddenCount == 0, "Unloaded objects are forgotten without writing stale pointers");
        filter.Update([stranger], true);
        var recycled = hidden with { Key = new(7, stranger.Key.Address, 107, stranger.Key.ObjectIndex) };
        check(filter.Update([recycled], false).Count == 0 && filter.HiddenCount == 0, "Recycled address and slot do not inherit restoration from the previous player");
        filter.Update([stranger], true);
        var moved = hidden with { Key = stranger.Key with { Address = 999 } };
        check(filter.Update([moved], false).Count == 0 && filter.HiddenCount == 0, "Reused entity ID at a new address does not inherit old ownership");
        filter.Update([stranger], true);
        check(filter.Update([hidden with { IsPlayer = false }], false).Count == 0 && filter.HiddenCount == 0, "A replaced non-player object must never receive restoration writes");
        check(filter.Update([stranger with { Key = stranger.Key with { Address = 0 } }], true).Count == 0, "Invalid address is not a hide target");
        check(filter.Update([stranger with { Key = stranger.Key with { ObjectId = 0 } }], true).Count == 0, "Missing object identity is not a hide target");
        check(filter.Update([stranger with { Key = stranger.Key with { EntityId = 0xE0000000 } }], true).Count == 0, "Sentinel entity ID is not a hide target");

        filter.Update([stranger], true);
        check(filter.Update([hidden], false, _ => false).Count == 0 && filter.HiddenCount == 1, "Failed restoration retains ownership for retry");
        check(filter.Update([hidden], false, _ => true).Single().RenderFlags == 0x80 && filter.HiddenCount == 0, "Next successful frame finishes pending restoration");
        filter.Update([stranger], true);
        var threw = false;
        try { filter.Update([hidden], false, _ => throw new InvalidOperationException("simulated renderer failure")); }
        catch (InvalidOperationException) { threw = true; }
        check(threw && filter.HiddenCount == 1, "Restoration exception cannot lose the record needed to restore");
        check(filter.Update([hidden], false).Count == 1 && filter.HiddenCount == 0, "Restoration recovers after an exception");
        filter.Update([stranger], true);
        check(filter.Update([stranger], false).Count == 0 && filter.HiddenCount == 0, "Game-cleared hidden bit needs no extra write on disable");
        filter.Update([stranger], true);
        check(filter.Update([stranger], true).Single().RenderFlags == 0x82 && filter.HiddenCount == 1, "Active filter reapplies a model bit cleared by a render refresh");
        check(filter.Update([hidden, hidden], false).Count == 1 && filter.HiddenCount == 0, "Duplicate snapshot entries cannot cause double restoration");
    }
}
