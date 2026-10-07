using System.Reflection;
using System.Runtime.InteropServices;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace CrescentCompass;

public sealed partial class Plugin
{
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    private IReadOnlyList<LootItem> lootItems = [];
    private Dictionary<uint, LootItem> lootById = [];
    private IReadOnlyList<LootSlot> lootSlots = [];
    private readonly LootCleanupTracker lootTracker = new();
    private bool lootArmed;
    private long lastLootScan;
    private ulong lootCharacter;
    private string lootDetail = "未啟動；先勾選要丟棄的物品，再選擇處理方式。";
    private delegate void SellLootDelegate(uint slot, InventoryType bag, uint flags);
    private SellLootDelegate? sellLoot;
    private bool sellLookupAttempted;
    private nint lootSaleHandler;
    private nint lootYesNoProxy;
    private const string SellLootSignature = "48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B F2 8B E9";
    // ShopEventHandler::ShowYesNo passes its own YesNoProxy to the dialog creator.
    private const string SaleYesNoProxySignature = "48 8D 0D ?? ?? ?? ?? 48 89 4C 24 20 48 8B CF E8";

    private void InitializeLoot()
    {
        Config.GarbageItemIds ??= [];
        if (Config.GarbageItemIds.RemoveWhere(LootCleanup.IsExcluded) > 0) PluginInterface.SavePluginConfig(Config);
        if (!Enum.IsDefined(Config.LootMode)) Config.LootMode = LootCleanupMode.Discard;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CrescentCompass.Data.loot_catalog.json")!;
        lootItems = LootCleanup.Load(stream).Select(item =>
        {
            var found = Data.GetExcelSheet<Item>().TryGetRow(item.Id, out var row) && !string.IsNullOrWhiteSpace(row.Name.ToString());
            return item with { Available = found, Name = found ? row.Name.ToString() : item.EnglishName,
                PriceLow = found ? row.PriceLow : 0, SearchCategory = found ? row.ItemSearchCategory.RowId : item.SearchCategory };
        }).ToArray();
        lootById = lootItems.ToDictionary(i => i.Id);
    }

