using System.Numerics;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed record CompassPatrolOverlayState(string Status, string Target, string Coordinates,
    int Completed, int Skipped, int Remaining, int Total, string Detail, bool Waiting,
    bool Paused = false, bool Planning = false, bool Occupied = false)
{
    internal bool HasPatrol => Remaining > 0 || Planning;
    internal bool CanAdvance => Remaining > 0 && !Waiting && !Paused && !Planning && !Occupied;
}

internal sealed record CompassPatrolOverlayCommands(Action Next, Action Pause, Action Resume, Action Stop);

/// <summary>Independent route display with explicit controls bound to the existing patrol actions.</summary>
internal sealed class PatrolOverlay
{
    private Vector2? dragging;
    internal Vector2 DragTarget { get; private set; }
    internal bool Drawn { get; private set; }
    internal Vector2 Position { get; private set; }
    internal Vector2 Size { get; private set; }
    internal Dictionary<string, Vector2> ButtonTargets { get; } = [];

    internal void Draw(CompassPatrolOverlayState? state, bool visible, bool locked,
        Vector2 savedPosition, Action<Vector2> savePosition, CompassPatrolOverlayCommands commands)
    {
        Drawn = false;
        ButtonTargets.Clear();
        if (!visible || state is not { HasPatrol: true }) { dragging = null; return; }
        if (locked) dragging = null;
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        var width = Math.Min(400 * scale, viewport.Size.X);
        var textWidth = Math.Max(1, width - 28 * scale);
        var detail = locked ? state.Detail : "拖曳浮窗標題移動，放開保存；按鈕可直接操作。";
        var height = 150 * scale + ImGui.CalcTextSize(state.Target, false, textWidth).Y +
            ImGui.CalcTextSize(state.Coordinates, false, textWidth).Y + ImGui.CalcTextSize(detail, false, textWidth).Y;
        Size = new(width, height);
        var offset = dragging ?? savedPosition;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = new(24, 320);
        var maximum = Vector2.Max(Vector2.Zero, viewport.Size - Size);
        offset = Vector2.Clamp(offset, Vector2.Zero, maximum);
        Position = offset;
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;
        ImGui.SetNextWindowPos(viewport.Pos + offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.90f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Mint, 0.65f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Raised);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Mint);
        ImGui.PushStyleColor(ImGuiCol.Button, Control);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Pressed);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        if (ImGui.Begin("巡查進度###CrescentCompass.PatrolOverlay", flags))
        {
            Drawn = true;
            var draw = ImGui.GetWindowDrawList(); var start = ImGui.GetCursorScreenPos();
            if (!locked)
            {
                ImGui.InvisibleButton("patrol-overlay-drag", new Vector2(ImGui.GetContentRegionAvail().X, 23 * scale));
                DragTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
                    dragging = offset + ImGui.GetIO().MouseDelta;
                ImGui.SetCursorScreenPos(start);
            }
            draw.AddText(start, Pack(state.Waiting ? Gold : Mint), $"巡查進度 · {state.Status}");
            ImGui.Dummy(new Vector2(0, 23 * scale));
            ImGui.TextWrapped(state.Target);
            ImGui.PushStyleColor(ImGuiCol.Text, Muted);
            ImGui.TextWrapped(state.Coordinates);
            ImGui.PopStyleColor();
            var progress = state.Total > 0 ? Math.Clamp((state.Completed + state.Skipped) / (float)state.Total, 0, 1) : 0;
            ImGui.ProgressBar(progress, new Vector2(-1, 12 * scale), "");
            ImGui.TextUnformatted($"已巡查 {state.Completed} · 略過 {state.Skipped} · 待巡 {state.Remaining} / {state.Total}");
            ImGui.PushStyleColor(ImGuiCol.Text, Muted);
            ImGui.TextWrapped(detail);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            var buttonSize = new Vector2((ImGui.GetContentRegionAvail().X - 16 * scale) / 3, 30 * scale);
            ImGui.BeginDisabled(!state.CanAdvance);
            if (ImGui.Button("下一站", buttonSize)) commands.Next();
            ButtonTargets["next"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(state.CanAdvance ? "將目前站點記為已巡查，前往下一站；不記為已開箱。" : "暫停、規劃、事件優先或遊戲忙碌時，暫停手動換站。");
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button(state.Paused ? "繼續" : "暫停", buttonSize))
            { if (state.Paused) commands.Resume(); else commands.Pause(); }
            ButtonTargets["pause"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(Surface, Coral, 0.25f));
            if (ImGui.Button("終止", buttonSize)) commands.Stop();
            ButtonTargets["stop"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.PopStyleColor();
        }
        ImGui.End();
        ImGui.PopStyleVar(5); ImGui.PopStyleColor(8);
        if (!locked && dragging is { } moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            savePosition(Vector2.Clamp(moved, Vector2.Zero, maximum));
            dragging = null;
        }
    }
}
