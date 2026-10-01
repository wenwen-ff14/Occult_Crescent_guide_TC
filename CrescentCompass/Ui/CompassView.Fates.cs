using System.Numerics;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private void DrawGeneralFates(CompassViewState state, CompassActions actions)
    {
        ImGui.TextColored(Mint, "新月島 FATE · 事件與自動標點");
        var fates = state.GeneralFates;
        var enabled = fates?.AutoFlag ?? true;
        if (ImGui.Checkbox("FATE 出現時自動標點", ref enabled)) actions.SetFateAutoFlag?.Invoke(enabled);
        ImGui.TextWrapped("包含島上所有一般 FATE 與魔法罐 FATE。每場標點一次；同時出現優先最新，開始時間相同則選最近的一場。上島時已出現的事件也會標點。");
        ImGui.TextWrapped("魔法罐尋寶優先。FATE 標點期間保留巡查路線，事件結束後接續；可隨時解除。關閉後重新開啟只處理下一場新事件。");
        ImGui.TextWrapped(fates?.Detail ?? "等待進入新月島。");
        ImGui.BeginDisabled(fates?.Holding != true);
        if (ImGui.Button("解除本次標點並接續巡查")) actions.ReleaseFateNavigation?.Invoke();
        ImGui.EndDisabled();
        if (!state.Active) { DrawWaiting(state); return; }
        if (fates is null || fates.Active.Count == 0) { ImGui.TextColored(Muted, "目前未偵測到正在進行的一般 FATE。"); return; }
        ImGui.Spacing();
        if (!ImGui.BeginTable("general-fates", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0, Math.Max(U(160), ImGui.GetContentRegionAvail().Y - U(12))))) return;
        ImGui.TableSetupColumn("事件／座標／狀態", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, U(75));
        foreach (var fate in fates.Active)
        {
            ImGui.PushID(fate.Id); ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.TextWrapped(fate.Name); ImGui.TextColored(Mint, fate.Coordinates); ImGui.TextWrapped(fate.Status);
            ImGui.TableNextColumn(); ImGui.BeginDisabled(!fate.CanFlag);
            if (ImGui.Button("插旗")) actions.FlagGeneralFate?.Invoke(fate.Id);
            ImGui.EndDisabled(); ImGui.PopID();
        }
        ImGui.EndTable();
    }
}
