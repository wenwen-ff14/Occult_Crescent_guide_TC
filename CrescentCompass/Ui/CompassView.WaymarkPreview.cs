using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private int previewArena;
    private Guid previewPreset;
    private static readonly Vector4[] MarkerColors = [Coral, Gold, Sky, Violet, Coral, Gold, Sky, Violet];
    internal int PreviewMarkersDrawn { get; private set; }

    private void DrawWaymarkPreview(WaymarkPreset preset)
    {
        if (previewPreset != preset.Id) { previewPreset = preset.Id; previewArena = 0; }
        PreviewMarkersDrawn = 0;
        if (!ImGui.CollapsingHeader("標點俯視預覽 / 力之塔場地", ImGuiTreeNodeFlags.DefaultOpen)) return;
        var options = "自動辨識場地\0僅看標點配置\0" + string.Join('\0', WaymarkPreview.Arenas.Select(a => a.Name)) + "\0";
        ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X, U(370)));
        ImGui.Combo("##preview-arena", ref previewArena, options);
        WaymarkTargets["preview-arena"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        var index = previewArena == 0 ? WaymarkPreview.Detect(preset) : previewArena - 2;
        var arena = index >= 0 && index < WaymarkPreview.Arenas.Count ? WaymarkPreview.Arenas[index] : null;
        ImGui.TextColored(Sky, arena?.Name ?? "標點配置 · 無場地底圖");
        ImGui.TextWrapped("北方朝上 · 原始 X/Z 座標 · 僅預覽，不會放置或移動現場標點。");
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, U(320));
        ImGui.InvisibleButton("waymark-preview-canvas", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, Pack(Background), U(8));
        var (center, span) = WaymarkPreview.Frame(preset, arena);
        var pixels = Math.Max(1, Math.Min(size.X - U(52), size.Y - U(52))) / span;
        Vector2 Project(Vector2 p) => origin + size / 2 + (p - center) * pixels;
        draw.PushClipRect(origin, origin + size, true);
        var step = span > 300 ? 100f : span > 100 ? 20f : 10f;
        for (float x = MathF.Floor((center.X - span / 2) / step) * step; x <= center.X + span / 2; x += step)
            draw.AddLine(Project(new(x, center.Y - span / 2)), Project(new(x, center.Y + span / 2)), Pack(Alpha(Border, 0.3f)));
        for (float z = MathF.Floor((center.Y - span / 2) / step) * step; z <= center.Y + span / 2; z += step)
            draw.AddLine(Project(new(center.X - span / 2, z)), Project(new(center.X + span / 2, z)), Pack(Alpha(Border, 0.3f)));
        if (arena is not null)
        {
            var c = Project(arena.Center);
            if (arena.Circular)
            {
                draw.AddCircleFilled(c, arena.HalfSize.X * pixels, Pack(Alpha(Sky, 0.09f)), 96);
                draw.AddCircle(c, arena.HalfSize.X * pixels, Pack(Sky), 96, U(2));
            }
            else
            {
                draw.AddRectFilled(Project(arena.Center - arena.HalfSize), Project(arena.Center + arena.HalfSize), Pack(Alpha(Sky, 0.09f)));
                draw.AddRect(Project(arena.Center - arena.HalfSize), Project(arena.Center + arena.HalfSize), Pack(Sky), 0, ImDrawFlags.None, U(2));
            }
            if (index == 1) draw.AddCircle(c, 30 * pixels, Pack(Gold), 96, U(1));
            if (index == 3)
            {
                for (int platform = 0; platform < 3; platform++)
                {
                    var angle = platform * 2 * MathF.PI / 3;
                    Vector2 Rotate(Vector2 p, float a) => new(p.X * MathF.Cos(a) - p.Y * MathF.Sin(a), p.X * MathF.Sin(a) + p.Y * MathF.Cos(a));
                    var platformCenter = arena.Center + Rotate(new(0, 14.5f), angle);
                    Vector2[] corners = [new(-10, -10), new(10, -10), new(10, 10), new(-10, 10)];
                    for (int edge = 0; edge < 4; edge++) draw.AddLine(
                        Project(platformCenter + Rotate(corners[edge], angle + MathF.PI / 4)),
                        Project(platformCenter + Rotate(corners[(edge + 1) % 4], angle + MathF.PI / 4)), Pack(Gold), U(2));
                }
            }
            draw.AddLine(c - new Vector2(U(5), 0), c + new Vector2(U(5), 0), Pack(Muted));
            draw.AddLine(c - new Vector2(0, U(5)), c + new Vector2(0, U(5)), Pack(Muted));
        }
        var hoveredLabels = new List<string>();
        for (int i = 0; i < preset.Markers.Length; i++)
        {
            var marker = preset.Markers[i];
            if (!marker.Active) continue;
            PreviewMarkersDrawn++;
            var p = Project(WaymarkPreview.Horizontal(marker));
            var color = MarkerColors[i];
            if (i < 4) { draw.AddCircleFilled(p, U(13), Pack(color), 24); draw.AddCircle(p, U(14), Pack(Text), 24); }
            else { draw.AddRectFilled(p - new Vector2(U(13)), p + new Vector2(U(13)), Pack(color), U(3)); draw.AddRect(p - new Vector2(U(14)), p + new Vector2(U(14)), Pack(Text), U(3)); }
            var label = WaymarkPreset.Label(i);
            draw.AddText(p - ImGui.CalcTextSize(label) / 2, Pack(Background), label);
            if (hovered && Vector2.Distance(ImGui.GetMousePos(), p) <= U(17))
                hoveredLabels.Add($"{label}: X {marker.X:F2} / Z {marker.Z:F2} / 高度 Y {marker.Y:F2}");
        }
        Label(draw, origin + new Vector2(U(12), U(10)), "北 N ↑", Text, 17);
        Label(draw, origin + new Vector2(U(12), size.Y - U(25)), $"網格 {step:0} m", Muted, 15);
        draw.PopClipRect();
        if (hoveredLabels.Count > 0) ImGui.SetTooltip(string.Join('\n', hoveredLabels));
        var active = preset.Markers.Where(m => m.Active).ToArray();
        ImGui.TextWrapped($"啟用：{string.Join(" / ", preset.Markers.Select((m, i) => m.Active ? WaymarkPreset.Label(i) : null).OfType<string>())} · 高度差 {(active.Length > 0 ? active.Max(m => m.Y) - active.Min(m => m.Y) : 0):F2} m");
        if (arena is not null)
        {
            var outside = active.Count(m => !arena.Contains(WaymarkPreview.Horizontal(m)));
            ImGui.TextColored(outside > 0 ? Coral : Muted, outside > 0 ? $"{outside} 個標點位於此場地輪廓外，請核對所選王房。" : "標點位於此場地的平面輪廓內。");
            ImGui.TextWrapped(index == 1 ? "藍線：初始半徑 35 m；金線：後期半徑 30 m。" : index == 3 ? "金線為三個平台輪廓；不代表目前階段可站立區域。" : "場地輪廓示意；不含地板貼圖與戰鬥機制。");
            ImGui.TextWrapped("場地依公開資料繪製，繁中場地尚待實測；不判定樓層、地面或能否放置。");
        }
    }
}
