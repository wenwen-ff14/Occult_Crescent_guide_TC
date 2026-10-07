using System.Numerics;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed class CarrotOverlay
{
    private Vector2? dragging;
    internal readonly CarrotTable Table = new();
    internal bool Drawn { get; private set; }
    internal Vector2 Position { get; private set; }
    internal Vector2 Size { get; private set; }
    internal Vector2 DragTarget { get; private set; }
    internal Vector2 CloseTarget { get; private set; }

    internal void Draw(CarrotTableState state, CarrotTableActions actions, bool visible, Vector2 savedPosition,
        Action<Vector2> savePosition, Action close)
    {
        Drawn = false;
        if (!visible) { dragging = null; return; }
        using var theme = new CompassTheme();
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        Size = Vector2.Min(new Vector2(490, 450) * scale, viewport.Size);
        var maximum = Vector2.Max(Vector2.Zero, viewport.Size - Size);
        var offset = dragging ?? savedPosition;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = new(440, 160);
        Position = offset = Vector2.Clamp(offset, Vector2.Zero, maximum);
        ImGui.SetNextWindowPos(viewport.Pos + offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7, 5) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3) * scale);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing;
        if (ImGui.Begin("蘿蔔搜尋###CrescentCompass.Carrots", flags))
        {
            Drawn = true;
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.InvisibleButton("carrot-overlay-drag", new(Math.Max(1, width - 34 * scale), 24 * scale));
            DragTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) dragging = offset + ImGui.GetIO().MouseDelta;
            ImGui.GetWindowDrawList().AddText(origin, Pack(Mint), "蘿蔔搜尋");
            ImGui.SameLine();
            if (CarrotTable.ToolButton(CarrotTool.Close, "關閉蘿蔔表格", scale, actions.DrawToolButton)) { dragging = null; close(); }
            CloseTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.Separator();
            Table.Draw(state, actions, Math.Max(60 * scale, ImGui.GetContentRegionAvail().Y - 116 * scale));
        }
        ImGui.End(); ImGui.PopStyleVar(4);
        if (dragging is { } moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            savePosition(Vector2.Clamp(moved, Vector2.Zero, maximum));
            dragging = null;
        }
    }
}
