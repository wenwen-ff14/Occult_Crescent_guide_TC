using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;

internal static class PatrolOverlayPreview
{
    internal static void Run(string output, IReadOnlyDictionary<ulong, Texture> textures)
    {
        var running = new CompassPatrolOverlayState("巡查中", "下一站 #24 · 銀寶箱", "X 27.0 / Y 18.2 · 直線 152 m",
            8, 3, 57, 68, "靠近下一站後確認寶箱；空點判定中會保留目前站點。", false);
        foreach (var scenario in new[] { "running", "paused", "pot-priority", "fate-priority", "planning", "planning-empty", "finished", "idle", "empty-route", "hidden", "locked", "drag", "restore", "scaled", "both", "controls-locked", "controls-unlocked", "last-station", "occupied", "paused-planning" })
        {
            var overlay = new PatrolOverlay(); var potOverlay = new PotCountdownOverlay();
            var io = ImGui.GetIO(); io.FontGlobalScale = scenario == "scaled" ? 1.5f : 1;
            var width = scenario == "scaled" ? 960 : 640;
            var height = scenario == "scaled" ? 600 : scenario == "both" ? 540 : 400;
            io.DisplaySize = new(width, height);
            io.AddMouseButtonEvent(0, false); io.AddMousePosEvent(-1000, -1000);
            var saved = scenario == "restore" ? new Vector2(9999) : scenario == "both" ? new Vector2(24, 260) : new Vector2(24, 82);
            var saves = 0;
            var state = scenario switch
            {
                "paused" => running with { Status = "已暫停", Target = "保留站點 #24 · 銀寶箱", Waiting = true, Paused = true, Detail = "使用者已暫停巡查；魔法罐與 FATE 使用獨立開關。" },
                "pot-priority" => running with { Status = "魔法罐優先", Waiting = true, Detail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" },
                "fate-priority" => running with { Status = "FATE 優先", Waiting = true, Detail = "FATE 標點中，巡查保留；可在 FATE 選單解除並接續。" },
                "planning" => running with { Status = "規劃路線中", Waiting = true, Planning = true, Detail = "地形路線計算中，暫停巡查判定。" },
                "planning-empty" or "paused-planning" => running with { Status = "等待路線資料", Waiting = true, Planning = true, Paused = scenario == "paused-planning", Remaining = 0, Total = 0, Completed = 0, Skipped = 0 },
                "finished" => running with { Status = "本輪巡查結束", Target = "目前沒有下一站", Coordinates = "在巡查頁選擇起點或規劃路線。", Completed = 60, Skipped = 8, Remaining = 0, Detail = "可在巡查頁開始下一輪；巡查紀錄保留。" },
                "idle" => running with { Status = "尚未開始", Target = "目前沒有下一站", Coordinates = "在巡查頁選擇起點或規劃路線。", Completed = 0, Skipped = 0, Remaining = 0, Total = 0, Detail = "啟動巡查後自動更新；關閉主介面也會顯示。" },
                "empty-route" => running with { Remaining = 0 },
                "last-station" => running with { Completed = 64, Remaining = 1 },
                "occupied" => running with { Occupied = true },
                _ => running,
            };
            List<string> calls = [];
            var commands = new CompassPatrolOverlayCommands(
                () => { calls.Add("next"); state = state with { Completed = state.Completed + 1, Remaining = state.Remaining - 1 }; },
                () => { calls.Add("pause"); state = state with { Paused = true, Waiting = true, Status = "已暫停" }; },
                () => { calls.Add("resume"); state = state with { Paused = false, Waiting = state.Planning, Status = "巡查中" }; },
                () => { calls.Add("stop"); state = state with { Remaining = 0, Total = 0, Planning = false, Paused = false }; });
            string[] clicks = scenario switch
            {
                "controls-locked" or "controls-unlocked" => ["next", "pause", "next", "pause", "stop"],
                "last-station" or "occupied" or "pot-priority" or "fate-priority" or "paused" or "planning" => ["next"],
                "paused-planning" => ["pause", "stop"],
                _ => [],
            };
            var locked = scenario is not ("drag" or "idle" or "controls-unlocked");
            for (var frame = 0; frame < (clicks.Length > 0 ? 3 + clicks.Length * 4 : scenario == "drag" ? 9 : 5); frame++)
            {
                if (scenario == "locked") io.AddMousePosEvent(70, 115);
                if (clicks.Length > 0 && frame >= 3)
                {
                    var index = (frame - 3) / 4; var step = (frame - 3) % 4;
                    if (step == 0) { var target = overlay.ButtonTargets[clicks[index]]; io.AddMousePosEvent(target.X, target.Y); }
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                }
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
                background.AddText(new(24, 24), ImGui.ColorConvertFloat4ToU32(new(0.70f, 0.78f, 0.85f, 1)), "巡查浮窗預覽 · 主介面已關閉（示範資料）");
                if (scenario == "both")
                {
                    var now = DateTimeOffset.UtcNow;
                    potOverlay.Draw(new([], PotFateTracker.Definitions[0], now.AddSeconds(522), true, true), now, true, true, new(24, 82), _ => { });
                }
                overlay.Draw(state, scenario != "hidden", locked, saved, value => { saved = value; saves++; }, commands);
                ImGui.Render();
            }
            var shouldShow = scenario is not ("hidden" or "finished" or "idle" or "empty-route" or "controls-locked" or "controls-unlocked" or "last-station" or "paused-planning");
            if (overlay.Drawn != shouldShow) throw new InvalidOperationException($"Patrol overlay visibility is wrong after {scenario}.");
            string[] expected = scenario switch
            {
                "controls-locked" or "controls-unlocked" => ["next", "pause", "resume", "stop"],
                "last-station" => ["next"],
                "paused-planning" => ["resume", "stop"],
                _ => [],
            };
            if (!calls.SequenceEqual(expected)) throw new InvalidOperationException($"Patrol controls dispatched unexpected actions in {scenario}: {string.Join(",", calls)}");
            if (clicks.Length > 0 && saves != 0) throw new InvalidOperationException("Clicking patrol controls must not move the overlay.");
            if (scenario == "drag" && (saves != 1 || Vector2.Distance(saved, new(104, 122)) > 1))
                throw new InvalidOperationException($"Patrol drag must save once: {saved}, saves={saves}");
            if (scenario == "restore" && (overlay.Position.X + overlay.Size.X > width || overlay.Position.Y + overlay.Size.Y > height))
                throw new InvalidOperationException("Patrol overlay must stay within the screen.");
            var path = Path.Combine(output, $"patrol-overlay-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, path);
            Console.WriteLine($"Rendered patrol-overlay-{scenario}: {path}");
        }
    }
}
