using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CrescentCompass.Ui;

internal sealed class CompassTheme : IDisposable
{
    internal static readonly Vector4 Background = new(0.035f, 0.052f, 0.078f, 1);
    internal static readonly Vector4 Surface = new(0.075f, 0.105f, 0.155f, 1);
    internal static readonly Vector4 Raised = new(0.13f, 0.19f, 0.28f, 1);
    internal static readonly Vector4 Border = new(0.30f, 0.41f, 0.54f, 1);
    internal static readonly Vector4 Control = new(0.16f, 0.28f, 0.42f, 1);
    internal static readonly Vector4 Hover = new(0.23f, 0.39f, 0.55f, 1);
    internal static readonly Vector4 Pressed = new(0.12f, 0.32f, 0.40f, 1);
    internal static readonly Vector4 Text = new(0.91f, 0.94f, 0.97f, 1);
    internal static readonly Vector4 Muted = new(0.69f, 0.76f, 0.84f, 1);
    internal static readonly Vector4 Mint = new(0.40f, 0.86f, 0.73f, 1);
    internal static readonly Vector4 Carrot = new(1.00f, 0.68f, 0.37f, 1);
    internal static readonly Vector4 Silver = new(0.67f, 0.81f, 0.95f, 1);
    internal static readonly Vector4 Bronze = new(0.82f, 0.57f, 0.38f, 1);
    internal static readonly Vector4 Gold = new(1f, 0.79f, 0.36f, 1);
    internal static readonly Vector4 Coral = new(1f, 0.53f, 0.51f, 1);
    internal static readonly Vector4 Sky = new(0.39f, 0.77f, 1f, 1);
    internal static readonly Vector4 Violet = new(0.73f, 0.64f, 1f, 1);
    internal static readonly Vector4 Rose = new(1f, 0.57f, 0.79f, 1);
    private int colors;
    private int styles;

    public CompassTheme()
    {
        var scale = ImGui.GetFontSize() / 17f;
        Color(ImGuiCol.WindowBg, Background); Color(ImGuiCol.ChildBg, Surface); Color(ImGuiCol.PopupBg, Surface);
        Color(ImGuiCol.Border, Border); Color(ImGuiCol.Text, Text); Color(ImGuiCol.TextDisabled, Muted);
        Color(ImGuiCol.TitleBg, Surface); Color(ImGuiCol.TitleBgActive, Raised);
        Color(ImGuiCol.MenuBarBg, Raised);
        Color(ImGuiCol.FrameBg, Raised); Color(ImGuiCol.FrameBgHovered, Hover);
        Color(ImGuiCol.FrameBgActive, Pressed);
        Color(ImGuiCol.Button, Control); Color(ImGuiCol.ButtonHovered, Hover);
        Color(ImGuiCol.ButtonActive, Pressed); Color(ImGuiCol.CheckMark, Mint);
        Color(ImGuiCol.Header, Control); Color(ImGuiCol.HeaderHovered, Hover);
        Color(ImGuiCol.HeaderActive, Pressed); Color(ImGuiCol.Separator, Border);
        Color(ImGuiCol.SliderGrab, Mint); Color(ImGuiCol.SliderGrabActive, Text);
        Color(ImGuiCol.TextSelectedBg, new(0.22f, 0.48f, 0.65f, 0.65f));
        Color(ImGuiCol.TableHeaderBg, Raised); Color(ImGuiCol.TableBorderLight, Alpha(Border, 0.45f));
        Color(ImGuiCol.TableBorderStrong, Border); Color(ImGuiCol.TableRowBg, Vector4.Zero);
        Color(ImGuiCol.TableRowBgAlt, new(0.22f, 0.32f, 0.46f, 0.22f));
        Color(ImGuiCol.ScrollbarBg, Background); Color(ImGuiCol.ScrollbarGrab, Border); Color(ImGuiCol.ScrollbarGrabHovered, Muted);
        Style(ImGuiStyleVar.WindowRounding, 12 * scale); Style(ImGuiStyleVar.ChildRounding, 10 * scale);
        Style(ImGuiStyleVar.FrameRounding, 6 * scale); Style(ImGuiStyleVar.PopupRounding, 8 * scale);
        Color(ImGuiCol.ScrollbarGrabActive, Mint);
        Style(ImGuiStyleVar.FrameBorderSize, 1 * scale);
        Style(ImGuiStyleVar.PopupBorderSize, 1 * scale);
        Style(ImGuiStyleVar.ScrollbarSize, 14 * scale);
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
