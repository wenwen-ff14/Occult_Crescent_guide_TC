using System.Numerics;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private static readonly (CompassPage Page, string Title, string Detail, Vector4 Color)[] Destinations =
    [
        (CompassPage.Patrol, "巡查", "路線規劃", Mint),
        (CompassPage.Pot, "魔法罐", "財寶追蹤", Gold),
        (CompassPage.Fate, "FATE", "即時事件", Coral),
        (CompassPage.Ce, "CE 冷卻", "事件紀錄", Sky),
        (CompassPage.Exploration, "探索筆記", "島嶼探索", Violet),
        (CompassPage.Waymarks, "標點", "位置收藏", Rose),
        (CompassPage.Loot, "背包整理", "", Bronze),
        (CompassPage.Settings, "設定", "個人偏好", Silver),
    ];
    internal Dictionary<CompassPage, Vector2> PageTargets { get; } = [];
    private Vector4 PageAccent => Destinations.Single(d => d.Page == Page).Color;

    private void DrawPageNavigation()
    {
        PageTargets.Clear();
        var width = ImGui.GetContentRegionAvail().X;
        var gap = U(6);
        var columns = Destinations.Length;
        var tileWidth = (width - gap * (columns - 1)) / columns;
        for (var i = 0; i < Destinations.Length; i++)
        {
            var item = Destinations[i];
            if (i % columns > 0) ImGui.SameLine(0, gap);
            var selected = Page == item.Page;
            var origin = ImGui.GetCursorScreenPos();
            var size = new Vector2(tileWidth, U(62));
            ImGui.PushStyleColor(ImGuiCol.Button, selected ? item.Color : Vector4.Lerp(Surface, item.Color, 0.13f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(item.Color, Surface, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, item.Color);
            ImGui.PushStyleColor(ImGuiCol.Border, selected ? item.Color : Alpha(item.Color, 0.55f));
            if (ImGui.Button($"##page-{item.Page}", size)) Page = item.Page;
            var hovered = ImGui.IsItemHovered();
            PageTargets[item.Page] = origin + size / 2;
            var draw = ImGui.GetWindowDrawList();
            var ink = selected || hovered ? Background : item.Color;
            var titleSize = ImGui.CalcTextSize(item.Title).X;
            var titleRatio = Math.Min(1, (tileWidth - U(6)) / titleSize);
            Label(draw, origin + new Vector2((tileWidth - titleSize * titleRatio) / 2, U(item.Detail.Length == 0 ? 22 : 9)), item.Title, ink, 17 * titleRatio);
            var detailSize = ImGui.CalcTextSize(item.Detail).X * 15 / 17;
            var detailRatio = Math.Min(1, (tileWidth - U(6)) / detailSize);
            Label(draw, origin + new Vector2((tileWidth - detailSize * detailRatio) / 2, U(38)), item.Detail, selected || hovered ? Background : Muted, 15 * detailRatio);
            if (selected && item.Detail.Length > 0) draw.AddCircleFilled(origin + new Vector2(tileWidth / 2, U(31)), U(2), Pack(Background));
            ImGui.PopStyleColor(4);
        }
        ImGui.Spacing();
    }

    private void DrawSection(string title, string detail, Vector4 accent)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, U(37)), Pack(Vector4.Lerp(Surface, accent, 0.12f)), U(6));
        draw.AddRectFilled(origin, origin + new Vector2(U(4), U(37)), Pack(accent), U(2));
        Label(draw, origin + new Vector2(U(14), U(8)), title, accent, 19);
        if (width > U(440))
            Label(draw, origin + new Vector2(width - ImGui.CalcTextSize(detail).X * 15 / 17 - U(14), U(10)), detail, Muted, 15);
        ImGui.Dummy(new Vector2(width, U(37)));
    }

    private static bool ToneButton(string label, Vector2 size, Vector4 tone)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(Surface, tone, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(Surface, tone, 0.40f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(Surface, tone, 0.32f));
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(tone, 0.65f));
        ImGui.PushStyleColor(ImGuiCol.Text, tone);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(5);
        return clicked;
    }
}
