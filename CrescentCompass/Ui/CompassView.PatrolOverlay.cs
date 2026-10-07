using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    internal Dictionary<string, Vector2> PatrolOverlayTargets { get; } = [];

    private void DrawPatrolOverlayControls(CompassViewState state, CompassActions actions)
    {
        PatrolOverlayTargets.Clear();
        ImGui.PushID("patrol-overlay-controls");
        var enabled = state.PatrolOverlay?.Enabled == true;
        if (ImGui.Checkbox("在畫面顯示巡查進度", ref enabled)) actions.PatrolOverlay?.SetVisible(enabled);
        PatrolOverlayTargets["visible"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        HoverHint("巡查或規劃期間顯示，可直接按下一站、暫停／繼續與終止。關閉主介面後仍可操作。\n拖曳浮窗標題移動，放開即保存。暫停時保留；完成、終止或尚未開始時隱藏。\n僅在新月島顯示；離島、傳送、過場及隱藏遊戲介面時隱藏。與魔法罐倒數、場景提示分開設定。");
        ImGui.PopID();
    }
}
