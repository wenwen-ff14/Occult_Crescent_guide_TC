using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    internal Vector2 AutoPatrolTarget { get; private set; }
    internal Dictionary<string, Vector2> PatrolRouteTargets { get; } = [];
    private static string PatrolRouteName(PatrolRouteKind kind) => kind switch
    { PatrolRouteKind.Bocchi => "BOCCHI 分區順序", PatrolRouteKind.Carrot => "蘿蔔路線 1～25", _ => "原圖表 1～68" };

    private void DrawAutoPatrolControl(CompassViewState state, CompassActions actions)
    {
        PatrolRouteTargets.Clear();
        if (state.Controls is { } control)
        {
            ImGui.BeginDisabled(actions.SetPatrolRoute is null);
            ImGui.SetNextItemWidth(Math.Min(U(250), ImGui.GetContentRegionAvail().X - U(50)));
            var open = ImGui.BeginCombo("路線", PatrolRouteName(control.RouteKind));
            PatrolRouteTargets["route"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            HoverHint("切換會停止目前巡查並保留已巡查紀錄；重新啟動後套用。箱點圖表編號與指定起點不變。");
            if (open)
            {
                foreach (var kind in new[] { PatrolRouteKind.Bocchi, PatrolRouteKind.Chart, PatrolRouteKind.Carrot })
                {
                    ImGui.BeginDisabled(kind == PatrolRouteKind.Bocchi && !control.BocchiAvailable);
                    if (ImGui.Selectable(PatrolRouteName(kind), control.RouteKind == kind))
                        actions.SetPatrolRoute?.Invoke(kind);
                    PatrolRouteTargets[kind.ToString()] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    ImGui.EndDisabled();
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            if (control.RouteDetail.Length > 0) ImGui.TextWrapped(control.RouteDetail);
            HoverHint("BOCCHI 預先計算距離僅作站間參考，不含目前位置至首站；實際可通行路徑仍由本機 vnavmesh 驗證。跨區段仍步行，不自動返回營地或傳送。");
            ImGui.Spacing();
        }
        var patrol = state.AutoPatrol ?? new(false, false, "需要相容的 vnavmesh；僅支援新月島南部。");
        ImGui.BeginDisabled(actions.SetAutoPatrol is null || !patrol.Enabled && !patrol.CanStart);
        if (ToneButton(patrol.Enabled ? "停止自動巡查" : "啟動自動巡查", new Vector2(U(180), U(34)), patrol.Enabled ? Coral : Mint))
            actions.SetAutoPatrol?.Invoke(!patrol.Enabled);
        AutoPatrolTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
        HoverHint(patrol.Enabled ? "停止本次自動移動與互動，保留巡查路線與紀錄。" :
            state.Controls?.RouteKind == PatrolRouteKind.Carrot ? "依提供圖表巡查 25 個蘿蔔點；到活躍點自動使用背包的胡蘿蔔（48096），等待兔子寶箱並開啟。請先下坐騎；一圈結束停止，不自動傳送。" :
            "沿目前路線自動移動並開箱；尚無路線時使用指定起點。每次登入需手動啟動。");
        ImGui.SameLine(); ImGui.AlignTextToFramePadding();
        ImGui.TextColored(patrol.Enabled ? Mint : Muted, patrol.Enabled ? "自動巡查已啟動" : "手動巡查");
        ImGui.TextWrapped(patrol.Detail);
        ImGui.Spacing();
    }
}
