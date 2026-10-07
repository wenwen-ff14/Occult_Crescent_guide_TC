using CrescentCompass.Core;

internal static class LootChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var catalogStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "loot_catalog.json"));
        var catalog = LootCleanup.Load(catalogStream);
        check(catalog.Count == 289 && catalog.Select(i => i.Id).Distinct().Count() == 289, "Cleanup catalog retains audited items except three explicit exclusions");
        check(catalog.All(i => i.Id is not (47866 or 47868 or 48096)), "Excluded materials and fortune carrot are absent from cleanup");
        check(catalog.Single(i => i.Id == 8143).Sources.Length == 2, "Common bronze loot retains both island sources");
        check(catalog.Any(i => i.Id == 48000) && catalog.Any(i => i.Id == 52266), "Tower rewards included");
        check(catalog.All(i => i.Sources.Length > 0 && !i.EnglishName.StartsWith("Item ")), "Every catalog entry has provenance and a resolved name");
        var item = new LootItem(100, "物品", "Item", ["South Horn / Treasure Bronze"], true, 10, 1);
        var slot = new LootSlot(0, 2, 100, 99, false);
        foreach (var (id, expected) in new (uint, LootCategory)[] {
            (21058, LootCategory.Minion), (48204, LootCategory.Orchestrion), (52366, LootCategory.Orchestrion),
            (47979, LootCategory.Mount), (26782, LootCategory.Mount), (52266, LootCategory.Mount),
            (10387, LootCategory.Equipment), (47987, LootCategory.Appearance), (47983, LootCategory.Appearance),
            (48161, LootCategory.Appearance), (13114, LootCategory.Dye), (41757, LootCategory.Materia),
            (48140, LootCategory.Furnishing), (48157, LootCategory.Consumable), (47734, LootCategory.FieldNote),
            (51989, LootCategory.FieldNote), (48000, LootCategory.Card), (48736, LootCategory.Other) })
            check(LootCategories.Classify(catalog.Single(i => i.Id == id)) == expected, $"Loot category matches item {id}: {expected}");
        check(LootCategories.Classify(item with { SearchCategory = uint.MaxValue }) == LootCategory.Other, "Unknown categories remain visible under Other");
        var excluded = new uint[] { 47866, 47868, 48096 }.Select(id => item with { Id = id }).ToArray();
        using (var oldCatalog = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(excluded.Append(item))))
            check(LootCleanup.Load(oldCatalog).Select(i => i.Id).SequenceEqual(new[] { item.Id }), "Old or regenerated catalog cannot reintroduce excluded items");
        HashSet<uint> oldRules = [100, 47866, 47868, 48096];
        foreach (var removed in excluded)
        foreach (var mode in new[] { LootCleanupMode.Discard, LootCleanupMode.Sell })
            check(!LootCleanup.Eligible(slot with { ItemId = removed.Id }, removed, oldRules, mode), "Excluded item is protected even with stale rules and a sellable price");
        check(oldRules.RemoveWhere(LootCleanup.IsExcluded) == 3 && oldRules.SetEquals(new uint[] { 100 }), "Rule migration removes only the three excluded item IDs");
        HashSet<uint> garbage = [100];
        var dialog = new LootSaleDialog(123, 123, 123, 99, 99, true, false, false, 2, 0, 2, 100, 99);
        var shop = new LootShopState(false, 1, false, false, false, false, 0);
        foreach (var mode in new[] { 1, 2 })
        {
            var tab = shop with { Mode = mode };
            check(LootCleanup.CanStartSale(tab), "Both sale-list and buyback tabs permit selling inventory garbage without switching tabs");
            check(LootCleanup.CanStartSale(tab with { WaitingForSellConfirm = true }), "TC stale confirmation flag does not block next sale on either tab");
            foreach (var busy in new[] { tab with { Retainer = true }, tab with { Mode = 0 }, tab with { Mode = 3 },
                tab with { StartingBuy = true }, tab with { StartingSell = true }, tab with { WaitingForTransaction = true },
                tab with { SaleDialogId = 99 }, tab with { WaitingForSellConfirm = true, SaleDialogId = 99 } })
                check(!LootCleanup.CanStartSale(busy), "Both tabs retain transaction, dialog, retainer and unknown-mode guards");
        }
        check(LootCleanup.CanConfirmSale(slot, dialog), "Own full-stack sale can confirm");
        foreach (var wrong in new[] { dialog with { RequestHandler = 0 }, dialog with { ActiveHandler = 124 },
            dialog with { DialogHandler = 124 }, dialog with { DialogId = 0 }, dialog with { VisibleDialogId = 98 },
            dialog with { WaitingForConfirm = false }, dialog with { WaitingForTransaction = true }, dialog with { Retainer = true },
            dialog with { TransactionType = 1 }, dialog with { Bag = 1 }, dialog with { Slot = 3 },
            dialog with { ItemId = 101 }, dialog with { Quantity = 1 } })
            check(!LootCleanup.CanConfirmSale(slot, wrong), "Unrelated, buying, partial-stack or stale sale dialog never confirmed");
        check(!LootCleanup.Eligible(slot, item, new HashSet<uint>(), LootCleanupMode.Discard), "Default keep is never processed");
        check(LootCleanup.Eligible(slot, item, garbage, LootCleanupMode.Discard), "Explicit garbage can be discarded");
        check(LootCleanup.Eligible(slot, item, garbage, LootCleanupMode.Sell), "Explicit sellable garbage can be sold");
        foreach (var bag in new[] { -1, 4, 1000, 2000 })
            check(!LootCleanup.Eligible(slot with { Bag = bag }, item, garbage, LootCleanupMode.Discard), "Other inventories excluded");
        check(!LootCleanup.Eligible(slot with { Protected = true }, item, garbage, LootCleanupMode.Discard), "Protected item excluded");
        check(!LootCleanup.Eligible(slot with { ItemId = 101 }, item, garbage, LootCleanupMode.Discard), "Replaced item excluded");
        check(!LootCleanup.Eligible(slot, null, garbage, LootCleanupMode.Discard), "Non-catalog item excluded");
        check(!LootCleanup.Eligible(slot, item with { Available = false }, garbage, LootCleanupMode.Discard), "Unavailable item excluded");
        check(!LootCleanup.Eligible(slot, item with { PriceLow = 0 }, garbage, LootCleanupMode.Sell), "Unsellable never falls back to discard");
        check(!LootCleanup.Eligible(slot, item, garbage, (LootCleanupMode)99), "Invalid mode excluded");
        check(!LootCleanup.Eligible(slot with { Quantity = 0 }, item, garbage, LootCleanupMode.Discard), "Empty stack excluded");
        var tracker = new LootCleanupTracker();
        tracker.Start(slot, 1000); tracker.Observe([slot], 2000);
        check(tracker.Pending == slot && tracker.Completed == 0, "Unchanged inventory keeps request pending");
        tracker.Observe([slot], 9000);
        check(tracker.Faulted && tracker.Pending == null && tracker.Completed == 0, "Timeout stops without retry or success");
        tracker.Reset(); tracker.Start(slot, 1000); tracker.Observe([slot with { Quantity = 98 }], 2000);
        check(tracker.Completed == 1 && tracker.Pending == null && !tracker.Faulted, "Decreased stack acknowledges operation");
        tracker.Start(slot, 3000); tracker.Observe([], 4000);
        check(tracker.Completed == 2, "Removed stack acknowledges operation");
        tracker.Reset(); tracker.Start(slot, 1000); tracker.MarkConfirmationSent(7000);
        tracker.Observe([slot], 10000);
        check(tracker.ConfirmationSent && !tracker.Faulted && tracker.Pending == slot, "Sale confirmation gets a server-response timeout");
        tracker.MarkConfirmationSent(14000); tracker.Observe([slot], 15000);
        check(tracker.Faulted, "Repeated confirmation cannot indefinitely extend timeout");
        tracker.Reset(); tracker.Start(slot, 1000); tracker.MarkConfirmationSent(2000); tracker.Observe([], 3000);
        tracker.Start(slot with { Slot = 3 }, 4000);
        check(tracker.Pending?.Slot == 3 && !tracker.ConfirmationSent && tracker.Completed == 1, "Next stack automatically has fresh confirmation state");
        tracker.Reset();
        var secondItem = item with { Id = 101 };
        var secondSlot = slot with { Slot = 3, ItemId = 101, Quantity = 12 };
        List<LootSlot> bagContents = [slot, secondSlot];
        HashSet<uint> bothGarbage = [100, 101];
        var items = new[] { item, secondItem }.ToDictionary(i => i.Id);
        // Re-scan after each server update: distinct item types must continue without a restart.
        foreach (var expected in new[] { slot, secondSlot })
        {
            var next = bagContents.FirstOrDefault(s => LootCleanup.Eligible(s, items.GetValueOrDefault(s.ItemId), bothGarbage, LootCleanupMode.Sell));
            check(next == expected && tracker.Pending is null && !tracker.Faulted, "Next distinct garbage item remains eligible after completed sale");
            check(LootCleanup.CanStartSale(shop), "Sale continues after automatic switch to buyback with stale confirmation flag");
            tracker.Start(next!, 1000);
            shop = shop with { WaitingForSellConfirm = true, SaleDialogId = 99 };
            check(LootCleanup.CanConfirmSale(next!, dialog with { Slot = next!.Slot, ItemId = next.ItemId, Quantity = next.Quantity }), "Each item uses its own sale confirmation");
            tracker.MarkConfirmationSent(2000);
            // Native callback clears the dialog ID, and server completion clears transaction busy.
            // Neither clears WaitingForSellConfirm; leave that flag true for the next iteration.
            shop = shop with { SaleDialogId = 0, WaitingForTransaction = true };
            check(!LootCleanup.CanStartSale(shop), "Closing dialog alone cannot dispatch next item while server transaction is pending");
            bagContents.Remove(next!);
            tracker.Observe(bagContents, 3000);
            shop = shop with { WaitingForTransaction = false, Mode = 2 };
        }
        check(tracker.Completed == 2 && tracker.Pending is null && !tracker.Faulted, "Two distinct sales complete without resetting tracker");
    }
}
