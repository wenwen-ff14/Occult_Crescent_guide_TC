using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;

internal static class PotOverlayPreview
{
    internal static void Run(string output, IReadOnlyDictionary<ulong, Texture> textures)
    {
        var now = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.FromHours(8));
        var north = PotFateTracker.Definitions[0]; var south = PotFateTracker.Definitions[1];
        var waiting = new PotFateSnapshot([], north, now.AddSeconds(522), true, true);
        var active = new PotFateSnapshot([new(north, north.Name, north.Location, 35, false, now.AddMinutes(7))], south, now.AddMinutes(17), true, true);
        foreach (var scenario in new[] { "countdown", "active", "unknown", "overdue", "paused", "hidden", "interactive", "drag", "restore", "scaled",
            "flags", "flags-south", "flags-disabled", "flags-north-island", "flags-south-north-island", "flags-unknown", "flags-no-time",
            "flags-overdue", "flags-paused", "flags-switch", "flags-reset", "flags-shared", "shared", "button-drag", "header-click", "hide-drag" })
        {
            var overlay = new PotCountdownOverlay();
            var io = ImGui.GetIO();
            io.FontGlobalScale = scenario == "scaled" ? 1.5f : 1;
            var width = scenario == "scaled" ? 930 : 620;
            var height = scenario == "scaled" ? 450 : 350;
            io.DisplaySize = new(width, height);
            io.AddMouseButtonEvent(0, false); io.AddMousePosEvent(-1000, -1000);
            var saved = scenario == "restore" ? new Vector2(9999) : new Vector2(24, 82);
            var saves = 0;
            List<ushort> calls = [];
            var snapshot = scenario switch
            {
                "active" or "scaled" or "flags-south" => active,
                "unknown" or "flags-unknown" => new([], null, null, false, true),
                "overdue" or "flags-overdue" => waiting with { ExpectedAt = now.AddSeconds(-10) },
                "paused" or "flags-paused" => active with { ScanFresh = false },
                "flags-north-island" => waiting with { Next = PotFateTracker.Definitions[2] },
                "flags-south-north-island" => waiting with { Next = PotFateTracker.Definitions[3] },
                "flags-no-time" => waiting with { ExpectedAt = null },
                "shared" or "flags-shared" => waiting with { IsSharedEstimate = true },
                _ => waiting,
            };
            var clickingFlags = scenario.StartsWith("flags");
            var predictionChanges = scenario is "flags-switch" or "flags-reset";
            var dragging = scenario is "drag" or "button-drag" or "hide-drag" or "header-click";
            for (var frame = 0; frame < (predictionChanges ? 11 : clickingFlags ? 7 : dragging ? 9 : 5); frame++)
            {
                if (scenario == "interactive") io.AddMousePosEvent(70, 115);
                if (predictionChanges && frame == 5)
                    snapshot = scenario == "flags-switch" ? waiting with { Next = south } : new([], null, null, false, true);
                if (clickingFlags && frame >= 3)
                {
                    var step = (frame - 3) % 4;
                    if (step == 0) { var target = overlay.FlagTarget; io.AddMousePosEvent(target.X, target.Y); }
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                }
                if (dragging)
                {
                    var target = scenario == "button-drag" ? overlay.FlagTarget : overlay.DragTarget;
                    if (frame == 3) io.AddMousePosEvent(target.X, target.Y);
                    if (frame == 4) io.AddMouseButtonEvent(0, true);
                    if (frame == 5 && scenario != "header-click") io.AddMousePosEvent(target.X + 80, target.Y + 40);
                    if (frame == 6) io.AddMouseButtonEvent(0, false);
                }
                ImGui.NewFrame();
                var background = ImGui.GetBackgroundDrawList();
                background.AddRectFilled(Vector2.Zero, io.DisplaySize, ImGui.ColorConvertFloat4ToU32(new(0.055f, 0.085f, 0.12f, 1)));
                background.AddText(new(24, 24), ImGui.ColorConvertFloat4ToU32(new(0.70f, 0.78f, 0.85f, 1)), "魔法罐浮窗預覽 · 主介面已關閉（示範資料）");
                overlay.Draw(snapshot, now, scenario != "hidden" && !(scenario == "hide-drag" && frame >= 5),
                    saved, value => { saved = value; saves++; }, scenario != "flags-disabled", id => calls.Add(id));
                ImGui.Render();
            }
            if (overlay.Drawn != (scenario is not ("hidden" or "hide-drag"))) throw new InvalidOperationException("Overlay visibility must not depend on the main window.");
            if (scenario == "interactive" && !io.WantCaptureMouse) throw new InvalidOperationException("Pot overlay is interactive without an unlock mode.");
            if (scenario == "drag" && (saves != 1 || Vector2.Distance(saved, new(104, 122)) > 1))
                throw new InvalidOperationException($"Default overlay drag saves exactly once: {saved}, saves={saves}");
            if (scenario != "drag" && saves != 0) throw new InvalidOperationException("Clicks, disabled buttons and interrupted drags must not save an overlay position.");
            var canFlag = scenario != "flags-disabled" && snapshot.Next is not null && snapshot.ExpectedAt is not null;
            if (overlay.FlagEnabled != (overlay.Drawn && canFlag))
                throw new InvalidOperationException($"Next-pot flag requires both a prediction and an available location in {scenario}.");
            ushort[] expected = clickingFlags && canFlag ? [snapshot.Next!.Id] : [];
            if (!calls.SequenceEqual(expected)) throw new InvalidOperationException($"Unexpected pot flags in {scenario}: {string.Join(',', calls)}");
            if (scenario == "restore" && (overlay.Position.X + overlay.Size.X > width || overlay.Position.Y + overlay.Size.Y > height))
                throw new InvalidOperationException("Saved overlay position must be clamped onto the current screen.");
            if (overlay.Drawn && (overlay.FlagTarget.Y < overlay.Position.Y || overlay.FlagTarget.Y + 15 * io.FontGlobalScale > overlay.Position.Y + overlay.Size.Y))
                throw new InvalidOperationException("Pot flag buttons must fit inside the overlay at every font scale.");
            var path = Path.Combine(output, $"pot-overlay-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, path);
            Console.WriteLine($"Rendered pot-overlay-{scenario}: {path}");
        }
    }
}
