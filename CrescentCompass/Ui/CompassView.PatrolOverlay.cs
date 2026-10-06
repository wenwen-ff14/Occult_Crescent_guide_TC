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
        HoverHint("巡查或規劃期間顯示，可直接按下一站、暫停／繼續與終止。關閉主介面後仍可操作。\n暫停時保留供繼續；完成、終止或尚未開始時隱藏，解鎖位置也不會在閒置時顯示。\n僅在新月島顯示；離島、傳送、過場及隱藏遊戲介面時隱藏。與魔法罐倒數、場景提示分開設定。");
        if (enabled)
        {
            var locked = state.PatrolOverlay?.Locked ?? true;
            if (ImGui.Button(locked ? "調整位置" : "完成調整"))
            {
                locked = !locked;
                actions.PatrolOverlay?.SetLocked(locked);
            }
            PatrolOverlayTargets["locked"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.SameLine();
            if (ImGui.SmallButton("重設巡查浮窗位置")) actions.PatrolOverlay?.ResetPosition();
            PatrolOverlayTargets["reset"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.TextWrapped(locked ? "巡查浮窗位置已鎖定，下一站／暫停／終止按鈕仍可操作。" :
                "巡查開始後拖曳浮窗標題移動，放開即保存；完成後按「完成調整」鎖定。");
            ImGui.Spacing();
        }
        ImGui.PopID();
    }
}
