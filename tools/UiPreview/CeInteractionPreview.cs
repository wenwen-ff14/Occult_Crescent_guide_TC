using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;

internal static class CeInteractionPreview
{
    internal static void Run(string output, IReadOnlyDictionary<ulong, Texture> textures, CompassViewState example, CompassActions actions)
    {
        foreach (var scenario in new[] { "unseen", "unseen-empty", "unseen-off-island", "unseen-stale", "unseen-toggle", "unseen-reset",
            "drag-left", "drag-right", "drag-middle", "drag-icon", "drag-outside", "click-jitter" })
        {
            var state = example;
            var view = new CompassView { Page = CompassPage.Ce };
            var io = ImGui.GetIO(); io.FontGlobalScale = 1; io.DisplaySize = new(960, 1350);
            for (var button = 0; button < 3; button++) io.AddMouseButtonEvent(button, false);
            io.AddKeyEvent(ImGuiKey.ModCtrl, false); io.AddMousePosEvent(-1000, -1000);
            var flags = new List<(ushort Id, bool Trigger)>();
            var commands = actions with { FlagCeLocation = (id, trigger) => flags.Add((id, trigger)) };
            var ce = state.Ce!;
            if (scenario == "unseen-off-island") state = state with { Active = false };
            if (scenario == "unseen-stale") state = state with { Ce = ce with { Snapshot = ce.Snapshot with { ScanFresh = false } } };
            if (scenario == "unseen-empty") state = state with { Ce = ce with { Snapshot = ce.Snapshot with {
                Entries = ce.Snapshot.Entries.Select(e => e with { LastSeen = ce.Now }).ToArray() } } };
            var filtering = scenario.StartsWith("unseen");
            var mouseButton = scenario == "drag-right" ? 1 : scenario == "drag-middle" ? 2 : 0;
            var beforeCenter = Vector2.Zero; var beforeScroll = 0f; ushort beforeSelection = 0;
            ushort clickedAfterDrag = 0;
            var dragStart = Vector2.Zero;
            for (var frame = 0; frame < 18; frame++)
            {
                if (filtering)
                {
                    if (frame == 3 || scenario == "unseen-toggle" && frame == 7)
                    { var p = view.CeTargets["unseen"]; io.AddMousePosEvent(p.X, p.Y); }
                    if (frame == 4 || scenario == "unseen-toggle" && frame == 8) io.AddMouseButtonEvent(0, true);
                    if (frame == 5 || scenario == "unseen-toggle" && frame == 9) io.AddMouseButtonEvent(0, false);
                    if (scenario == "unseen-reset" && frame == 7)
                        state = state with { Ce = ce with { Snapshot = new CeCooldownTracker().Snapshot(ce.Now) } };
                }
                else
                {
                    if (frame == 3)
                    {
                        dragStart = scenario is "drag-icon" or "click-jitter" ? view.CeTargets["boss-42"] : view.CeMapArea.Origin + view.CeMapArea.Size / 2;
                        io.AddMousePosEvent(dragStart.X, dragStart.Y);
                    }
                    if (frame == 4) { io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddMouseWheelEvent(0, 3); }
                    if (frame == 5) io.AddKeyEvent(ImGuiKey.ModCtrl, false);
                    if (frame == 6) { beforeCenter = view.CeMapCenter; beforeScroll = view.PageScroll; beforeSelection = view.SelectedCe; }
                    if (frame == 7) io.AddMouseButtonEvent(mouseButton, true);
                    if (frame == 8)
                    {
                        var end = scenario == "drag-outside" ? view.CeMapArea.Origin + view.CeMapArea.Size + new Vector2(40) :
                            dragStart + (scenario == "click-jitter" ? new Vector2(1) : new Vector2(80, 35));
                        io.AddMousePosEvent(end.X, end.Y);
                    }
                    if (frame == 9) io.AddMouseButtonEvent(mouseButton, false);
                    if (frame == 10)
                    {
                        var jitter = scenario == "click-jitter";
                        if (view.CeMapZoom < 1.5f || Math.Abs(view.PageScroll - beforeScroll) > 0.1f || flags.Count != 0 ||
                            (jitter ? view.CeMapCenter != beforeCenter || view.SelectedCe != 42 : view.CeMapCenter == beforeCenter || view.SelectedCe != beforeSelection))
                            throw new InvalidOperationException($"CE zoom/drag/select isolation failed: {scenario}");
                    }
                    if (scenario != "drag-outside")
                    {
                        if (frame == 12)
                        {
                            var target = view.CeTargets.Where(t => t.Key.StartsWith("boss-") && ushort.TryParse(t.Key[5..], out _))
                                .MinBy(t => Vector2.DistanceSquared(t.Value, view.CeMapArea.Origin + view.CeMapArea.Size / 2));
                            clickedAfterDrag = ushort.Parse(target.Key[5..]);
                            io.AddMousePosEvent(target.Value.X, target.Value.Y);
                        }
                        if (frame == 13) io.AddMouseButtonEvent(0, true);
                        if (frame == 14) io.AddMouseButtonEvent(0, false);
                        if (frame == 16 && view.SelectedCe != clickedAfterDrag) throw new InvalidOperationException("Click selection must still work after map dragging.");
                    }
                }
                ImGui.NewFrame();
                using (new CompassTheme())
                {
                    ImGui.SetNextWindowPos(new Vector2(20)); ImGui.SetNextWindowSize(io.DisplaySize - new Vector2(40));
                    if (ImGui.Begin($"CE 互動驗證##{scenario}", ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                        view.Draw(state, commands);
                    ImGui.End();
                }
                ImGui.Render();
            }
            if (filtering)
            {
                var expected = scenario is "unseen-off-island" or "unseen-toggle" or "unseen-reset" ? 15 :
                    state.Ce!.Snapshot.Entries.Count(e => e.LastSeen is null);
                if (view.CeRowsDrawn != expected || flags.Count != 0)
                    throw new InvalidOperationException($"CE unseen filter failed: {scenario}, {view.CeRowsDrawn} != {expected}");
                if (view.CeUnseenOnly && expected > 0 && state.Active && state.Ce!.Snapshot.Entries.Single(e => e.Definition.Id == view.SelectedCe).LastSeen is not null)
                    throw new InvalidOperationException("Unseen selection must not retain a hidden, previously observed CE.");
                if (expected == 0 && (view.SelectedCe != 0 || view.CeTargets.ContainsKey("boss-flag")))
                    throw new InvalidOperationException("An empty CE filter must not leave stale flag controls.");
            }
            var path = Path.Combine(output, $"ce-interaction-{scenario}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 960, 1350, path);
            Console.WriteLine($"Rendered ce-interaction-{scenario}: {path}");
        }
    }
}
