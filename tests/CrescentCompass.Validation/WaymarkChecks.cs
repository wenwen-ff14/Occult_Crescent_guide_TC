using System.Numerics;
using System.Text.Json;
using CrescentCompass.Core;

internal static class WaymarkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string json = """{"Name":"塔內・一樓","MapID":1018,"A":{"X":-530.125,"Y":-840.75,"Z":210.123,"ID":0,"Active":true},"Four":{"X":-528,"Y":-840.75,"Z":215,"ID":7,"Active":true}}""";
        var preset = WaymarkPreset.Import(json);
        check(preset.Territory == 1252 && preset.Name == "塔內・一樓" && preset.Markers.Length == 8, "Waymark content ID maps to TC South Horn territory");
        check(preset.Markers[0].Position == new Vector3(-530.125f, -840.75f, 210.123f) && preset.Markers[7].Active && !preset.Markers[1].Active,
            "Waymark world coordinates preserve negative height, decimals and absent slots");
        var roundtrip = WaymarkPreset.Import(preset.Export());
        check(roundtrip.Markers.SequenceEqual(preset.Markers) && roundtrip.Name == preset.Name && roundtrip.Id != preset.Id, "Public JSON export round trips without units/axis swaps");
        using (var parsed = JsonDocument.Parse(preset.Export()))
            check(parsed.RootElement.GetProperty("MapID").GetInt32() == 1018 && parsed.RootElement.GetProperty("Four").GetProperty("ID").GetInt32() == 7, "Export uses duty ID and 0-based marker IDs");
        check(WaymarkPreset.Import("```json\n" + json + "\n```").Markers.SequenceEqual(preset.Markers), "Fenced JSON paste accepted");
        check(WaymarkPreset.Import(json.Replace("1018", "0"), true).Territory == 1252 &&
            WaymarkPreset.Import(json.Replace("\"MapID\":1018,", ""), true).Territory == 1252, "Unassigned imports require explicit map assignment");
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { rejected = true; }
            check(rejected, label);
        }
        foreach (int map in new[] { 0, 1252, 967, 1, -1 }) Reject(() => WaymarkPreset.Import(json.Replace("1018", map.ToString())), $"Reject unassigned/foreign MapID {map}");
        Reject(() => WaymarkPreset.Import(json.Replace("1018", "967"), true), "Explicit assignment cannot override a foreign map");
        foreach (string bad in new[] { "", "[]", "null", "{", new string('x', 65537), json.Replace("1018", "\"1018\""), json.Replace("1018", "1.1"),
            json.Replace("\"MapID\":1018", "\"MapID\":1018,\"mapid\":1018"), json.Replace("\"X\":-530.125", "\"X\":2,\"X\":3"),
            json.Replace("-530.125", "1e100"), json.Replace("-530.125", "10001"), json.Replace("\"X\":-530.125,", ""), json.Replace("true", "\"true\""),
            json.Replace("true", "false"), json.Replace("塔內・一樓", "invalid\\nname") })
            Reject(() => WaymarkPreset.Import(bad), "Malformed, ambiguous, empty or unsafe JSON rejected");
        Reject(() => WaymarkPreset.Validate(preset with { Markers = preset.Markers.Take(7).ToArray() }), "Missing saved slot rejected");
        Reject(() => WaymarkPreset.Validate(preset with { Id = Guid.Empty }), "Corrupt preset identity rejected");
        check(!WaymarkPlacement.WithinRange(preset.Markers[0], Vector3.Zero) && WaymarkPlacement.WithinRange(preset.Markers[0], preset.Markers[0].Position), "Placement range includes world height");
        check(WaymarkPlacement.WithinRange(new(200, 0, 0, true), Vector3.Zero) && !WaymarkPlacement.WithinRange(new(200.01f, 0, 0, true), Vector3.Zero), "200m boundary enforced");

        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CrescentWaymarkChecks-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "waymarks.json");
            var library = new WaymarkLibrary(file);
            check(library.Presets.Count == 0 && library.LoadError == "", "New library is empty and writable");
            var id = library.Add(preset);
            check(library.Add(roundtrip) == id && library.Presets.Count == 1, "Repeated identical import deduplicates");
            for (int i = 0; i < 55; i++) library.Add(preset with { Name = "預設 " + i });
            check(library.Presets.Count == 56 && library.Presets.Select(p => p.Id).Distinct().Count() == 56, "No 3/50-preset cap and duplicate GUIDs never overwrite");
            library.Rename(id, "南部・重命名");
            library = new WaymarkLibrary(file);
            check(library.Get(id).Name == "南部・重命名" && library.Get(id).SavedAt == preset.SavedAt, "Rename survives reload and preserves timestamp");
            library.Delete(id);
            check(new WaymarkLibrary(file).Presets.Count == 55 && new WaymarkLibrary(file + ".bak").Presets.Count == 56, "Delete persists selected record and keeps previous backup");
            Reject(() => library.Get(id), "Deleted preset cannot be placed from stale selection");
            Reject(() => library.Rename(id, "ghost"), "Rename cannot recreate a deleted preset");
            foreach (var invalid in new[] { "{", "{\"Version\":2,\"Presets\":[]}", "{\"Version\":1,\"Presets\":[null]}" })
            {
                File.WriteAllText(file, invalid);
                var corrupt = new WaymarkLibrary(file);
                check(corrupt.LoadError.Length > 0 && corrupt.Presets.Count == 0, "Corrupt library does not unload plugin");
                Reject(() => corrupt.Add(preset), "Corrupt library cannot be overwritten by save/import");
                check(File.ReadAllText(file) == invalid, "Original corrupt data preserved for recovery");
            }
            string occupied = Path.Combine(root, "occupied"); File.WriteAllText(occupied, "keep");
            var unavailable = new WaymarkLibrary(Path.Combine(occupied, "waymarks.json"));
            bool failed = false;
            try { unavailable.Add(preset); } catch (IOException) { failed = true; }
            check(failed && unavailable.Presets.Count == 0 && File.ReadAllText(occupied) == "keep", "Failed disk write cannot publish phantom in-memory preset");
        }
        finally
        {
            // Only the unique temporary directory constructed above is eligible.
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test path.");
            Directory.Delete(root, true);
        }

        var ready = new WaymarkContext(1252, 1, 123, true, false);
        SavedWaymark[] Empty() => Enumerable.Repeat(SavedWaymark.Off, 8).ToArray();
        var queue = new WaymarkPlacement();
        var live = Empty();
        var operations = new List<WaymarkOperation>();
        byte Apply(WaymarkOperation op) { operations.Add(op); live[op.Index] = op.Marker; return 0; }
        queue.Start(preset, live, ready, 0);
        Reject(() => queue.Start(preset, live, ready, 0), "Concurrent apply blocked");
        queue.Tick(ready, live, 0, Apply);
        check(queue.Active && operations.Count == 1, "Native success waits for local state observation");
        queue.Tick(ready, live, 1, Apply); queue.Tick(ready, live, 600, Apply);
        check(operations.Count == 1, "Marker calls spaced at least 600ms after observation");
        queue.Tick(ready, live, 601, Apply); queue.Tick(ready, live, 602, Apply);
        check(!queue.Active && operations.Select(op => op.Index).SequenceEqual(new[] { 0, 7 }), "Only changed slots applied in order");
        queue.Start(preset, live, ready, 700);
        check(!queue.Active, "Identical preset requires no native writes");
        live[3] = new(1, 2, 3, true);
        queue.Start(preset, live, ready, 800); queue.Tick(ready, live, 800, Apply); queue.Tick(ready, live, 801, Apply);
        check(!queue.Active && operations[^1].Index == 3 && !operations[^1].Marker.Active, "Only the chosen preset's inactive slot is cleared");
        foreach (var changed in new[] { ready with { InCombat = true }, ready with { Ready = false }, ready with { Territory = 1346 }, ready with { Instance = 2 }, ready with { Character = 999 } })
        {
            queue.Start(preset, Empty(), ready, 0); int count = operations.Count;
            queue.Tick(changed, [], 1, Apply);
            check(!queue.Active && operations.Count == count, "Combat, loading, zone, instance and character changes cancel before writes");
        }
        foreach (var invalid in new[] { ready with { InCombat = true }, ready with { Ready = false }, ready with { Territory = 1346 }, ready with { Character = 0 } })
            Reject(() => queue.Start(preset, Empty(), invalid, 0), "Invalid initial placement context rejected");
        queue.Start(preset, Empty(), ready, 0); queue.Tick(ready, Empty(), 0, _ => 0); queue.Tick(ready, Empty(), 5000, Apply);
        check(!queue.Active && queue.Detail.Contains("未確認"), "Unobserved native success times out instead of claiming completion");
        queue.Start(preset, Empty(), ready, 10000); queue.Tick(ready, Empty(), 10000, _ => 2);
        check(queue.Active, "Busy retry gets a fresh deadline after previous timeout");
        queue.Tick(ready, Empty(), 15000, _ => 3);
        check(!queue.Active, "Persistent native busy has bounded retries");
        live = Empty(); queue.Start(preset, live, ready, 20000); queue.Tick(ready, live, 20000, Apply); queue.Tick(ready, live, 20001, Apply);
        queue.Tick(ready, live, 27000, _ => 2);
        check(queue.Active, "Next slot busy retry does not inherit earlier observation deadline");
        queue.Tick(ready, live, 32000, _ => 2); check(!queue.Active, "Next slot busy deadline expires independently");
        queue.Start(preset, Empty(), ready, 0); queue.Tick(ready, Empty(), 0, _ => 4);
        check(!queue.Active && queue.Detail.Contains("代碼 4"), "Native rejection stops remaining marker mutations");
        queue.Start(preset, Empty(), ready, 0); queue.Cancel(); int before = operations.Count;
        queue.Tick(ready, Empty(), 1000, Apply); check(!queue.Active && before == operations.Count, "Cancel has no rollback or future writes");
    }
}
