using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

/// <summary>Independent of the main window; only consumes the existing local FATE snapshot.</summary>
internal sealed class PotCountdownOverlay
{
    private Vector2? dragging;
    internal Vector2 DragTarget { get; private set; }
    internal bool Drawn { get; private set; }
    internal Vector2 Position { get; private set; }
    internal bool ClickThrough { get; private set; }

    internal void Draw(PotFateSnapshot snapshot, DateTimeOffset now, bool visible, bool locked,
        Vector2 savedPosition, Action<Vector2> savePosition)
    {
        Drawn = false;
        if (!visible) { dragging = null; return; }
        if (locked) dragging = null;
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        var width = Math.Min(330 * scale, viewport.Size.X);
        var live = snapshot.ScanFresh ? snapshot.Active.Where(f => f.EndsAt is null || f.EndsAt > now).ToArray() : [];
        var height = (108 + live.Length * 24) * scale;
        var offset = dragging ?? savedPosition;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = new(24, 160);
        offset = Vector2.Clamp(offset, Vector2.Zero, Vector2.Max(Vector2.Zero, viewport.Size - new Vector2(width, height)));
        Position = offset;
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;
        if (locked) flags |= ImGuiWindowFlags.NoInputs;
        ClickThrough = locked;
        ImGui.SetNextWindowPos(viewport.Pos + offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.90f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Gold, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10) * scale);
        if (ImGui.Begin("魔法罐倒數###CrescentCompass.PotCountdown", flags))
        {
            Drawn = true;
            var draw = ImGui.GetWindowDrawList(); var start = ImGui.GetCursorScreenPos();
            if (!locked)
            {
                ImGui.InvisibleButton("pot-overlay-drag", ImGui.GetContentRegionAvail());
                DragTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
                    dragging = offset + ImGui.GetIO().MouseDelta;
                ImGui.SetCursorScreenPos(start);
            }
            draw.AddText(start, Pack(Gold), locked ? "魔法罐倒數 · 預估" : "魔法罐倒數 · 拖曳浮窗調整位置");
            ImGui.Dummy(new Vector2(0, 23 * scale));
            foreach (var fate in live)
            {
                ImGui.TextColored(Mint, $"{Side(fate.Definition)}已出現" + (fate.EndsAt is { } end ? $" · 剩餘 {Clock(end - now)}" : " · 準備中"));
            }
            var next = snapshot.Next is { } definition ? Side(definition) : "下一場";
            var remaining = snapshot.ExpectedAt is not { } at ? "--:--" : at > now ? Clock(at - now) : "等待出現";
            ImGui.TextColored(Text, $"{next} · 預估 {remaining}");
            ImGui.TextColored(Muted, snapshot.ExpectedAt is null ? "尚無本場紀錄，等待觀測" : !snapshot.ScanFresh ? "觀測暫停 · 依既有紀錄推估" :
                snapshot.ExpectedAt <= now ? "已到預估時間，等待實際出現" : snapshot.UsesGameStart ? "依 FATE 開始時間推估" : "依首次偵測時間推估");
        }
        ImGui.End();
        ImGui.PopStyleVar(3); ImGui.PopStyleColor(2);
        if (!locked && dragging is { } moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            savePosition(Vector2.Clamp(moved, Vector2.Zero, Vector2.Max(Vector2.Zero, viewport.Size - new Vector2(width, height))));
            dragging = null;
        }
    }

    private static string Side(PotFateDefinition fate) => fate.Side == "北側" ? "北罐" : "南罐";
    private static string Clock(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
