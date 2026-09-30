using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CrescentCompass.Ui;

internal sealed class CompassTheme : IDisposable
{
    internal static readonly Vector4 Background = new(0.035f, 0.052f, 0.078f, 1);
    internal static readonly Vector4 Surface = new(0.060f, 0.084f, 0.116f, 1);
    internal static readonly Vector4 Raised = new(0.094f, 0.126f, 0.165f, 1);
    internal static readonly Vector4 Border = new(0.155f, 0.205f, 0.255f, 1);
    internal static readonly Vector4 Text = new(0.91f, 0.94f, 0.97f, 1);
    internal static readonly Vector4 Muted = new(0.57f, 0.65f, 0.73f, 1);
    internal static readonly Vector4 Mint = new(0.40f, 0.86f, 0.73f, 1);
    internal static readonly Vector4 Carrot = new(1.00f, 0.68f, 0.37f, 1);
    internal static readonly Vector4 Silver = new(0.67f, 0.81f, 0.95f, 1);
    internal static readonly Vector4 Bronze = new(0.82f, 0.57f, 0.38f, 1);
    private int colors;
    private int styles;

    public CompassTheme()
    {
        var scale = ImGui.GetFontSize() / 17f;
        Color(ImGuiCol.WindowBg, Background); Color(ImGuiCol.ChildBg, Surface); Color(ImGuiCol.PopupBg, Surface);
        Color(ImGuiCol.Border, Border); Color(ImGuiCol.Text, Text); Color(ImGuiCol.TextDisabled, Muted);
        Color(ImGuiCol.TitleBg, Surface); Color(ImGuiCol.TitleBgActive, Surface);
        Color(ImGuiCol.FrameBg, Raised); Color(ImGuiCol.FrameBgHovered, new(0.14f, 0.21f, 0.26f, 1));
        Color(ImGuiCol.FrameBgActive, new(0.17f, 0.29f, 0.30f, 1));
        Color(ImGuiCol.Button, Raised); Color(ImGuiCol.ButtonHovered, new(0.16f, 0.26f, 0.30f, 1));
        Color(ImGuiCol.ButtonActive, new(0.19f, 0.34f, 0.35f, 1)); Color(ImGuiCol.CheckMark, Mint);
        Color(ImGuiCol.Header, Raised); Color(ImGuiCol.HeaderHovered, new(0.13f, 0.24f, 0.28f, 1));
        Color(ImGuiCol.HeaderActive, new(0.16f, 0.29f, 0.31f, 1)); Color(ImGuiCol.Separator, Border);
        Color(ImGuiCol.TableHeaderBg, Surface); Color(ImGuiCol.TableBorderLight, Alpha(Border, 0.45f));
        Color(ImGuiCol.TableBorderStrong, Border); Color(ImGuiCol.TableRowBg, Vector4.Zero);
        Color(ImGuiCol.TableRowBgAlt, new(0.12f, 0.18f, 0.24f, 0.15f));
        Color(ImGuiCol.ScrollbarBg, Background); Color(ImGuiCol.ScrollbarGrab, Border); Color(ImGuiCol.ScrollbarGrabHovered, Muted);
        Style(ImGuiStyleVar.WindowRounding, 12 * scale); Style(ImGuiStyleVar.ChildRounding, 10 * scale);
        Style(ImGuiStyleVar.FrameRounding, 6 * scale); Style(ImGuiStyleVar.PopupRounding, 8 * scale);
        Style(ImGuiStyleVar.ScrollbarSize, 10 * scale);
        Style(ImGuiStyleVar.WindowPadding, new Vector2(20, 18) * scale);
        Style(ImGuiStyleVar.FramePadding, new Vector2(12, 7) * scale);
        Style(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10) * scale);
        Style(ImGuiStyleVar.CellPadding, new Vector2(10, 9) * scale);
    }

    internal static Vector4 Alpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);
    internal static uint Pack(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
    private void Color(ImGuiCol name, Vector4 value) { ImGui.PushStyleColor(name, value); colors++; }
    private void Style(ImGuiStyleVar name, float value) { ImGui.PushStyleVar(name, value); styles++; }
    private void Style(ImGuiStyleVar name, Vector2 value) { ImGui.PushStyleVar(name, value); styles++; }
    public void Dispose() { ImGui.PopStyleVar(styles); ImGui.PopStyleColor(colors); }
}
