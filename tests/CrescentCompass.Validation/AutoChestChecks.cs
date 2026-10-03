using System.Numerics;
using CrescentCompass.Core;

internal static class AutoChestChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var ready = new ChestInteractionContext(1252, 1, 123, true);
        var chest = new Observation(10, 1234, SpotKind.Silver, new(1.5f, 0, 0));
        var opener = new AutoChestOpener();
        List<ulong> calls = [];
        bool Interact(Observation o) { calls.Add(o.ObjectId); return true; }
        void Tick(long now, IReadOnlyList<Observation>? objects = null, ChestInteractionContext? context = null,
            Vector3? player = null, bool enabled = true) => opener.Update(enabled, context ?? ready, objects ?? [chest], player ?? Vector3.Zero, now, Interact);
        Tick(0, enabled: false);
        check(calls.Count == 0, "Auto chest is opt-in: disabled mode never interacts");
        foreach (var blocked in new[] { ready with { Territory = 1 }, ready with { CharacterId = 0 }, ready with { Ready = false },
            ready with { InCombat = true }, ready with { Occupied = true },
            ready with { Mounted = true, InFlight = true }, ready with { Mounted = true, RidingPillion = true },
            ready with { Mounted = true, Occupied = true }, ready with { Mounted = true, InCombat = true },
            ready with { Dead = true }, ready with { Paused = true } })
        {
            opener.Reset(); Tick(0, context: blocked);
            check(calls.Count == 0 && opener.Detail == blocked.BlockReason, $"Auto chest blocks and explains {blocked}");
        }
        foreach (var invalid in new[] { chest with { ObjectId = 0 }, chest with { ObjectId = 0xE0000000 },
            chest with { Kind = SpotKind.Carrot }, chest with { Kind = SpotKind.Exploration }, chest with { Available = false },
            chest with { Targetable = false }, chest with { Opening = true }, chest with { Position = new(2.01f, 0, 0) },
            chest with { Position = new(0, 2.01f, 0) }, chest with { Position = new(float.NaN, 0, 0) } })
        {
            opener.Reset(); Tick(0, [invalid]);
            check(calls.Count == 0, $"Auto chest excludes invalid, unavailable or distant observation: {invalid}");
        }
        opener.Reset(); Tick(0, player: new(float.PositiveInfinity, 0, 0));
        check(calls.Count == 0, "Auto chest waits for finite player coordinates");
        opener.Reset(); Tick(0, context: ready with { Mounted = true });
        check(calls.SequenceEqual(new ulong[] { 10 }), "Ground-mounted player may interact with a nearby chest");
        Tick(100, context: ready with { Mounted = false });
        check(calls.Count == 1, "Dismounting does not reset the interaction cooldown");
        Tick(5000, [chest with { Available = false }], ready with { Mounted = true });
        check(calls.Count == 1, "Mounted interaction still excludes opened chests");
        calls.Clear();
        var second = chest with { ObjectId = 20, Position = new(2, 0, 0), Kind = SpotKind.PotGold };
        opener.Reset(); Tick(0, [second, chest]);
        check(calls.SequenceEqual(new ulong[] { 10 }), "Chooses nearest loaded chest; one call per update");
        Tick(999, [second, chest]);
        check(calls.Count == 1, "Global cooldown covers all nearby chests, not just the same object");
        Tick(5000, [second, chest with { Available = false }]);
        check(calls.SequenceEqual(new ulong[] { 10, 20 }), "Opened chest excluded; next nearby event chest may interact at 2m boundary");
        Tick(10000, [chest]);
        check(calls.Count == 2, "Spent evidence prevents poking a chest whose state later flickers");
        Tick(10500, [], player: new(10, 0, 0)); Tick(15000);
        check(calls.Count == 3, "Leaving 6m rearms the chest for a later visit");

        calls.Clear(); opener.Reset();
        Tick(0); Tick(1000); Tick(2000); Tick(3000); Tick(4000);
        check(calls.Count == 5, "Keeps trying nearby unopened chest beyond three attempts without a stationary delay");
        Tick(30000, [chest, second]);
        check(calls.Count == 6 && calls[^1] == 20, "A rejected chest does not starve another nearby chest");
        Tick(35000, [chest with { Opening = true }, second]);
        check(calls.Count == 6, "Waits for nearby opening animation instead of stacking another interaction");
        Tick(40000, context: ready with { Occupied = true });
        check(calls.Count == 6, "Casting/interaction pause does not submit another call");
        Tick(45000);
        check(calls.Count == 7, "Continues nearby interaction after busy state clears");

        Tick(45001, enabled: false); Tick(45002);
        check(calls.Count == 8, "Disable stops immediately and re-enable resets retry history");
        Tick(45003, context: ready with { Instance = 2 });
        check(calls.Count == 9, "Different instance starts a fresh chest session");
        Tick(45004, context: ready with { Instance = 2, CharacterId = 999 });
        check(calls.Count == 10, "Different character never inherits another character's suppression");
        Tick(1, context: ready with { Instance = 2, CharacterId = 999 });
        check(calls.Count == 11, "Clock rollback clears timing state instead of waiting indefinitely");

        calls.Clear(); opener.Reset(); Tick(0); Tick(5000); Tick(10000);
        Tick(30000, []); Tick(41500, []); Tick(42000);
        check(calls.Count == 4, "An object unloaded for over 30 seconds may be a fresh spawn");
        Tick(47000, [chest with { Position = new(-2, 0, 0) }]);
        check(calls.Count == 5, "Reused object ID at a different position starts a fresh attempt history");
        calls.Clear(); opener.Reset(); Tick(0); Tick(5000); Tick(10000); Tick(41000);
        check(calls.Count == 4, "First scan after a long loading/busy gap expires old observations before refreshing last-seen time");

        opener.Reset(); var rejected = 0;
        bool Refuse(Observation _) { rejected++; return false; }
        opener.Update(true, ready, [chest], Vector3.Zero, 0, Refuse);
        opener.Update(true, ready, [chest], Vector3.Zero, 100, Refuse);
        check(rejected == 1, "Native preflight rejection is rate limited too");
        opener.Reset(); var faults = 0;
        try { opener.Update(true, ready, [chest], Vector3.Zero, 0, _ => { faults++; throw new InvalidOperationException(); }); }
        catch (InvalidOperationException) { }
        opener.Update(true, ready, [chest], Vector3.Zero, 100, _ => { faults++; return true; });
        check(faults == 1, "An exception cannot trigger repeated calls every frame");

        var tracker = new ChestOpenTracker();
        check(tracker.Update([], [chest with { Available = false }], Vector3.Zero, 0, 0, DateTimeOffset.UtcNow) is null,
            "Automatic interaction alone does not forge chest collection or chart progress");

        calls.Clear(); opener.Reset();
        for (var tick = 0; tick < 6000; tick += 100) Tick(tick, [chest, second]);
        check(calls.SequenceEqual(new ulong[] { 10, 20, 10, 20, 10, 20 }), "Continuous retry is limited to one per second and fairly rotates nearby boxes");
        Tick(6000, player: new(-1, 0, 0));
        check(calls.Count == 6, "Leaving the two metre interaction radius immediately stops attempts");
        Tick(6100, player: new(0.2f, 0, 0));
        check(calls.Count == 7, "Returning while moving interacts on the first eligible scan");
    }
}
