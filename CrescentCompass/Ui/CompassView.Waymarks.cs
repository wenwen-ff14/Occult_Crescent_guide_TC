using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private string waymarkName = "", waymarkJson = "", waymarkSearch = "", waymarkRename = "", waymarkClipboardStatus = "";
    private bool waymarkAssignMap, waymarkDeleteConfirm;
    private Guid waymarkSelected;
    private int waymarkSelectionRevision;
    internal Guid SelectedWaymark => waymarkSelected;
    internal Dictionary<string, Vector2> WaymarkTargets { get; } = [];

    private void DrawWaymarks(CompassViewState state, CompassActions actions)
    {
        WaymarkTargets.Clear();
        var library = state.Waymarks ?? new CompassWaymarkState([], false, false, false, "預設庫尚未就緒。");
        var commands = actions.Waymarks;
        // Consume each successful save/import once. Manual selection on later
        // frames must not be pulled back to the last saved preset.
        if (library.SelectionRevision != waymarkSelectionRevision)
        {
            if (library.Presets.FirstOrDefault(p => p.Id == library.SelectionRequest) is { } saved)
            { SelectWaymark(saved); waymarkSearch = ""; }
            waymarkSelectionRevision = library.SelectionRevision;
        }
        void Target(string name) => WaymarkTargets[name] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        // Wide windows separate the library from actions on the selected preset.
        // Narrow windows keep one reading order with the page's existing scrollbar.
        var split = ImGui.GetContentRegionAvail().X >= U(820) &&
            ImGui.BeginTable("waymark-workspace", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.NoSavedSettings);
        if (split)
        {
            ImGui.TableSetupColumn("預設庫", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("已選預設操作", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
        }
        ImGui.TextColored(Gold, "新增預設  /  儲存目前場上的標點");
        HoverHint("保存現場 A–D／1–4，或匯入 Waymark Preset JSON。預設獨立保存，重新進島與重載插件後仍可使用。");
        if (library.Presets.Count == 0) DrawWaymarkStatus(library);
        if (library.Error.Length > 0) ImGui.TextWrapped(library.Error);
        ImGui.Spacing();
        ImGui.SetNextItemWidth(Math.Max(U(180), ImGui.GetContentRegionAvail().X - U(125)));
        ImGui.InputTextWithHint("##waymark-name", "預設名稱（留空以日期時間命名）", ref waymarkName, 401);
        ImGui.SameLine();
        ImGui.BeginDisabled(commands is null || !library.CanCapture);
        if (ToneButton("儲存現場標點", Vector2.Zero, Gold)) commands?.Save(waymarkName);
        Target("save"); ImGui.EndDisabled();
        if (!library.CanCapture && !library.Busy) ImGui.TextColored(Muted, "進入新月島南部後可儲存現場；匯入及管理可在島外操作。");
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Lerp(Surface, Sky, 0.22f));
        var importOpen = ImGui.CollapsingHeader("從分享代碼匯入預設（Waymark JSON）", library.Presets.Count == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor();
        Target("import-header");
        if (importOpen)
        {
            ImGui.InputTextMultiline("##waymark-json", ref waymarkJson, 65537, new Vector2(-1, U(78)));
            Target("json");
            if (ImGui.Button("從剪貼簿貼上"))
            {
                try
                {
                    var text = ImGui.GetClipboardText();
                    if (text.Length > 65536) waymarkClipboardStatus = "內容超過 64 KB，請只貼上一組標點。";
                    else { waymarkJson = text; waymarkClipboardStatus = ""; }
                }
                catch (Exception) { waymarkClipboardStatus = "無法讀取剪貼簿，可直接在文字框貼上。"; }
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(commands is null || library.Error.Length > 0 || string.IsNullOrWhiteSpace(waymarkJson));
            if (ToneButton("匯入為新預設", Vector2.Zero, Sky)) commands?.Import(waymarkJson, waymarkAssignMap);
            Target("import"); ImGui.EndDisabled();
            ImGui.Checkbox("MapID 為 0／缺漏時，我確認這份標點屬於新月島南部", ref waymarkAssignMap);
            HoverHint("南部 Waymark 的 MapID 為 1018。其他副本的標點會拒絕匯入；缺少地圖資訊的舊匯出需勾選後才能匯入。");
        }
        if (waymarkClipboardStatus.Length > 0) ImGui.TextWrapped(waymarkClipboardStatus);
        ImGui.Separator();
        ImGui.TextColored(Sky, $"選擇預設  /  共 {library.Presets.Count} 組 · 點選下方名稱");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##waymark-search", "搜尋名稱…", ref waymarkSearch, 401);
        if (library.Presets.All(p => p.Id != waymarkSelected)) SelectWaymark(library.Presets.FirstOrDefault());
        var rows = library.Presets.Where(p => p.Name.Contains(waymarkSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length == 0) ImGui.TextColored(Muted, library.Presets.Count == 0 ? "還沒有預設。先儲存現場，或貼上 JSON 匯入。" : "沒有符合名稱的預設。");
        else if (ImGui.BeginTable("waymark-presets", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings,
                     new Vector2(0, U(Math.Min(150, 36 + rows.Length * 50)))))
        {
            ImGui.TableSetupColumn("名稱／地點", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("儲存時間", ImGuiTableColumnFlags.WidthFixed, U(158));
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(rows.Length);
            while (clipper.Step()) for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var row = rows[i];
                ImGui.PushID(row.Id.ToString()); ImGui.TableNextRow(ImGuiTableRowFlags.None, U(47)); ImGui.TableNextColumn();
                var isSelected = waymarkSelected == row.Id;
                ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Lerp(Surface, Mint, 0.38f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Lerp(Surface, Mint, 0.48f));
                ImGui.PushStyleColor(ImGuiCol.Text, isSelected ? Mint : Text);
                if (ImGui.Selectable((isSelected ? "[已選取]  " : "[選取]  ") + row.Name + "##select", isSelected,
                    ImGuiSelectableFlags.SpanAllColumns)) SelectWaymark(row);
                ImGui.PopStyleColor(3);
                Target("preset-" + row.Id);
                HoverHint(row.Name);
                ImGui.TextColored(Muted, $"新月島南部 · {row.Markers.Count(p => p.Active)} 個標點");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                ImGui.PopID();
            }
            clipper.End(); ImGui.Destroy(clipper); ImGui.EndTable();
        }
        if (split) ImGui.TableNextColumn();
        if (library.Presets.FirstOrDefault(p => p.Id == waymarkSelected) is { } selected)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Mint);
            ImGui.TextWrapped("即將放置  /  " + selected.Name);
            ImGui.PopStyleColor();
            var ignoreDistance = library.IgnoreDistance;
            ImGui.BeginDisabled(commands?.SetIgnoreDistance is null || library.Busy);
            if (ImGui.Checkbox("忽略距離（使用預設座標）", ref ignoreDistance)) commands?.SetIgnoreDistance?.Invoke(ignoreDistance);
            Target("ignore-distance"); ImGui.EndDisabled();
            HoverHint("啟用後略過 200 公尺與地面校正，直接使用預設的 XYZ（含高度）；仍須同區域、非戰鬥，且由遊戲接受操作。設定會保留。");
            ImGui.BeginDisabled(commands is null || !library.CanPlace);
            if (PrimaryButton("放置已選預設", new Vector2(U(160), 0))) commands?.Place(selected.Id);
            Target("place"); ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(commands is null);
            if (ImGui.Button("複製 Waymark JSON"))
            {
                try { if (commands?.Export(selected.Id) is { } json) { ImGui.SetClipboardText(json); waymarkClipboardStatus = "已複製 Waymark JSON。"; } }
                catch (Exception) { waymarkClipboardStatus = "無法寫入剪貼簿，請稍後重試。"; }
            }
            ImGui.EndDisabled();
            if (library.Busy) { if (ToneButton("取消放置", Vector2.Zero, Gold)) commands?.Cancel(); Target("cancel"); }
            DrawWaymarkStatus(library);
            Target("placement-status");
            if (!library.CanPlace && library.PlacementUnavailableReason.Length > 0)
                ImGui.TextWrapped(library.PlacementUnavailableReason);
            ImGui.TextWrapped(library.IgnoreDistance
                ? "依預設 XYZ 放置，不限制與角色的距離，也不校正地面高度。仍須在新月島南部、非戰鬥；同名標點會被取代，未啟用的標點會清除。"
                : "放置會取代同名標點，並清除本預設未啟用的標點。請在非戰鬥時靠近目標位置（200 公尺內）；八個標點依序處理。");
            ImGui.Separator();
            ImGui.TextColored(Violet, "管理已選預設  /  新名稱");
            ImGui.BeginDisabled(commands is null || library.Error.Length > 0);
            ImGui.SetNextItemWidth(Math.Max(U(150), ImGui.GetContentRegionAvail().X - U(166)));
            ImGui.InputTextWithHint("##waymark-rename", "替已選預設輸入新名稱", ref waymarkRename, 401); ImGui.SameLine();
            if (ToneButton("重新命名", Vector2.Zero, Violet)) commands?.Rename(selected.Id, waymarkRename);
            ImGui.SameLine();
            if (ToneButton("刪除", Vector2.Zero, Coral)) waymarkDeleteConfirm = true;
            Target("delete");
            if (waymarkDeleteConfirm)
            {
                ImGui.TextWrapped($"刪除「{selected.Name}」？現場標點不受影響。");
                if (ImGui.Button("確認刪除此預設")) { commands?.Delete(selected.Id); waymarkDeleteConfirm = false; }
                Target("delete-confirm"); ImGui.SameLine();
                if (ImGui.Button("保留")) waymarkDeleteConfirm = false;
            }
            ImGui.EndDisabled();
            if (ImGui.CollapsingHeader("檢視八個標點世界座標"))
            {
                ImGui.TextWrapped("下列為遊戲世界座標，Y 代表高度；不是地圖上的 X／Y 座標。");
                for (int i = 0; i < 8; i++)
                {
                    var p = selected.Markers[i];
                    ImGui.TextColored(p.Active ? Mint : Muted, $"{WaymarkPreset.Label(i),-2}  " +
                        (p.Active ? FormattableString.Invariant($"X {p.X:0.000}   Y（高度）{p.Y:0.000}   Z {p.Z:0.000}") : "未啟用"));
                }
            }
        }
        else if (split) ImGui.TextWrapped("先儲存或匯入一組預設，再從左側選取。放置與管理操作會顯示在這裡。");
        if (split) ImGui.EndTable();
        if (library.Presets.FirstOrDefault(p => p.Id == waymarkSelected) is { } preview)
            DrawWaymarkPreview(preview);
    }

    private void SelectWaymark(WaymarkPreset? preset)
    { waymarkSelected = preset?.Id ?? Guid.Empty; waymarkRename = preset?.Name ?? ""; waymarkDeleteConfirm = false; }

    private static void DrawWaymarkStatus(CompassWaymarkState library)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, library.Failed ? Carrot : library.Busy ? Mint : Text);
        ImGui.TextWrapped("狀態：" + library.Detail);
        ImGui.PopStyleColor();
    }
}
