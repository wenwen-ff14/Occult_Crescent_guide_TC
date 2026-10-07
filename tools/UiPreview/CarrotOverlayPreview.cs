using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;

internal static class CarrotOverlayPreview
{
    internal static void Run(string output, IReadOnlyDictionary<ulong, Texture> textures, IReadOnlyList<CompassPoint> points)
    {
        foreach (var scenario in new[] { "ready", "hidden", "close", "drag", "header-click", "hide-drag", "restore", "invalid-position",
            "scaled", "narrow", "narrow-scaled", "report", "stale-report", "flag", "reset", "hide-zero", "all-zero", "off-island", "live-update" })
        {
            var overlay = new CarrotOverlay(); var io = ImGui.GetIO();
            io.FontGlobalScale = scenario.Contains("scaled") ? 1.5f : 1;
            var width = scenario == "narrow" ? 320 : scenario == "narrow-scaled" ? 380 : 900;
            var height = scenario.Contains("narrow") ? 560 : 800;
            io.DisplaySize = new(width, height); io.AddMouseButtonEvent(0, false); io.AddMousePosEvent(-1000, -1000);
            var visible = scenario != "hidden";
            var saved = scenario == "restore" ? new Vector2(9999) : scenario == "invalid-position" ? new(float.NaN) : new(24, 62);
            var saves = 0; var closes = 0; var resets = 0;
            List<string> flagged = []; List<(string, int)> reported = [];
            var tablePoints = points.Select((p, i) => p with { CarrotWeight = scenario == "all-zero" || i < 4 ? 0 : i < 10 ? 1 : 2 }).ToArray();
            var state = new CarrotTableState(tablePoints, new(1, "", scenario != "off-island", 7), scenario != "off-island", points[10].Spot.Id);
            var actions = new CarrotTableActions(p => flagged.Add(p.Id), (id, revision) => reported.Add((id, revision)), () => resets++);
            string[] clicks = scenario switch { "report" => ["report-11", "confirm-yes"], "stale-report" => ["report-11", "confirm-yes", "cancel"], "flag" => ["flag-11"],
                "reset" => ["reset"], "hide-zero" or "all-zero" => ["hide-zero"], "off-island" => ["confirm", "reset", "flag-11"], _ => [] };
            var gesture = scenario is "drag" or "header-click" or "hide-drag" or "close";
            var frames = clicks.Length > 0 ? 3 + 4 * clicks.Length : gesture ? 9 : 5;
            for (var frame = 0; frame < frames; frame++)
            {
                if (clicks.Length > 0 && frame >= 3)
                {
                    var index = (frame - 3) / 4; var step = (frame - 3) % 4;
                    if (step == 0) { var target = overlay.Table.Targets[clicks[index]]; io.AddMousePosEvent(target.X, target.Y); }
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                }
                if (gesture && frame >= 3)
                {
                    var target = scenario == "close" ? overlay.CloseTarget : overlay.DragTarget;
                    if (frame == 3) io.AddMousePosEvent(target.X, target.Y);
                    if (frame == 4) io.AddMouseButtonEvent(0, true);
                    if (frame == 5 && scenario is not ("close" or "header-click")) io.AddMousePosEvent(target.X + 80, target.Y + 40);
                    if (frame == 6) io.AddMouseButtonEvent(0, false);
                }
                if (scenario == "hide-drag" && frame >= 5) visible = false;
                if (scenario == "stale-report" && frame == 7) state = state with { Progress = state.Progress with { Revision = 8 } };
                if (scenario == "live-update" && frame == 3) state = state with
                    { Points = tablePoints.Select(p => p with { CarrotWeight = 0 }).ToArray(), Progress = state.Progress with { Revision = 8, Pickups = 2 } };
                ImGui.NewFrame();
                ImGui.GetBackgroundDrawList().AddRectFilled(Vector2.Zero, io.DisplaySize, ImGui.ColorConvertFloat4ToU32(new(0.075f, 0.085f, 0.095f, 1)));
                overlay.Draw(state, actions, visible, saved, p => { saves++; saved = p; }, () => { closes++; visible = false; });
                ImGui.Render();
            }
            if (overlay.Drawn != visible) throw new InvalidOperationException($"Carrot overlay visibility failed: {scenario}");
            if (closes != (scenario == "close" ? 1 : 0)) throw new InvalidOperationException("Carrot close must persist off once.");
            if (scenario == "drag" ? saves != 1 || Vector2.Distance(saved, new(104, 102)) > 1 : saves != 0)
                throw new InvalidOperationException($"Only finished header drag saves position: {scenario}, {saved}, {saves}");
            if (visible && (overlay.Position.X < 0 || overlay.Position.Y < 0 || overlay.Position.X + overlay.Size.X > width + 1 || overlay.Position.Y + overlay.Size.Y > height + 1))
                throw new InvalidOperationException($"Carrot overlay exceeds viewport: {scenario}");
            if (scenario == "report" ? !reported.SequenceEqual(new[] { (points[10].Spot.Id, 7) }) : reported.Count != 0)
                throw new InvalidOperationException($"Carrot report callback failed: {scenario}");
            if (scenario == "flag" ? !flagged.SequenceEqual(new[] { points[10].Spot.Id }) : flagged.Count != 0)
                throw new InvalidOperationException($"Carrot flag callback failed: {scenario}");
            if (resets != (scenario == "reset" ? 1 : 0)) throw new InvalidOperationException("Reset must remain available only on-island.");
            var rows = scenario == "hide-zero" ? 21 : scenario == "all-zero" ? 0 : 25;
            if (visible && overlay.Table.RowsDrawn != rows) throw new InvalidOperationException($"Carrot filter must not delete data: {scenario}");
            if (visible)
                foreach (var key in new[] { "confirm", "number", "sort", "hide-zero", "reset" })
                {
                    var target = overlay.Table.Targets[key];
                    if (target.X < overlay.Position.X || target.X > overlay.Position.X + overlay.Size.X || target.Y < overlay.Position.Y || target.Y > overlay.Position.Y + overlay.Size.Y)
                        throw new InvalidOperationException($"Carrot control clipped: {scenario}, {key}, {target}");
                }
            var path = Path.Combine(output, $"carrot-overlay-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, path);
            Console.WriteLine($"Rendered carrot-overlay-{scenario}: {path}");
        }
    }
}
