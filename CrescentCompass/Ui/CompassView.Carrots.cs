using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private bool carrotWeightOrder = true;
    private string? carrotConfirmationId;
    internal Dictionary<string, Vector2> CarrotTargets { get; } = [];
    internal int CarrotRowsDrawn { get; private set; }
    private void DrawCarrotWeights(CompassViewState state, CompassActions actions)
    {
        CarrotTargets.Clear(); CarrotRowsDrawn = 0;
        if (state.Controls is not { RouteKind: PatrolRouteKind.Carrot } controls) return;
        var points = controls.ChartPoints.Where(p => p.Spot.Kind == SpotKind.Carrot).ToArray();
        ImGui.TextColored(Mint, $"蘿蔔搜尋權重 · 已確認拾取 {state.Carrots?.Pickups ?? 0} 根");
        HoverHint("依本機觀測及最多兩根、拾取後重生的假設，表示搜尋優先度，不是精確機率。\n0：此點剛確認為空；+1：之後確認拾取一根；+2：尚未排除或可能有兩根。\n其他玩家在視野外拾取無法自動掌握；權重不改寫固定巡航順序。");
        ImGui.TextColored(Muted, $"+2：{points.Count(p => p.CarrotWeight == 2)} 點 · +1：{points.Count(p => p.CarrotWeight == 1)} 點 · 0：{points.Count(p => p.CarrotWeight == 0)} 點");
        ImGui.Checkbox("權重優先排序", ref carrotWeightOrder);
        CarrotTargets["sort"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.SameLine(); ImGui.BeginDisabled(!state.Active || actions.ResetCarrotWeights is null);
        if (ImGui.SmallButton("重設權重")) actions.ResetCarrotWeights?.Invoke();
        CarrotTargets["reset"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
        var rows = carrotWeightOrder ? points.OrderByDescending(p => p.Status == SpotStatus.Visible).ThenByDescending(p => p.CarrotWeight ?? 2).ThenBy(p => p.ChartNumber)
            : points.OrderBy(p => p.ChartNumber);
        if (ImGui.BeginTable("carrot-weights", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings, new Vector2(0, U(185))))
        {
            ImGui.TableSetupColumn("編號", ImGuiTableColumnFlags.WidthFixed, U(50));
            ImGui.TableSetupColumn("權重", ImGuiTableColumnFlags.WidthFixed, U(54));
            ImGui.TableSetupColumn("地圖座標 / 狀態", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, U(58));
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            foreach (var point in rows)
            {
                CarrotRowsDrawn++;
                ImGui.PushID("carrot-" + point.Spot.Id); ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"#{point.ChartNumber:00}");
                var weight = point.CarrotWeight ?? 2;
                ImGui.TableNextColumn(); ImGui.TextColored(weight == 2 ? Mint : weight == 1 ? Gold : Muted, weight == 0 ? "0" : $"+{weight}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(point.Coordinates);
                if (point.Status == SpotStatus.Visible) { ImGui.SameLine(); ImGui.TextColored(Mint, "已看見"); }
                ImGui.TableNextColumn(); ImGui.BeginDisabled(!state.Active);
                if (ImGui.SmallButton("插旗")) actions.Flag(point.Spot);
                CarrotTargets[$"flag-{point.ChartNumber}"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                ImGui.EndDisabled(); ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(state.Carrots?.CanConfirm != true || actions.ConfirmCarrotPickup is null);
        if (ImGui.Button("手動確認本點已拾取"))
        { carrotConfirmationId = state.Route.FirstOrDefault()?.Spot.Id; ImGui.OpenPopup("confirm-carrot"); }
        CarrotTargets["confirm"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
        if (ImGui.BeginPopup("confirm-carrot"))
        {
            var selected = points.FirstOrDefault(p => p.Spot.Id == carrotConfirmationId);
            ImGui.TextUnformatted($"確認蘿蔔 #{selected?.ChartNumber:00} 已拾取且寶箱已處理？");
            ImGui.BeginDisabled(state.Carrots?.CanConfirm != true || selected is null || state.Route.FirstOrDefault()?.Spot.Id != carrotConfirmationId);
            if (ImGui.Button("確認拾取")) { actions.ConfirmCarrotPickup?.Invoke(carrotConfirmationId!); ImGui.CloseCurrentPopup(); }
            CarrotTargets["confirm-yes"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.EndDisabled(); ImGui.SameLine();
            if (ImGui.Button("取消")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.Spacing();
    }
}
