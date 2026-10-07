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
    internal Vector2 Size { get; private set; }
    internal Vector2 FlagTarget { get; private set; }
    internal bool FlagEnabled { get; private set; }

    internal void Draw(PotFateSnapshot snapshot, DateTimeOffset now, bool visible,
        Vector2 savedPosition, Action<Vector2> savePosition, bool nextLocationAvailable, Action<ushort> flagLocation)
    {
        Drawn = false;
        FlagEnabled = false;
        FlagTarget = default;
        if (!visible) { dragging = null; return; }
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        var width = Math.Min(330 * scale, viewport.Size.X);
        var live = snapshot.ScanFresh ? snapshot.Active.Where(f => f.EndsAt is null || f.EndsAt > now).ToArray() : [];
        var height = (146 + live.Length * 24) * scale;
        Size = new(width, height);
        var offset = dragging ?? savedPosition;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = new(24, 160);
        offset = Vector2.Clamp(offset, Vector2.Zero, Vector2.Max(Vector2.Zero, viewport.Size - new Vector2(width, height)));
        Position = offset;
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;
        ImGui.SetNextWindowPos(viewport.Pos + offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.90f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Gold, 0.65f));
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.Button, Control);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Pressed);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        if (ImGui.Begin("魔法罐倒數###CrescentCompass.PotCountdown", flags))
        {
            Drawn = true;
            var draw = ImGui.GetWindowDrawList(); var start = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("pot-overlay-drag", new Vector2(ImGui.GetContentRegionAvail().X, 23 * scale));
            DragTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                dragging = offset + ImGui.GetIO().MouseDelta;
            ImGui.SetCursorScreenPos(start);
            draw.AddText(start, Pack(Gold), "魔法罐倒數 · 預估");
            ImGui.Dummy(new Vector2(0, 23 * scale));
            foreach (var fate in live)
            {
                ImGui.TextColored(Mint, $"{Side(fate.Definition)}已出現" + (fate.EndsAt is { } end ? $" · 剩餘 {Clock(end - now)}" : " · 準備中"));
            }
            var next = snapshot.Next is { } definition ? Side(definition) : "下一場";
            var remaining = snapshot.ExpectedAt is not { } at ? "--:--" : at > now ? Clock(at - now) : "等待出現";
            ImGui.TextColored(Text, $"{next} · 預估 {remaining}");
            ImGui.TextColored(Muted, snapshot.ExpectedAt is null ? "尚無本場紀錄，等待觀測" : !snapshot.ScanFresh ? "觀測暫停 · 依既有紀錄推估" :
                snapshot.ExpectedAt <= now ? "已到預估時間，等待實際出現" : snapshot.IsSharedEstimate ? "進島共享時間 · 本機倒數" :
                snapshot.UsesGameStart ? "依 FATE 開始時間推估" : "依首次偵測時間推估");
            ImGui.Spacing();
            FlagEnabled = nextLocationAvailable && snapshot.Next is not null && snapshot.ExpectedAt is not null;
            // Changing the prediction cancels a press started on the previous target.
            ImGui.PushID(snapshot.Next?.Id ?? 0);
            ImGui.BeginDisabled(!FlagEnabled);
            if (ImGui.Button("下一場插旗", new Vector2(ImGui.GetContentRegionAvail().X, 30 * scale)) && snapshot.Next is { } target)
                flagLocation(target.Id);
            FlagTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(FlagEnabled ? $"設定預估下一場{next}的地圖旗標；不代表事件已出現。" :
                    snapshot.Next is null || snapshot.ExpectedAt is null ? "尚無本場紀錄，無法判斷下一場魔法罐。" : "目前區域或座標尚未就緒。");
            ImGui.EndDisabled(); ImGui.PopID();
        }
        ImGui.End();
        ImGui.PopStyleVar(5); ImGui.PopStyleColor(6);
        if (dragging is { } moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
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
