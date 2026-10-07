using System.Text.Json;

namespace CrescentCompass.Core;

public enum LootCleanupMode { Discard, Sell }
public sealed record LootItem(uint Id, string Name, string EnglishName, string[] Sources, bool Available, uint PriceLow, uint SearchCategory);
public sealed record LootSlot(int Bag, int Slot, uint ItemId, int Quantity, bool Protected);
public sealed record LootSaleDialog(nint RequestHandler, nint ActiveHandler, nint DialogHandler,
    uint DialogId, uint VisibleDialogId, bool WaitingForConfirm, bool WaitingForTransaction,
    bool Retainer, int TransactionType, int Bag, int Slot, uint ItemId, int Quantity);
public sealed record LootShopState(bool Retainer, int Mode, bool StartingBuy, bool StartingSell,
    bool WaitingForSellConfirm, bool WaitingForTransaction, uint SaleDialogId);

public static class LootCleanup
{
    // Excluded from both the catalog and execution, including previously saved garbage rules.
    public static bool IsExcluded(uint id) => id is 47866 or 47868 or 48096;

    // On the TC client WaitingForSellConfirm remains true AFTER the sale dialog closes.
    // Only the next sale preparation / shop UI event clears it. Treating it as busy
    // prevents that next sale forever; the live proxy's AddonId is the dialog lifetime.
    // Inventory selling accepts both the sale list (1) and buyback list (2).
    // The native sale takes a bag/slot, not a row from the currently selected shop tab.
    public static bool CanStartSale(LootShopState shop) => !shop.Retainer && shop.Mode is 1 or 2 &&
        !shop.StartingBuy && !shop.StartingSell && !shop.WaitingForTransaction && shop.SaleDialogId == 0;

    public static bool CanConfirmSale(LootSlot pending, LootSaleDialog dialog) =>
        dialog.RequestHandler != 0 && dialog.RequestHandler == dialog.ActiveHandler && dialog.ActiveHandler == dialog.DialogHandler &&
        dialog.DialogId != 0 && dialog.DialogId == dialog.VisibleDialogId && dialog.WaitingForConfirm &&
        !dialog.WaitingForTransaction && !dialog.Retainer && dialog.TransactionType == 2 &&
        dialog.Bag == pending.Bag && dialog.Slot == pending.Slot && dialog.ItemId == pending.ItemId && dialog.Quantity == pending.Quantity;

    public static IReadOnlyList<LootItem> Load(Stream stream)
    {
        var items = JsonSerializer.Deserialize<LootItem[]>(stream) ?? [];
        if (items.Any(i => i.Id == 0 || string.IsNullOrWhiteSpace(i.Name)) || items.Select(i => i.Id).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid loot catalog");
        return items.Where(i => !IsExcluded(i.Id)).ToArray();
    }

    // Only ordinary bags; never armory, equipped gear, key items, currencies, saddlebags or retainers.
    public static bool Eligible(LootSlot slot, LootItem? item, IReadOnlySet<uint> garbage, LootCleanupMode mode) =>
        Enum.IsDefined(mode) && slot.Bag is >= 0 and <= 3 && slot.Slot is >= 0 and < 35 &&
        slot.Quantity > 0 && !slot.Protected && !IsExcluded(slot.ItemId) && item is { Available: true } && item.Id == slot.ItemId &&
        garbage.Contains(item.Id) && (mode != LootCleanupMode.Sell || item.PriceLow > 0);
}

/// <summary>One outstanding request; never retry a timed-out destructive operation.</summary>
public sealed class LootCleanupTracker
{
    public LootSlot? Pending { get; private set; }
    private long started;
    public bool Faulted { get; private set; }
    public int Completed { get; private set; }
    public bool ConfirmationSent { get; private set; }
    public void Start(LootSlot slot, long now) { Pending = slot; started = now; ConfirmationSent = false; }
    public void MarkConfirmationSent(long now)
    {
        if (Pending is null || ConfirmationSent || Faulted) return;
        ConfirmationSent = true;
        started = now;
    }
    public void Reset() { Pending = null; Faulted = false; Completed = 0; ConfirmationSent = false; }
    public void Observe(IReadOnlyList<LootSlot> slots, long now)
    {
        if (Pending is not { } pending) return;
        var current = slots.FirstOrDefault(s => s.Bag == pending.Bag && s.Slot == pending.Slot);
        if (current is null || current.ItemId != pending.ItemId || current.Quantity < pending.Quantity)
        { Pending = null; Completed++; }
        else if (now - started >= 8000) { Pending = null; Faulted = true; }
    }
}
