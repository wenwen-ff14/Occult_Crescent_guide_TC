using CrescentCompass.Core;
using Lumina.Excel.Sheets;

namespace CrescentCompass;

internal static class ExplorationReader
{
    // Use the TC API 13 services, not offsets or UI text. Its MKDLore query maps the TC Unknown2 unlock link.
    internal static bool TryRead(IReadOnlyList<Spot> points, out Dictionary<uint, bool> snapshot)
    {
        snapshot = [];
        if (!Plugin.PlayerState.IsLoaded || Plugin.Objects.LocalPlayer is not { } player) return false;
        var contentId = Plugin.PlayerState.ContentId;
        if (contentId == 0 || Plugin.PlayerState.EntityId != player.EntityId) return false;
        var sheet = Plugin.Data.GetExcelSheet<MKDLore>();
        foreach (var spot in points)
        {
            if (spot.LoreId == 0 || !sheet.TryGetRow(spot.LoreId, out var row) || row.Unknown2 == 0) return false;
            snapshot.Add(spot.LoreId, Plugin.UnlockState.IsMKDLoreUnlocked(row));
        }
        return Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ContentId == contentId && Plugin.Objects.LocalPlayer?.EntityId == player.EntityId;
    }
}
