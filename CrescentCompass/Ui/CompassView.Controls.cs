using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private void DrawRouteControls(CompassViewState state, CompassActions actions)
    {
        if (state.Controls is not { } control) return;
        var carrots = control.RouteKind == PatrolRouteKind.Carrot;
        ImGui.TextColored(Muted, carrots ? "南部蘿蔔 25 點 · 依提供圖表巡查" : control.RouteKind == PatrolRouteKind.Bocchi ? "南部 68 點 · BOCCHI 分區順序" : "南部 68 點 · 依圖表編號巡查");
        if (control.ChartMode)
        {
            var number = control.StartNumber;
            ImGui.SetNextItemWidth(U(145));
            if (ImGui.InputInt("起點編號", ref number)) actions.SetChartStart?.Invoke(Math.Clamp(number, 1, carrots ? CarrotRoute.Count : ChestChart.Count));
            var next = control.NextChartNumber ?? (control.RouteKind == PatrolRouteKind.Chart && control.LastOpen is { } last ? ChestChart.Next(last.Number) : (int?)null);
            if (next is { } nextNumber)
            {
                ImGui.SameLine(); ImGui.BeginDisabled(!state.Active || state.Planning || !state.Region.Contains("南"));
                if (ImGui.Button($"接續上次 → #{nextNumber:00}")) actions.ContinueAfterLast?.Invoke();
                ImGui.EndDisabled();
            }
            ImGui.TextColored(Muted, carrots ? "蘿蔔圖表 1～25 · 巡查一圈" : control.RouteKind == PatrolRouteKind.Bocchi ? "BOCCHI 分區順序 · 圖表編號不變" : "圖表 1～68 · 循環一圈");
            if (state.Active && !state.Region.Contains("南")) ImGui.TextColored(Carrot, "巡查路線僅支援新月島南部。");
        }
        if (!carrots)
        {
            if (control.LastOpen is { } opened)
                ImGui.TextColored(Mint, $"上次開箱 #{opened.Number:00} · {opened.OpenedAt.ToLocalTime():MM/dd HH:mm} · {(opened.Manual ? "手動確認" : "自動判定")}");
            else ImGui.TextColored(Muted, "上次開箱：尚無本角色紀錄");
            HoverHint("依角色儲存，可跨重啟查看。自動判定需捕捉到自己對該箱讀條，再確認同一箱已開啟；不是伺服器領取紀錄，缺少證據時可手動確認。空點略過、只經過或單獨看到箱子已開不寫入。");
        }
        ImGui.BeginDisabled(!state.Active && !state.Transit || state.Route.Count == 0 && !state.Planning);
        ImGui.BeginDisabled(control.Paused && !state.Active);
        if (ToneButton(control.Paused ? "繼續巡查" : "暫停巡查", new Vector2(U(120), U(32)), control.Paused ? Mint : Gold))
        { if (control.Paused) actions.Resume?.Invoke(); else actions.Pause?.Invoke(); }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ToneButton("終止巡查", new Vector2(U(120), U(32)), Coral)) actions.Stop?.Invoke();
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.AlignTextToFramePadding();
        ImGui.TextColored(control.Paused ? Carrot : Muted, control.Paused ? "已暫停" : state.Planning ? "計算路段中" : state.Route.Count > 0 ? "巡查中" : "尚未開始");
        ImGui.Spacing();
    }
}
