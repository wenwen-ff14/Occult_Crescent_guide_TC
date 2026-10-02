using System.Numerics;
using CrescentCompass.Core;

internal static class WaymarkGroundChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Reproduce a regular ground mesh hit: the triangle is valid, Normal is
        // zero. Version 0.9.1 rejected this before reaching PlaceFieldMarker.
        var marker = new SavedWaymark(10.123f, 75, -20.456f, true);
        var p = marker.Position;
        var flat = new WaymarkGroundHit(p, p, p + new Vector3(0, 0, 2), p + new Vector3(3, 0, 0), Vector3.Zero);
        var snapped = WaymarkGround.Snap(marker, flat);
        check(snapped == marker, "Normal-less mesh hit accepts valid floor triangle, preserving saved XYZ");
        check(WaymarkGround.Snap(marker, flat with { Normal = new(float.NaN) }) == marker, "Triangle is authoritative when optional normal is unfilled");
        var adjusted = WaymarkGround.Snap(marker, flat with { Point = p + new Vector3(.0001f, .1254f, -.0001f) });
        check(adjusted.X == marker.X && adjusted.Z == marker.Z && adjusted.Y == 75.125f, "Ground snap rounds only height and never drifts horizontal coordinates");
        check(WaymarkGround.Snap(marker, new(p, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.UnitY * 5)) == marker,
            "Primitive collider without triangle uses normalized native normal");
        check(WaymarkGround.Snap(marker, flat with { V3 = p + new Vector3(1, 1, 0) }) == marker, "Walkable slope passes after normalization");
        void Reject(WaymarkGroundHit hit, string label)
        {
            bool failed = false;
            try { WaymarkGround.Snap(marker, hit); } catch (InvalidOperationException) { failed = true; }
            check(failed, label);
        }
        Reject(flat with { V3 = p + new Vector3(1, 2, 0) }, "Steep slope remains rejected");
        Reject(flat with { V2 = flat.V3, V3 = flat.V2, Normal = Vector3.UnitY }, "Ceiling/underside cannot be accepted through absolute normal or fallback");
        Reject(new(p, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero), "Missing triangle and normal cannot fabricate a floor");
        Reject(new(p, Vector3.Zero, Vector3.Zero, Vector3.Zero, new(float.NaN)), "Degenerate and nonfinite ground rejected");
        Reject(flat with { Point = p + Vector3.UnitY * 2 }, "Wrong floor height rejected");
        Reject(flat with { Point = new(float.NaN) }, "Nonfinite hit point rejected");
        Reject(flat with { V2 = flat.V1, V3 = flat.V1 }, "Zero-area triangle rejected");

        var slots = Enumerable.Repeat(SavedWaymark.Off, 8).ToArray(); slots[0] = marker;
        var saved = new WaymarkPreset(Guid.NewGuid(), "Normal-less mesh regression", 1252, DateTimeOffset.UtcNow, slots);
        var ready = new WaymarkContext(1252, 1, 123, true, false);
        var current = slots.ToArray();
        int casts = 0, nativeCalls = 0;
        SavedWaymark Cast(int i, SavedWaymark mark) { casts++; return WaymarkGround.Snap(mark, flat); }
        var same = WaymarkPlacement.Prepare(saved, current, (_, _) => throw new Exception("Same preset must not raycast"));
        var placement = new WaymarkPlacement(); placement.Start(same, current, ready, 0);
        check(!placement.Active && placement.Detail.Contains("已在儲存位置"), "Save then place without moving reports an explicit no-op without ground checks");

        // Save -> clear live A -> place -> read back: the original regression.
        current[0] = SavedWaymark.Off;
        var prepared = WaymarkPlacement.Prepare(saved, current, Cast);
        placement.Start(prepared, current, ready, 0);
        placement.Tick(ready, current, 0, op => { nativeCalls++; current[op.Index] = op.Marker; return 0; });
        placement.Tick(ready, current, 1, _ => throw new Exception("Unexpected duplicate native request"));
        check(casts == 1 && nativeCalls == 1 && current[0] == marker && !placement.Active && !placement.Failed,
            "Saved mesh marker cleared in game is preflighted, placed and observed exactly once");
        current[0] = marker with { X = marker.X + 15 };
        casts = 0;
        prepared = WaymarkPlacement.Prepare(saved, current, Cast);
        check(casts == 1 && prepared.Markers[0] == marker && saved.Markers[0] == marker, "Moving live marker preflights original saved location without changing preset");
        current[0] = marker; current[1] = new(12, 75, -20, true);
        prepared = WaymarkPlacement.Prepare(saved, current, (_, _) => throw new Exception("Clear must not raycast"));
        placement.Start(prepared, current, ready, 1000);
        placement.Tick(ready, current, 1000, op => { nativeCalls++; current[op.Index] = op.Marker; return 0; });
        placement.Tick(ready, current, 1001, _ => throw new Exception("Unexpected duplicate clear"));
        check(!current[1].Active && !placement.Active && nativeCalls == 2, "Unchanged active marker does not block clearing a slot absent in preset");

        var twoSlots = slots.ToArray(); twoSlots[1] = marker with { X = marker.X + 1 };
        var two = saved with { Markers = twoSlots };
        var empty = Enumerable.Repeat(SavedWaymark.Off, 8).ToArray();
        casts = 0; bool rejected = false;
        try { WaymarkPlacement.Prepare(two, empty, (i, mark) => { casts++; if (i == 1) throw new InvalidOperationException("Unloaded terrain"); return mark; }); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && casts == 2 && !placement.Active && nativeCalls == 2 && two.Markers.SequenceEqual(twoSlots),
            "A later preflight failure cannot start partial placement or alter saved data");
        placement.Start(saved, empty, ready, 2000); placement.Tick(ready, empty, 2000, _ => 4);
        check(placement.Failed && !placement.Active, "Native rejection exposes a failure status for visible UI feedback");
        placement.Start(saved, empty, ready, 2100);
        check(!placement.Failed, "Retry clears previous failure state");
        placement.Cancel(); check(!placement.Failed, "User cancellation is not reported as a native failure");
    }
}
