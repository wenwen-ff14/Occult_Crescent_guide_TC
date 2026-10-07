using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal enum CarrotTool { Flag, Collected, Reset, Close }
internal sealed record CarrotTableState(IReadOnlyList<CompassPoint> Points, CompassCarrotState Progress, bool Active, string? CurrentId);
internal sealed record CarrotTableActions(Action<Spot> Flag, Action<string, int>? ConfirmPickup, Action? ResetWeights,
    Func<CarrotTool, Vector2, bool>? DrawToolButton = null);

internal sealed class CarrotTable
{
    private bool weightOrder = true;
    private bool hideZero;
    private string? confirmationId;
    private int confirmationRevision;
    private int reportNumber = 1;
    internal Dictionary<string, Vector2> Targets { get; } = [];
    internal int RowsDrawn { get; private set; }
    internal List<string> RowIds { get; } = [];

    internal void Draw(CarrotTableState state, CarrotTableActions actions, float height)
    {
        Targets.Clear(); RowIds.Clear(); RowsDrawn = 0;
        var scale = ImGui.GetFontSize() / 17f;
        var points = state.Points.Where(p => CarrotRoute.Number(p.Spot) is not null).ToArray();
        string? requestedId = null;
        ImGui.TextColored(Mint, $"蘿蔔權重 · 拾取 {state.Progress.Pickups}");
        Hint("本機搜尋優先度，非精確機率。灰色 0、金色 +1、綠色 +2。\n其他玩家在視野外拾取，需依報點手動標記；不更動巡航順序。");
        var summary = $"+2 {points.Count(p => p.CarrotWeight == 2)}  /  +1 {points.Count(p => p.CarrotWeight == 1)}  /  0 {points.Count(p => p.CarrotWeight == 0)}";
        if (ImGui.GetContentRegionAvail().X >= ImGui.CalcTextSize($"蘿蔔權重 · 拾取 {state.Progress.Pickups}  {summary}").X + 12 * scale) ImGui.SameLine();
        ImGui.TextColored(Muted, summary);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3) * scale);
        ImGui.Checkbox("權重排序", ref weightOrder); Target("sort");
        ImGui.SameLine(); ImGui.Checkbox("隱藏 0", ref hideZero); Target("hide-zero");
        ImGui.SameLine(); ImGui.BeginDisabled(!state.Active || actions.ResetWeights is null);
        if (ToolButton(CarrotTool.Reset, "重設全部權重為 +2", scale, actions.DrawToolButton)) actions.ResetWeights?.Invoke();
        Target("reset"); ImGui.EndDisabled();
        var showCoordinates = ImGui.GetContentRegionAvail().X >= 410 * scale;
        var rows = (weightOrder ? points.OrderByDescending(p => p.Status == SpotStatus.Visible).ThenByDescending(p => p.CarrotWeight ?? 2).ThenBy(p => p.ChartNumber)
            : points.OrderBy(p => p.ChartNumber)).Where(p => !hideZero || p.CarrotWeight != 0);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5, 2) * scale);
        if (ImGui.BeginTable("carrot-weights", showCoordinates ? 5 : 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings, new(0, Math.Max(60 * scale, height))))
        {
            ImGui.TableSetupColumn("編號", ImGuiTableColumnFlags.WidthFixed, 46 * scale);
            ImGui.TableSetupColumn("權重", ImGuiTableColumnFlags.WidthFixed, 40 * scale);
            ImGui.TableSetupColumn("距離", showCoordinates ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch, 65 * scale);
            if (showCoordinates) ImGui.TableSetupColumn("座標", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 64 * scale);
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            foreach (var point in rows)
            {
                RowsDrawn++; RowIds.Add(point.Spot.Id);
                ImGui.PushID(point.Spot.Id); ImGui.TableNextRow();
                var current = point.Spot.Id == state.CurrentId;
                if (current) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Pack(Alpha(Mint, 0.14f)));
                ImGui.TableNextColumn(); ImGui.TextColored(current ? Mint : Text, $"{(current ? ">" : " ")}#{point.ChartNumber:00}");
                Hint($"{(current ? "目前巡查點 · " : "")}#{point.ChartNumber:00}\n{point.Coordinates}\n{(point.Status == SpotStatus.Visible ? "目前看見蘿蔔" : "候選位置，未保證有蘿蔔")}");
                var weight = point.CarrotWeight ?? 2;
                ImGui.TableNextColumn(); ImGui.TextColored(weight == 2 ? Mint : weight == 1 ? Gold : Muted, weight == 0 ? "0" : $"+{weight}");
                ImGui.TableNextColumn(); ImGui.TextColored(Muted, float.IsFinite(point.Distance) ? $"{point.Distance:F0} m" : "--");
                Hint("直線距離，不是步行路程。");
                if (showCoordinates) { ImGui.TableNextColumn(); ImGui.TextUnformatted(point.Coordinates); }
                ImGui.TableNextColumn(); ImGui.BeginDisabled(!state.Active);
                if (ToolButton(CarrotTool.Flag, $"插旗 #{point.ChartNumber:00}", scale, actions.DrawToolButton)) actions.Flag(point.Spot);
                Target($"flag-{point.ChartNumber}"); ImGui.EndDisabled(); ImGui.SameLine(0, 4 * scale);
                ImGui.BeginDisabled(!state.Active || !state.Progress.CanConfirm || actions.ConfirmPickup is null);
                if (ToolButton(CarrotTool.Collected, $"標記 #{point.ChartNumber:00} 已拾取", scale, actions.DrawToolButton)) requestedId = point.Spot.Id;
                Target($"report-{point.ChartNumber}"); ImGui.EndDisabled(); ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.SetNextItemWidth(100 * scale);
        ImGui.InputInt("##carrot-report-number", ref reportNumber);
        reportNumber = Math.Clamp(reportNumber, 1, CarrotRoute.Count); Target("number"); Hint("蘿蔔點編號（1～25）");
        ImGui.SameLine(); ImGui.BeginDisabled(!state.Active || !state.Progress.CanConfirm || actions.ConfirmPickup is null);
        if (ImGui.Button("標記已拾取")) requestedId = points.FirstOrDefault(p => p.ChartNumber == reportNumber)?.Spot.Id;
        Target("confirm"); ImGui.EndDisabled();
        if (requestedId is not null)
        {
            confirmationId = requestedId; confirmationRevision = state.Progress.Revision;
            ImGui.OpenPopup("confirm-carrot");
        }
        if (ImGui.BeginPopup("confirm-carrot"))
        {
            var selected = points.FirstOrDefault(p => p.Spot.Id == confirmationId);
            ImGui.TextUnformatted($"確認蘿蔔 #{selected?.ChartNumber:00} 已被拾取？");
            ImGui.TextColored(Muted, "全圖加權（上限 +2），保留原巡航。");
            var changed = state.Progress.Revision != confirmationRevision;
            if (changed) ImGui.TextColored(Gold, "紀錄已更新，請取消後重新確認。");
            ImGui.BeginDisabled(!state.Active || !state.Progress.CanConfirm || selected is null || changed || actions.ConfirmPickup is null);
            if (ImGui.Button("確認拾取"))
            {
                actions.ConfirmPickup?.Invoke(confirmationId!, confirmationRevision);
                confirmationId = null; ImGui.CloseCurrentPopup();
            }
            Target("confirm-yes"); ImGui.EndDisabled(); ImGui.SameLine();
            if (ImGui.Button("取消")) { confirmationId = null; ImGui.CloseCurrentPopup(); }
            Target("cancel"); ImGui.EndPopup();
        }
        if (!ImGui.IsPopupOpen("confirm-carrot")) confirmationId = null;
        ImGui.PopStyleVar();
    }

    internal static bool ToolButton(CarrotTool tool, string hint, float scale, Func<CarrotTool, Vector2, bool>? drawButton)
    {
        var size = new Vector2(26, 24) * scale;
        var pressed = drawButton?.Invoke(tool, size) ?? ImGui.Button(tool switch
        { CarrotTool.Flag => "↗##flag", CarrotTool.Collected => "+##collected", CarrotTool.Reset => "↻##reset", _ => "×##close" }, size);
        Hint(hint);
        return pressed;
    }
    private void Target(string key) => Targets[key] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
    private static void Hint(string text) { if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text); }
}
