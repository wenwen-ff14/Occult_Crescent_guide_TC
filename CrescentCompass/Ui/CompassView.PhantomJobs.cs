using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    internal bool ShowPhantomJobs { get; set; }
    internal Dictionary<string, Vector2> PhantomTargets { get; } = [];
    private string copiedPhantomMacro = "";

    internal Vector2 PhantomOverlayToggleTarget { get; private set; }
    private void DrawPhantomOverlayControl(CompassViewState state, CompassActions actions)
    {
        var enabled = state.PhantomOverlay?.Enabled == true;
        ImGui.BeginDisabled(actions.PhantomOverlay is null);
        if (ImGui.Checkbox("在畫面顯示幻影職業", ref enabled)) actions.PhantomOverlay?.SetVisible(enabled);
        PhantomOverlayToggleTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
        HoverHint("關閉主介面後仍可點選職業圖示切換；拖曳標題移動並保存位置。\n只在新月島顯示，離島、過場及隱藏遊戲介面時隱藏；戰鬥、倒地或互動期間無法切換。");
    }

    private void DrawPhantomJobs(CompassViewState state, CompassActions actions)
    {
        PhantomTargets.Clear();
        if (ImGui.Button("← 返回設定")) ShowPhantomJobs = false;
        ImGui.SameLine(); ImGui.TextColored(Violet, "幻影職業／巨集");
        DrawPhantomOverlayControl(state, actions);
        ImGui.Spacing();
        var jobs = state.PhantomJobs;
        var current = jobs?.CurrentJob is { } id ? PhantomJobs.Find(id) : null;
        ImGui.TextColored(Mint, current is null ? "目前職業：等待遊戲資料" : $"目前職業：{current.Name}");
        ImGui.TextWrapped(jobs?.Detail ?? "進島前可查看圖標與複製巨集；切換需進入新月島。");
        ImGui.TextWrapped("貼入遊戲巨集後關閉編輯視窗，再拖到快捷列；預設 M 圖示會自動對應職業。");
        ImGui.TextColored(copiedPhantomMacro.Length == 0 ? Muted : Gold,
            copiedPhantomMacro.Length == 0 ? "適用個人／共用的單行職業巨集，自訂圖示會保留。" : $"已複製：{copiedPhantomMacro}");
        ImGui.BeginDisabled(actions.RefreshPhantomMacroIcons is null);
        if (ToneButton("更新快捷列圖示", new Vector2(0, U(32)), Sky)) actions.RefreshPhantomMacroIcons?.Invoke();
        PhantomTargets["refresh-icons"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.EndDisabled();
        HoverHint("只更新單行 /crescent job 職業名稱或編號 的巨集，且圖示為預設 M 或本插件職業圖示。\n巨集名稱、指令內容與快捷列位置保持原樣；含 /micon、多行指令或自訂圖示的巨集會略過。");
        ImGui.TextWrapped(jobs?.MacroIconDetail ?? "關閉遊戲巨集編輯視窗後，預設 M 會自動換成職業圖示。");
        ImGui.Spacing();
        var height = Math.Max(U(170), ImGui.GetContentRegionAvail().Y - U(4));
        if (!ImGui.BeginTable("phantom-jobs", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0, height))) return;
        ImGui.TableSetupColumn("職業／圖標", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("巨集指令", ImGuiTableColumnFlags.WidthFixed, U(136));
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, U(105));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        foreach (var job in PhantomJobs.All)
        {
            ImGui.PushID(job.Id);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, U(78)); ImGui.TableNextColumn();
            var size = new Vector2(U(30), U(40));
            if (actions.DrawPhantomJobIcon?.Invoke(job.IconId, size) != true)
            {
                var origin = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, Pack(Alpha(Violet, 0.25f)), U(5));
                ImGui.GetWindowDrawList().AddText(origin + new Vector2(U(5), U(10)), Pack(Violet), job.Id.ToString("00"));
                ImGui.Dummy(size);
                HoverHint("職業圖標尚未載入。");
            }
            ImGui.SameLine(); ImGui.BeginGroup();
            ImGui.TextColored(current?.Id == job.Id ? Mint : Text, job.Name);
            ImGui.TextColored(Muted, job.EnglishName[8..]);
            if (current?.Id == job.Id) ImGui.TextColored(Mint, "使用中");
            ImGui.EndGroup();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(job.Macro);
            ImGui.TableNextColumn();
            if (ToneButton("複製巨集", new Vector2(U(99), U(29)), Gold))
            {
                if (actions.CopyPhantomMacro is { } copy) copy(job.Macro); else ImGui.SetClipboardText(job.Macro);
                copiedPhantomMacro = job.Macro;
            }
            PhantomTargets[$"copy-{job.Id}"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.BeginDisabled(jobs?.CanSwitch != true || current?.Id == job.Id || actions.SwitchPhantomJob is null);
            if (ToneButton(current?.Id == job.Id ? "使用中" : "切換職業", new Vector2(U(99), U(29)), Mint)) actions.SwitchPhantomJob?.Invoke(job.Id);
            PhantomTargets[$"switch-{job.Id}"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
}
