using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private bool Chip(string text, bool selected, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? accent : Control);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? Vector4.Lerp(accent, Vector4.One, 0.18f) : Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, selected ? accent : Pressed);
        ImGui.PushStyleColor(ImGuiCol.Border, selected ? accent : Border);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? Background : Text);
        var result = ImGui.Button(text, new Vector2(0, U(32)));
        if (selected)
        {
            var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X + U(9), max.Y - U(4)),
                new Vector2(max.X - U(9), max.Y - U(4)), Pack(Background), U(2));
        }
        ImGui.PopStyleColor(5); return result;
    }
    private static bool PrimaryButton(string text, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Mint); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.54f, 0.94f, 0.82f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.73f, 0.62f, 1)); ImGui.PushStyleColor(ImGuiCol.Text, Background);
        var result = ImGui.Button(text, size); ImGui.PopStyleColor(4); return result;
    }
    private void DrawStatus(SpotStatus status, bool tower = false)
    {
        var color = status switch { SpotStatus.Visible or SpotStatus.Explored => Mint, SpotStatus.Unexplored => KindColor(SpotKind.Exploration), SpotStatus.LastSeen => Carrot, _ => Muted };
        var width = Badge(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos() + new Vector2(0, U(4)), tower ? "塔內 · 需解鎖" : StatusName(status), color, status == SpotStatus.Visible);
        ImGui.InvisibleButton("status", new Vector2(width, U(30)));
    }
    private float Badge(ImDrawListPtr draw, Vector2 origin, string text, Vector4 color, bool dot, float fontSize = 13)
    {
        var width = ImGui.CalcTextSize(text).X * fontSize / 17 + U(dot ? 27 : 17);
        draw.AddRectFilled(origin, origin + new Vector2(width, U(24)), Pack(Alpha(color, 0.12f)), U(5));
        if (dot) draw.AddCircleFilled(origin + new Vector2(U(10), U(12)), U(2.5f), Pack(color));
        Label(draw, origin + new Vector2(U(dot ? 18 : 8), U((24 - fontSize) / 2)), text, color, fontSize); return width;
    }
    private void Label(ImDrawListPtr draw, Vector2 position, string text, Vector4 color, float fontSize) => draw.AddText(ImGui.GetFont(), U(fontSize), position, Pack(color), text, 0);
    private void CenterLabel(ImDrawListPtr draw, Vector2 position, string text, Vector4 color, float fontSize) => Label(draw, position - new Vector2(ImGui.CalcTextSize(text).X * fontSize / 17 / 2, 0), text, color, fontSize);
    private static void HoverHint(string message) { if (ImGui.IsItemHovered()) ImGui.SetTooltip(message); }
    internal static void DrawPin(ImDrawListPtr draw, Vector2 center, float radius, Vector4 color)
    {
        draw.AddCircle(center - new Vector2(0, radius * 0.4f), radius * 0.65f, Pack(color), 16, 1.5f);
        draw.AddLine(center + new Vector2(-radius * 0.55f, 0), center + new Vector2(0, radius), Pack(color), 1.5f);
        draw.AddLine(center + new Vector2(radius * 0.55f, 0), center + new Vector2(0, radius), Pack(color), 1.5f);
    }
    private static void DrawCompass(ImDrawListPtr draw, Vector2 center, float radius, Vector4 color)
    {
        draw.AddCircle(center, radius, Pack(Alpha(color, 0.45f)), 48, 1.3f);
        var n = center - new Vector2(0, radius * 0.78f); var s = center + new Vector2(0, radius * 0.78f);
        var w = center - new Vector2(radius * 0.27f, 0); var e = center + new Vector2(radius * 0.27f, 0);
        draw.AddTriangleFilled(n, w, e, Pack(color)); draw.AddTriangleFilled(s, w, e, Pack(Alpha(color, 0.25f)));
        draw.AddCircleFilled(center, radius * 0.09f, Pack(Background));
    }
}
