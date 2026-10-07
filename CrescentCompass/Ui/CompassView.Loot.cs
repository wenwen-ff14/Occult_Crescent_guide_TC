using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed record CompassLootState(IReadOnlyList<LootItem> Items, IReadOnlySet<uint> Garbage,
    IReadOnlyDictionary<uint, int> Counts, LootCleanupMode Mode, bool Armed, string Detail, int Completed);
internal sealed record CompassLootActions(Action<uint, bool> SetKeep, Action<LootCleanupMode> SetMode, Action<bool> SetArmed);

internal sealed partial class CompassView
{
    private string lootSearch = "";
    private bool lootOnlyBag;
    private bool lootOnlyGarbage;
    private int lootRegion;
    private LootCategory? lootCategory;
    private static readonly LootCategory[] LootCategoryOptions = Enum.GetValues<LootCategory>();
    internal Dictionary<string, Vector2> LootTargets { get; } = [];
    internal IReadOnlyList<LootItem> LootRows { get; private set; } = [];

    private void DrawLoot(CompassViewState state, CompassActions actions)
    {
        LootTargets.Clear();
        LootRows = [];
        var loot = state.Loot;
        var commands = actions.Loot;
        if (loot is null || commands is null) { ImGui.TextWrapped("物品清單尚未載入。"); return; }
        ImGui.TextWrapped("勾選「丟棄」＝標記為垃圾；未勾選＝保留。標記物品會依下方模式丟棄或售出。規則套用一般背包內所有同 ID 物品，包含先前持有的物品與整疊數量。");
        var mode = (int)loot.Mode;
        ImGui.SetNextItemWidth(U(230));
        if (ImGui.Combo("處理方式", ref mode, "自動丟棄（新月島內）\0自動售出（NPC 商店）\0")) commands.SetMode((LootCleanupMode)mode);
        ImGui.TextWrapped(loot.Mode == LootCleanupMode.Discard ? "丟棄無法復原。只處理明確標記的垃圾；HQ、收藏品及已加工裝備保留。" :
            "開啟一般 NPC 商店後逐疊自動售出，並自動確認本次售出視窗；停留在出售或回購列表皆可，不需切換分頁。無售價的物品保留，可在島外商店使用。");
        var marked = loot.Items.Count(i => loot.Garbage.Contains(i.Id));
        ImGui.BeginDisabled(!loot.Armed && marked == 0);
        if (ImGui.Button(loot.Armed ? "停止自動處理" : "啟動自動處理")) commands.SetArmed(!loot.Armed);
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextColored(loot.Armed ? Carrot : Muted, $"垃圾 {marked} 種 · 已確認處理 {loot.Completed} 疊");
        ImGui.TextWrapped(loot.Detail);
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped("每次載入／重新登入需手動啟動；變更丟棄標記或模式會停止。已送出的請求無法取消。");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.SetNextItemWidth(U(250)); ImGui.InputTextWithHint("##loot-search", "搜尋名稱／英文／物品 ID", ref lootSearch, 160);
        LootTargets["search"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        var inlineFilters = ImGui.GetContentRegionAvail().X >= U(570);
        ImGui.SetNextItemWidth(U(220));
        var categoryOpen = ImGui.BeginCombo("物品分類", lootCategory is { } selected ? LootCategoryName(selected) : "全部");
        LootTargets["category"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        if (categoryOpen)
        {
            if (ImGui.Selectable("全部", lootCategory is null)) lootCategory = null;
            LootTargets["category-all"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            foreach (var category in LootCategoryOptions)
            {
                if (ImGui.Selectable(LootCategoryName(category), lootCategory == category)) lootCategory = category;
                LootTargets[$"category-{category}"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            }
            ImGui.EndCombo();
        }
        if (inlineFilters) ImGui.SameLine();
        ImGui.SetNextItemWidth(U(150)); ImGui.Combo("來源島嶼", ref lootRegion, "全部\0南部\0北部\0");
        ImGui.Checkbox("只顯示背包內物品", ref lootOnlyBag);
        LootTargets["only-bag"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.SameLine(); ImGui.Checkbox("只顯示垃圾", ref lootOnlyGarbage);
        LootTargets["only-garbage"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        var rows = loot.Items.Where(i => (!lootOnlyBag || loot.Counts.GetValueOrDefault(i.Id) > 0) && (!lootOnlyGarbage || loot.Garbage.Contains(i.Id)) &&
            (lootCategory is null || LootCategories.Classify(i) == lootCategory) &&
            (lootRegion == 0 || i.Sources.Any(s => s.StartsWith(lootRegion == 1 ? "South Horn" : "North Horn"))) &&
            (i.Name.Contains(lootSearch, StringComparison.OrdinalIgnoreCase) || i.EnglishName.Contains(lootSearch, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Contains(lootSearch)))
            .OrderBy(LootCategories.Classify).ThenBy(i => i.Id).ToArray();
        LootRows = rows;
        ImGui.TextColored(Muted, $"顯示 {rows.Length} / {loot.Items.Count} 種 · 野外／魔法罐／幸運兔／塔內 · 資料 2026-10-05");
        if (rows.Length == 0) { ImGui.TextWrapped("沒有符合目前篩選條件的物品。"); return; }
        if (!ImGui.BeginTable("loot-items", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(U(180), ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("丟棄", ImGuiTableColumnFlags.WidthFixed, U(48));
        ImGui.TableSetupColumn("物品／來源", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("背包", ImGuiTableColumnFlags.WidthFixed, U(55));
        ImGui.TableSetupColumn("NPC 單價", ImGuiTableColumnFlags.WidthFixed, U(80));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        foreach (var item in rows)
        {
            ImGui.PushID((int)item.Id); ImGui.TableNextRow(); ImGui.TableNextColumn();
            var discard = loot.Garbage.Contains(item.Id);
            ImGui.BeginDisabled(!item.Available && !discard);
            if (ImGui.Checkbox("##discard", ref discard)) commands.SetKeep(item.Id, !discard);
            ImGui.EndDisabled();
            ImGui.TableNextColumn(); ImGui.TextWrapped(item.Name);
            var categoryName = LootCategoryName(LootCategories.Classify(item));
            HoverHint($"{item.EnglishName}\nID {item.Id} · {categoryName}\n{string.Join('\n', item.Sources.Select(LootSourceName))}");
            ImGui.PushStyleColor(ImGuiCol.Text, Muted);
            ImGui.TextWrapped($"{categoryName} · " + (item.Available ? string.Join("／", item.Sources.Select(s => s.StartsWith("South") ? "南部" : "北部").Distinct()) : "目前客戶端無此物品資料 · 不處理"));
            ImGui.PopStyleColor();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(loot.Counts.GetValueOrDefault(item.Id).ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(!item.Available ? "—" : item.PriceLow == 0 ? "不可售出" : $"{item.PriceLow:N0}");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static string LootCategoryName(LootCategory category) => category switch
    {
        LootCategory.Minion => "寵物", LootCategory.Orchestrion => "樂譜", LootCategory.Mount => "坐騎",
        LootCategory.Equipment => "裝備", LootCategory.Appearance => "髮型／表情／配件", LootCategory.Dye => "染劑",
        LootCategory.Materia => "魔晶石", LootCategory.Furnishing => "家具", LootCategory.Consumable => "消耗品",
        LootCategory.FieldNote => "調查記錄", LootCategory.Card => "九宮幻卡", _ => "其他",
    };

    private static string LootSourceName(string source) => source.Replace("South Horn", "南部").Replace("North Horn", "北部")
        .Replace("Forked Tower Blood", "力之塔").Replace("Forked Tower Magic Extreme", "魔之塔（極）").Replace("Forked Tower Magic", "魔之塔")
        .Replace("Treasure Bronze", "野外銅箱").Replace("Treasure Silver", "野外銀箱")
        .Replace("Pot Bronze", "魔法罐銅箱").Replace("Pot Silver", "魔法罐銀箱").Replace("Pot Gold", "魔法罐金箱").Replace("Bunny Gold", "幸運兔金箱");
}
