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
        foreach (var scenario in new[] { "countdown", "active", "unknown", "overdue", "paused", "hidden", "locked", "drag", "restore", "scaled" })
        {
            var overlay = new PotCountdownOverlay();
            var io = ImGui.GetIO();
            io.FontGlobalScale = scenario == "scaled" ? 1.5f : 1;
            var width = scenario == "scaled" ? 930 : 620;
            var height = scenario == "scaled" ? 450 : 300;
            io.DisplaySize = new(width, height);
            io.AddMouseButtonEvent(0, false); io.AddMousePosEvent(-1000, -1000);
            var saved = scenario == "restore" ? new Vector2(9999) : new Vector2(24, 82);
            var saves = 0;
            var snapshot = scenario switch
            {
                "active" or "scaled" => active,
                "unknown" => new([], null, null, false, true),
                "overdue" => waiting with { ExpectedAt = now.AddSeconds(-10) },
                "paused" => active with { ScanFresh = false },
                _ => waiting,
            };
            for (var frame = 0; frame < (scenario == "drag" ? 9 : 5); frame++)
            {
                if (scenario == "locked") io.AddMousePosEvent(70, 115);
                if (scenario == "drag")
                {
                    if (frame == 3) io.AddMousePosEvent(overlay.DragTarget.X, overlay.DragTarget.Y);
                    if (frame == 4) io.AddMouseButtonEvent(0, true);
                    if (frame == 5) io.AddMousePosEvent(overlay.DragTarget.X + 80, overlay.DragTarget.Y + 40);
                    if (frame == 6) io.AddMouseButtonEvent(0, false);
                }
                ImGui.NewFrame();
                var background = ImGui.GetBackgroundDrawList();
                background.AddRectFilled(Vector2.Zero, io.DisplaySize, ImGui.ColorConvertFloat4ToU32(new(0.055f, 0.085f, 0.12f, 1)));
                background.AddText(new(24, 24), ImGui.ColorConvertFloat4ToU32(new(0.70f, 0.78f, 0.85f, 1)), "魔法罐浮窗預覽 · 主介面已關閉（示範資料）");
                overlay.Draw(snapshot, now, scenario != "hidden", scenario != "drag", saved, value => { saved = value; saves++; });
                ImGui.Render();
            }
            if (overlay.Drawn != (scenario != "hidden")) throw new InvalidOperationException("Overlay visibility must not depend on the main window.");
            if (scenario == "locked" && io.WantCaptureMouse) throw new InvalidOperationException("Locked pot overlay must let mouse input pass through.");
            if (scenario == "drag" && (saves != 1 || Vector2.Distance(saved, new(104, 122)) > 1))
                throw new InvalidOperationException($"Overlay drag must save its position exactly once: {saved}, saves={saves}");
            if (scenario == "restore" && (overlay.Position.X + 330 > width || overlay.Position.Y + 108 > height))
                throw new InvalidOperationException("Saved overlay position must be clamped onto the current screen.");
            var path = Path.Combine(output, $"pot-overlay-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, path);
            Console.WriteLine($"Rendered pot-overlay-{scenario}: {path}");
        }
    }
}