    internal CompassLootState LootState => new(lootItems, Config.GarbageItemIds,
        lootSlots.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity)),
        Config.LootMode, lootArmed, lootDetail, lootTracker.Completed);

    internal void SetLootKeep(uint id, bool keep)
    {
        SetLootArmed(false);
        if (keep) Config.GarbageItemIds.Remove(id);
        else if (lootById.TryGetValue(id, out var item) && item.Available) Config.GarbageItemIds.Add(id);
        PluginInterface.SavePluginConfig(Config);
    }
    internal void SetLootMode(LootCleanupMode mode)
    {
        if (!Enum.IsDefined(mode)) return;
        SetLootArmed(false); Config.LootMode = mode; PluginInterface.SavePluginConfig(Config);
    }
    internal void SetLootArmed(bool value)
    {
        // Changing rules or mode always pauses; an in-flight server request cannot be undone.
        if (value && lootTracker.Pending is not null) { lootDetail = "上一筆請求尚未完成，請稍候再啟動。"; return; }
        lootArmed = value && Client.IsLoggedIn && PlayerState.IsLoaded;
        if (lootArmed) { lootTracker.Reset(); lootCharacter = PlayerState.ContentId; }
        lootDetail = lootArmed ? "已啟動，等待安全的背包／商店狀態。" : "已停止。";
    }

    private unsafe bool TryReadLoot(out List<LootSlot> slots)
    {
        slots = [];
        var inventory = InventoryManager.Instance();
        if (inventory == null) return false;
        for (var bag = 0; bag < 4; bag++)
        {
            var container = inventory->GetInventoryContainer((InventoryType)bag);
            if (container == null || !container->IsLoaded || container->Size != 35) return false;
            for (var index = 0; index < container->Size; index++)
            {
                var item = container->GetInventorySlot(index);
                if (item == null || item->ItemId == 0) continue;
                var protect = item->IsSymbolic || item->Flags != 0 || item->SpiritbondOrCollectability != 0 || item->GlamourId != 0;
                for (var m = 0; m < 5; m++) protect |= item->Materia[m] != 0;
                protect |= item->Stains[0] != 0 || item->Stains[1] != 0;
                slots.Add(new(bag, index, item->ItemId, item->Quantity, protect));
            }
        }
        return true;
    }

    private unsafe bool LootAddonVisible(string name)
    {
        var addon = (AtkUnitBase*)GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible;
    }

    private unsafe void ContinueLootSale(LootSlot pending, List<LootSlot> slots, long now)
    {
        if (Config.LootMode != LootCleanupMode.Sell) return;
        var shop = AgentShop.Instance();
        var proxy = ShopEventHandler.AgentProxy.Instance();
        if (shop == null || !shop->IsAgentActive() || !LootAddonVisible("Shop") || proxy == null ||
            shop->EventReceiver != &proxy->AtkEventInterface || (nint)proxy->Handler != lootSaleHandler)
        { lootArmed = false; lootDetail = "商店已關閉或切換，停止本次自動售出。"; return; }
        if (lootTracker.ConfirmationSent || lootYesNoProxy == 0) return;
        var handler = proxy->Handler;
        if (handler == null) return;
        var owner = (ShopEventHandler.YesNoProxy*)lootYesNoProxy;
        var addon = (AtkUnitBase*)GameGui.GetAddonByName("SelectYesno").Address;
        if (addon == null || !addon->IsVisible || !slots.Contains(pending) ||
            !LootCleanup.Eligible(pending, lootById.GetValueOrDefault(pending.ItemId), Config.GarbageItemIds, LootCleanupMode.Sell)) return;
        var dialog = new LootSaleDialog(lootSaleHandler, (nint)handler, (nint)owner->Handler, owner->AddonId, addon->Id,
            handler->WaitingForSellConfirm, handler->WaitingForTransactionToFinish, handler->IsTradingWithRetainer,
            handler->TransactionType, (int)handler->SellInventoryType, handler->SellInventorySlot,
            handler->TransactionItemId, handler->TransactionItemCount);
        if (!LootCleanup.CanConfirmSale(pending, dialog)) return;
        // Only the pending full-stack sale's exact dialog; never confirm a generic/unrelated prompt.
        lootTracker.MarkConfirmationSent(now);
        addon->FireCallbackInt(0);
        lootDetail = $"已確認售出：{lootById[pending.ItemId].Name} × {pending.Quantity}；等待背包更新。";
    }

    private unsafe void UpdateLoot()
    {
        var now = Environment.TickCount64;
        if (disposed || now - lastLootScan < 750) return;
        lastLootScan = now;
        try
        {
            if (!Client.IsLoggedIn || !PlayerState.IsLoaded || Objects.LocalPlayer is null ||
                (lootArmed && lootCharacter != PlayerState.ContentId))
            { lootSlots = []; if (lootArmed) SetLootArmed(false); return; }
            if (IsLoading) { if (lootArmed) lootDetail = "轉場中暫停。"; return; }
            if (!TryReadLoot(out var slots)) { lootSlots = []; return; }
            lootSlots = slots;
            lootTracker.Observe(slots, now);
            if (!lootArmed) return;
            if (lootTracker.Faulted) { lootArmed = false; lootDetail = "8 秒內未確認背包變更，已停止；請檢查遊戲提示後重新啟動。"; return; }
            if (Conditions[ConditionFlag.InCombat] || Conditions[ConditionFlag.Mounted] || Objects.LocalPlayer.IsDead || Client.IsGPosing ||
                Conditions[ConditionFlag.WatchingCutscene] || Conditions[ConditionFlag.WatchingCutscene78] || Conditions[ConditionFlag.OccupiedInCutSceneEvent] ||
                Conditions[ConditionFlag.TradeOpen] || Conditions[ConditionFlag.Crafting] || Conditions[ConditionFlag.Gathering])
            { lootDetail = "角色忙碌或有確認視窗，暫停處理。"; return; }
            if (lootTracker.Pending is { } pending)
            {
                lootDetail = "等待目前交易完成，完成後自動接續下一疊。";
                ContinueLootSale(pending, slots, now);
                return;
            }
            if (LootAddonVisible("SelectYesno") || LootAddonVisible("InputNumeric") || LootAddonVisible("ContextMenu"))
            { lootDetail = "有其他操作視窗，暫停處理。"; return; }
            var shop = AgentShop.Instance();
            var shopOpen = shop != null && shop->IsAgentActive() && LootAddonVisible("Shop");
            if (Config.LootMode == LootCleanupMode.Discard && (!SpotCatalog.IsSupported(Client.TerritoryType) || shopOpen || AutoChestContext.Occupied))
            { lootDetail = "自動丟棄僅在新月島閒置時執行。"; return; }
            if (Config.LootMode == LootCleanupMode.Sell)
            {
                if (!shopOpen) { lootDetail = "請開啟一般 NPC 商店；開啟後自動售出已標記垃圾。"; return; }
                var proxy = ShopEventHandler.AgentProxy.Instance();
                if (proxy == null || shop->EventReceiver != &proxy->AtkEventInterface || proxy->Handler == null)
                { lootDetail = "等待一般 NPC 商店完成載入。"; return; }
                var handler = proxy->Handler;
                if (!sellLookupAttempted)
                {
                    sellLookupAttempted = true;
                    if (SigScanner.TryScanText(SellLootSignature, out var address)) sellLoot = Marshal.GetDelegateForFunctionPointer<SellLootDelegate>(address);
                    SigScanner.TryGetStaticAddressFromSig(SaleYesNoProxySignature, out lootYesNoProxy);
                }
                if (sellLoot is null || lootYesNoProxy == 0) { lootArmed = false; lootDetail = "此客戶端的售出介面不相容，已停止。"; return; }
                var saleDialog = (ShopEventHandler.YesNoProxy*)lootYesNoProxy;
                var shopState = new LootShopState(handler->IsTradingWithRetainer, handler->CurrentMode,
                    handler->StartingBuy, handler->StartingSell, handler->WaitingForSellConfirm,
                    handler->WaitingForTransactionToFinish, saleDialog->AddonId);
                if (!LootCleanup.CanStartSale(shopState))
                {
                    lootDetail = shopState.Retainer ? "請改用一般 NPC 商店；不處理雇員交易。" :
                        shopState.Mode is not (1 or 2) ? "等待 NPC 商店的出售／回購列表完成載入。" :
                        shopState.WaitingForTransaction ? "等待上一筆交易回應，完成後自動接續。" :
                        shopState.SaleDialogId != 0 ? "等待遊戲售出確認視窗結束。" : "等待商店載入交易物品，完成後自動接續。";
                    return;
                }
                lootSaleHandler = (nint)handler;
            }
            var candidate = slots.FirstOrDefault(s => LootCleanup.Eligible(s, lootById.GetValueOrDefault(s.ItemId), Config.GarbageItemIds, Config.LootMode));
            if (candidate is null) { lootDetail = "沒有可處理的垃圾；持續等待新物品。HQ、收藏品、染色／投影／鑲嵌及精煉裝備保留。"; return; }
            // Re-read the exact slot immediately before the destructive call, with no queued stale pointers.
            if (!TryReadLoot(out var fresh) || !fresh.Contains(candidate) || !LootCleanup.Eligible(candidate, lootById.GetValueOrDefault(candidate.ItemId), Config.GarbageItemIds, Config.LootMode)) return;
            lootTracker.Start(candidate, now);
            if (Config.LootMode == LootCleanupMode.Discard) InventoryManager.Instance()->DiscardItem((InventoryType)candidate.Bag, (ushort)candidate.Slot);
            else sellLoot!((uint)candidate.Slot, (InventoryType)candidate.Bag, 0);
            lootDetail = $"已送出{(Config.LootMode == LootCleanupMode.Discard ? "丟棄" : "售出")}請求：{lootById[candidate.ItemId].Name} × {candidate.Quantity}";
            Log.Information(lootDetail);
        }
        catch (Exception error) { lootArmed = false; lootDetail = "背包處理失敗，已停止；請查看 Dalamud 記錄。"; Log.Error(error, "Loot cleanup failed"); }
    }
}
