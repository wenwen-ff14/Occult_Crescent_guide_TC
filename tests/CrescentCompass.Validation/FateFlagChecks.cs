using System.Numerics;
using CrescentCompass.Core;

internal static class FateFlagChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var tracker = new FateAutoFlagger();
        var flags = new List<ushort>();
        FateFlagObservation Row(ushort id = 1963, int start = 0) => new(id, 1252, $"FATE {id}", new(100, 0, 100), time.AddSeconds(start).ToUnixTimeSeconds(), 900, 0, true);
        void Scan(int s, FateFlagObservation[] rows, bool enabled = true, uint instance = 1, ushort territory = 1252) => tracker.Observe(territory, instance, rows, time.AddSeconds(s), enabled, Vector3.Zero);
        void Tick(int s, bool occupied = false, bool pot = false, bool success = true) => tracker.Tick(time.AddSeconds(s), true, occupied, pot, f => { flags.Add(f.Id); return success; });
        Scan(0, [Row()]); Tick(0);
        check(flags.SequenceEqual(new ushort[] { 1963 }) && tracker.OwnsNavigation(time), "Ordinary non-pot FATE flags on first observation and holds route navigation");
        Scan(1, [Row(), Row()]); Tick(1);
        check(flags.Count == 1, "Same FATE and duplicate rows never repeatedly open the map");
        tracker.Suspend(); Tick(2);
        check(tracker.Active(time.AddSeconds(2)).Count == 0 && flags.Count == 1, "Transit cannot flag stale locations");
        Scan(10, [Row()]); Tick(10);
        check(flags.Count == 1 && tracker.OwnsNavigation(time.AddSeconds(10)), "Same-island transit preserves occurrence and hold without reflagging");
        Scan(11, [Row() with { Running = false }]); Tick(11);
        check(!tracker.OwnsNavigation(time.AddSeconds(11)), "Finished FATE releases route navigation");
        Scan(12, [Row(start: 12)]); Tick(12);
        check(flags.Count == 2, "Same FATE ID with a new start can flag again");
        Scan(13, [Row(start: 12)], instance: 2); Tick(13);
        check(flags.Count == 3, "Different instance establishes a separate occurrence");

        tracker.Reset(); flags.Clear();
        Scan(0, [Row(1976), Row(1963, -20)]); Tick(0);
        check(flags.SequenceEqual(new ushort[] { 1976 }), "Newest start wins over older ordinary FATE, including pot FATEs");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row(1976), Row(1963) with { Position = Vector3.One }]); Tick(0);
        check(flags.SequenceEqual(new ushort[] { 1963 }), "Simultaneous FATEs select nearest, never cycle through every event");
        Scan(1, [Row(1976), Row(1963) with { Position = Vector3.One }]); Tick(1);
        check(flags.Count == 1, "Unselected simultaneous FATE does not replay on next scan");

        tracker.Reset(); flags.Clear();
        Scan(0, [Row()], false); Tick(0);
        Scan(1, [Row()]); Tick(1);
        check(flags.Count == 0, "Enabling auto flag does not replay events observed while disabled");
        Scan(2, [Row(start: 2)]); Tick(2, occupied: true);
        check(flags.Count == 0, "Casting or interaction defers the new flag");
        Scan(3, [Row(start: 2)]); Tick(3, pot: true);
        check(flags.Count == 0, "Pot treasure navigation takes priority over FATE");
        Scan(4, [Row(start: 2)]); Tick(4);
        check(flags.Count == 1, "Deferred FATE flags after pot search while still active");
        tracker.Release(); Scan(5, [Row(start: 2)]); Tick(5);
        check(flags.Count == 1 && !tracker.OwnsNavigation(time.AddSeconds(5)), "Manual release prevents replay of same occurrence");
        Scan(6, [Row(1976, 6)]); Tick(6, pot: true);
        Scan(7, []); Tick(7);
        check(flags.Count == 1 && !tracker.OwnsNavigation(time.AddSeconds(7)), "Disappeared deferred event cannot insert a stale flag");

        tracker.Reset(); flags.Clear();
        Scan(0, [Row()]); Tick(0, success: false); Tick(1, success: false);
        check(flags.Count == 1, "Failed flags are throttled for two seconds");
        Scan(2, [Row()]); Tick(2, success: false); Scan(4, [Row()]); Tick(4, success: false);
        Scan(6, [Row()]); Tick(6);
        check(flags.Count == 3 && !tracker.OwnsNavigation(time.AddSeconds(6)), "Three failed attempts stop and release navigation");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row() with { Position = new(float.NaN, 0, 0) }]); Tick(0);
        check(flags.Count == 0, "Invalid coordinates cannot flag");
        Scan(1, [Row()]); Tick(1);
        check(flags.Count == 1, "Coordinates becoming valid can complete pending flag");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row() with { Progress = 100 }, Row(1976) with { Running = false }, Row(1977, -1000)]); Tick(0);
        check(flags.Count == 0 && tracker.Active(time).Count == 0, "Finished, 100 percent and expired FATEs never flag");
        Scan(1, [Row() with { Territory = 1346 }]); Tick(1);
        check(flags.Count == 0, "Wrong-territory rows excluded");
        Scan(2, [Row()], territory: 100); Tick(2);
        check(flags.Count == 0, "Outside Crescent no auto flag");

        tracker.Reset(); flags.Clear();
        Scan(0, [Row() with { StartTimeEpoch = 0, Preparing = true }]); Tick(0);
        Scan(1, [Row()]); Tick(1);
        check(flags.Count == 1, "Preparation-to-running and timestamp correction remain one occurrence");
        Scan(2, [Row(1976, 2)]);
        tracker.HoldManual(tracker.Active(time.AddSeconds(2)).Single(), time.AddSeconds(2)); Tick(2);
        check(flags.Count == 1 && tracker.OwnsNavigation(time.AddSeconds(2)), "Manual flag cancels a queued auto flag while retaining navigation");
        tracker.Tick(time.AddSeconds(3), false, false, false, _ => throw new Exception());
        check(!tracker.OwnsNavigation(time.AddSeconds(3)), "Disabling auto flags releases pending and held navigation");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row()]); Tick(4);
        check(flags.Count == 0 && !tracker.OwnsNavigation(time.AddSeconds(4)), "Stale event table does not flag or hold navigation");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row()]); Tick(0);
        Scan(1, [Row(), Row(1976, 1)]); Tick(1);
        check(flags.SequenceEqual(new ushort[] { 1963, 1976 }), "A later new event replaces an existing FATE flag once");
        Tick(1, pot: true);
        check(!tracker.OwnsNavigation(time.AddSeconds(1)), "Starting pot treasure navigation relinquishes an already flagged FATE");
        Scan(2, [Row(), Row(1976, 1)]); Tick(2);
        check(flags.Count == 2, "Ending pot navigation does not replay a FATE already flagged before the search");
        tracker.Reset(); flags.Clear();
        Scan(0, [Row(2072) with { Territory = 1346 }], territory: 1346); Tick(0);
        check(flags.SequenceEqual(new ushort[] { 2072 }), "General FATE flagging supports the current northern zone without southern coordinates");
        tracker.Reset();
        Scan(0, [Row()]);
        tracker.Tick(time, true, false, false, _ => throw new InvalidOperationException());
        check(tracker.Detail.Contains("稍後重試"), "Map API exception is contained and becomes a bounded retry");
    }
}
