using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

/// <summary>Compact manual job shortcuts, independent of the main window.</summary>
internal sealed class PhantomJobOverlay
{
    private Vector2? dragging;
    internal bool Drawn { get; private set; }
    internal Vector2 Position { get; private set; }
    internal Vector2 Size { get; private set; }
    internal Vector2 DragTarget { get; private set; }
    internal Vector2 CloseTarget { get; private set; }
    internal Dictionary<byte, Vector2> ButtonTargets { get; } = [];

    internal void Draw(CompassPhantomJobs state, bool visible, Vector2 savedPosition, Action<Vector2> savePosition,
        Action close, Action<byte> switchJob, Func<uint, Vector2, bool> drawIcon)
    {
        Drawn = false;
        ButtonTargets.Clear();
        if (!visible) { dragging = null; return; }
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        var cell = 44 * scale; var gap = 4 * scale; var padding = 8 * scale;
        var width = Math.Min(5 * cell + 4 * gap + 2 * padding, viewport.Size.X);
        var columns = Math.Clamp((int)((width - 2 * padding + gap) / (cell + gap)), 1, 5);
        var rows = (PhantomJobs.All.Count + columns - 1) / columns;
        Size = new(width, 2 * padding + 24 * scale + rows * cell + (rows - 1) * gap);
        var maximum = Vector2.Max(Vector2.Zero, viewport.Size - Size);
        var offset = dragging ?? savedPosition;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = new(24, 540);
        Position = offset = Vector2.Clamp(offset, Vector2.Zero, maximum);
        ImGui.SetNextWindowPos(viewport.Pos + offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.90f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Gold, 0.65f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.Button, Control);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Pressed);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("幻影職業###CrescentCompass.PhantomJobs", flags))
        {
            Drawn = true;
            var draw = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var closeSize = 20 * scale;
            ImGui.InvisibleButton("phantom-overlay-drag", new(width - 2 * padding - closeSize - gap, closeSize));
            DragTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) dragging = offset + ImGui.GetIO().MouseDelta;
            draw.AddText(origin, Pack(Gold), "幻影職業");
            ImGui.SetCursorScreenPos(origin + new Vector2(width - 2 * padding - closeSize, 0));
            if (ImGui.Button("×##close-phantom", new(closeSize))) { dragging = null; close(); }
            CloseTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("關閉幻影職業浮窗");
            for (var i = 0; i < PhantomJobs.All.Count; i++)
            {
                var job = PhantomJobs.All[i];
                var current = state.CurrentJob == job.Id;
                var start = origin + new Vector2(i % columns * (cell + gap), 24 * scale + i / columns * (cell + gap));
                ImGui.SetCursorScreenPos(start);
                ImGui.PushID(job.Id);
                ImGui.BeginDisabled(!state.CanSwitch || current);
                var clicked = ImGui.Button("##switch", new(cell));
                var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
                ButtonTargets[job.Id] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                var cursor = ImGui.GetCursorScreenPos();
                var iconSize = new Vector2(30, 40) * scale;
                ImGui.SetCursorScreenPos(start + (new Vector2(cell) - iconSize) / 2);
                if (!drawIcon(job.IconId, iconSize))
                {
                    var label = job.ShortName[..1];
                    draw.AddText(start + (new Vector2(cell) - ImGui.CalcTextSize(label)) / 2, Pack(Muted), label);
                    ImGui.Dummy(iconSize);
                }
                ImGui.SetCursorScreenPos(cursor);
                ImGui.EndDisabled(); ImGui.PopID();
                if (current) draw.AddRect(start, start + new Vector2(cell), Pack(Mint), 4 * scale, ImDrawFlags.None, 2 * scale);
                if (hovered) ImGui.SetTooltip($"{job.Name}{(current ? " · 使用中" : "")}\n{state.Detail}");
                if (clicked && state.CanSwitch && !current) switchJob(job.Id);
            }
        }
        ImGui.End();
        ImGui.PopStyleVar(4); ImGui.PopStyleColor(6);
        if (dragging is { } moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            savePosition(Vector2.Clamp(moved, Vector2.Zero, maximum));
            dragging = null;
        }
    }
}
