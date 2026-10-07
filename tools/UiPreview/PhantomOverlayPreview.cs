using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;

internal static class PhantomOverlayPreview
{
    internal static void Run(string output, IReadOnlyDictionary<ulong, Texture> textures)
    {
        foreach (var scenario in new[] { "ready", "all-icons", "freelancer", "current", "combat", "occupied", "dead", "loading", "pending",
            "hidden", "close", "drag", "button-drag", "header-click", "hide-drag", "restore", "scaled", "narrow", "missing-icons", "blocked-after-press", "confirmed" })
        {
            var overlay = new PhantomJobOverlay();
            var io = ImGui.GetIO();
            io.FontGlobalScale = scenario == "scaled" ? 1.5f : 1;
            var width = scenario == "narrow" ? 230 : 620;
            var height = 430;
            io.DisplaySize = new(width, height);
            io.AddMouseButtonEvent(0, false); io.AddMousePosEvent(-1000, -1000);
            var visible = scenario != "hidden";
            var saved = scenario == "restore" ? new Vector2(9999) : new Vector2(24, 62);
            var saves = 0; var closes = 0; var icons = 0;
            List<byte> calls = [];
            var blocked = scenario is "combat" or "occupied" or "dead" or "loading" or "pending";
            var state = new CompassPhantomJobs(scenario == "loading" ? null : scenario == "freelancer" ? (byte)3 : (byte)0, !blocked,
                scenario switch
                {
                    "combat" => "戰鬥中無法切換幻影職業。",
                    "occupied" => "互動、讀條或移動過場中，請稍後切換。",
                    "dead" => "倒地時無法切換幻影職業。",
                    "loading" => "請進入新月島，等待職業資料載入。",
                    "pending" => "已送出切換輔助騎士的請求，等待遊戲確認。",
                    _ => "已切換為輔助自由人。",
                });
            byte[] clicks = scenario == "all-icons" ? PhantomJobs.All.Select(j => j.Id).ToArray() :
                scenario is "freelancer" or "current" ? [0] : blocked || scenario is "blocked-after-press" or "confirmed" ? [1] : [];
            var gesture = scenario is "drag" or "button-drag" or "header-click" or "hide-drag" or "close";
            var frames = clicks.Length > 0 ? 3 + clicks.Length * 4 : gesture ? 9 : 5;
            for (var frame = 0; frame < frames; frame++)
            {
                if (clicks.Length > 0 && frame >= 3)
                {
                    var index = (frame - 3) / 4; var step = (frame - 3) % 4;
                    if (step == 0) { var target = overlay.ButtonTargets[clicks[index]]; io.AddMousePosEvent(target.X, target.Y); }
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                }
                if (scenario == "blocked-after-press" && frame == 5) state = state with { CanSwitch = false, Detail = "戰鬥中無法切換幻影職業。" };
                if (scenario == "confirmed" && frame == 6) state = state with { CurrentJob = 1, Detail = "已切換為輔助騎士。" };
                if (gesture && frame >= 3)
                {
                    var target = scenario == "button-drag" ? overlay.ButtonTargets[1] : scenario == "close" ? overlay.CloseTarget : overlay.DragTarget;
                    if (frame == 3) io.AddMousePosEvent(target.X, target.Y);
                    if (frame == 4) io.AddMouseButtonEvent(0, true);
                    if (frame == 5 && scenario is not ("header-click" or "close")) io.AddMousePosEvent(target.X + 80, target.Y + 40);
                    if (frame == 6) io.AddMouseButtonEvent(0, false);
                }
                if (scenario == "hide-drag" && frame >= 5) visible = false;
                ImGui.NewFrame();
                ImGui.GetBackgroundDrawList().AddRectFilled(Vector2.Zero, io.DisplaySize, ImGui.ColorConvertFloat4ToU32(new(0.055f, 0.085f, 0.12f, 1)));
                icons = 0;
                overlay.Draw(state, visible, saved, value => { saves++; saved = value; }, () => { closes++; visible = false; }, id => calls.Add(id),
                    (id, size) =>
                    {
                        if (scenario == "missing-icons" || !textures.ContainsKey(id)) return false;
                        ImGui.Image(new ImTextureID(id), size); icons++; return true;
                    });
                ImGui.Render();
            }
            byte[] expected = scenario == "all-icons" ? PhantomJobs.All.Where(j => j.Id != 0).Select(j => j.Id).ToArray() :
                scenario == "freelancer" ? [0] : scenario == "confirmed" ? [1] : [];
            if (!calls.SequenceEqual(expected)) throw new InvalidOperationException($"Unexpected phantom switches in {scenario}: {string.Join(',', calls)}");
            if (overlay.Drawn != visible || overlay.ButtonTargets.Count != (visible ? 13 : 0))
                throw new InvalidOperationException($"Overlay visibility must be independent of the main window: {scenario}");
            if (scenario == "drag" ? saves != 1 || Vector2.Distance(saved, new(104, 102)) > 1 : saves != 0)
                throw new InvalidOperationException($"Only a completed header drag should save once in {scenario}: {saved}, saves={saves}");
            if (closes != (scenario == "close" ? 1 : 0)) throw new InvalidOperationException("Closing persists visibility off exactly once.");
            if (visible && (overlay.Position.X < 0 || overlay.Position.Y < 0 || overlay.Position.X + overlay.Size.X > width + 1 || overlay.Position.Y + overlay.Size.Y > height + 1))
                throw new InvalidOperationException($"Phantom overlay must fit on the viewport: {scenario}");
            foreach (var point in overlay.ButtonTargets.Values)
                if (point.X - 22 * io.FontGlobalScale < overlay.Position.X || point.X + 22 * io.FontGlobalScale > overlay.Position.X + overlay.Size.X ||
                    point.Y - 22 * io.FontGlobalScale < overlay.Position.Y || point.Y + 22 * io.FontGlobalScale > overlay.Position.Y + overlay.Size.Y)
                    throw new InvalidOperationException($"Icons must fit inside the overlay: {scenario}");
            if (visible && scenario != "missing-icons" && PhantomJobs.All.All(j => textures.ContainsKey(j.IconId)) && icons != 13)
                throw new InvalidOperationException("All available game job textures must render.");
            var path = Path.Combine(output, $"phantom-overlay-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, path);
            Console.WriteLine($"Rendered phantom-overlay-{scenario}: {path}");
        }
    }
}
