using System.Numerics;
using CrescentCompass.Core;

internal static class WaymarkDistanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var far = new SavedWaymark(850.123f, -840.567f, -720.789f, true);
        var slots = Enumerable.Repeat(SavedWaymark.Off, 8).ToArray(); slots[0] = far;
        var preset = new WaymarkPreset(Guid.NewGuid(), "Distant saved coordinates", 1252, DateTimeOffset.UtcNow, slots);
        var empty = Enumerable.Repeat(SavedWaymark.Off, 8).ToArray();
        var ready = new WaymarkContext(1252, 1, 123, true, false);
        int groundCalls = 0;
        SavedWaymark NearbyOnly(int _, SavedWaymark point)
        {
            groundCalls++;
            if (!WaymarkPlacement.WithinRange(point, Vector3.Zero)) throw new InvalidOperationException("Outside streamed terrain");
            return point;
        }
        void Reject(Action action, string label)
        {
            bool failed = false;
            try { action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { failed = true; }
            check(failed, label);
        }
        Reject(() => WaymarkPlacement.Prepare(preset, empty, NearbyOnly), "Default mode still enforces local ground preflight");
        check(groundCalls == 1, "Default mode invokes the checked ground provider");
        var prepared = WaymarkPlacement.Prepare(preset, empty, NearbyOnly, useSavedCoordinates: true);
        check(groundCalls == 1 && prepared.Markers[0] == far, "Ignore distance skips terrain preflight and preserves complete saved XYZ");
        bool direct = true;
        var queue = new WaymarkPlacement(); queue.Start(prepared, empty, ready, 0, direct);
        direct = false; // Simulates a changed preference after the batch is built.
        SavedWaymark? placed = null;
        WaymarkOperation? captured = null;
        queue.Tick(ready, empty, 0, op =>
        {
            captured = op;
            placed = op.ResolvePoint(_ => throw new Exception("Distant apply must not query unstreamed terrain"));
            return 0;
        });
        check(!direct && captured?.UseSavedCoordinates == true && placed == far, "Queued operation snapshots direct mode and apply cannot reintroduce distance/ground checks");
        var observed = empty.ToArray(); observed[0] = far;
        queue.Tick(ready, observed, 1, _ => throw new Exception("Already requested"));
        check(!queue.Active && !queue.Failed, "Distant marker confirmation follows the normal queue lifecycle");
        Reject(() => new WaymarkOperation(0, far).ResolvePoint(p => NearbyOnly(0, p)), "Checked mode still validates again immediately before applying");
        var changedSlots = slots.ToArray(); changedSlots[0] = far with { X = float.NaN };
        Reject(() => WaymarkPlacement.Prepare(preset with { Markers = changedSlots }, empty, NearbyOnly, true), "Direct mode still rejects nonfinite coordinates");
        changedSlots[0] = far with { Y = 10001 };
        Reject(() => WaymarkPlacement.Prepare(preset with { Markers = changedSlots }, empty, NearbyOnly, true), "Direct mode retains coordinate bounds");
        Reject(() => WaymarkPlacement.Prepare(preset with { Territory = 1346 }, empty, NearbyOnly, true), "Direct mode cannot import or place a foreign-territory preset");
        Reject(() => WaymarkPlacement.Prepare(preset, [], NearbyOnly, true), "Direct mode still requires a valid live-marker snapshot");
        foreach (var invalid in new[] { ready with { InCombat = true }, ready with { Ready = false }, ready with { Territory = 1346 } })
            Reject(() => queue.Start(prepared, empty, invalid, 10, true), "Direct mode preserves combat, loading and territory guards");
        queue.Start(prepared, empty, ready, 10, true);
        int calls = 0;
        queue.Tick(ready with { Instance = 2 }, empty, 11, _ => { calls++; return 0; });
        check(!queue.Active && calls == 0, "Changing instances cancels distant placement before native calls");
        queue.Start(prepared, empty, ready, 20, true); queue.Tick(ready, empty, 20, _ => 5);
        check(!queue.Active && queue.Failed, "Native rejection is not bypassed by ignore-distance mode");
        queue.Start(prepared, empty, ready, 30, true); queue.Cancel();
        queue.Tick(ready, empty, 31, _ => { calls++; return 0; });
        check(calls == 0, "Cancel stops pending distant placement");
    }
}
