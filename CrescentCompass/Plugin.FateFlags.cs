using CrescentCompass.Core;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CrescentCompass;

public sealed partial class Plugin
{
    internal FateAutoFlagger FateFlags { get; } = new();
    internal bool FateNavigationActive => Config.AutoFlagFates && !PotNavigationActive && FateFlags.OwnsNavigation(DateTimeOffset.UtcNow);
    private bool EventNavigationActive => PotNavigationActive || FateNavigationActive;
    private bool fateNavigationWasActive;

    private void UpdateFateNavigation()
    {
        FateFlags.Tick(DateTimeOffset.UtcNow, Config.AutoFlagFates, IsOccupied, PotNavigationActive, TryFlagFate);
        var active = FateNavigationActive;
        if (fateNavigationWasActive && !active && Config.AutoAdvanceChests && Remaining.FirstOrDefault() is { } next)
            routeAutomation.ScheduleFlag(next.Id);
        fateNavigationWasActive = active;
    }

    internal void ReleaseFateNavigation()
    {
        FateFlags.Release(); fateNavigationWasActive = false;
        if (Config.AutoAdvanceChests && Remaining.FirstOrDefault() is { } next) routeAutomation.ScheduleFlag(next.Id);
    }

    internal void SetFateAutoFlag(bool enabled)
    {
        Config.AutoFlagFates = enabled;
        if (!enabled) ReleaseFateNavigation();
        PluginInterface.SavePluginConfig(Config);
    }

    private bool TryFlagFate(FateFlagTarget fate)
    {
        if (!Active || Client.MapId == 0 || !Coordinates.IsFinite(fate.Position) ||
            !FateFlags.Active(DateTimeOffset.UtcNow).Any(f => f.Id == fate.Id && f.Occurrence == fate.Occurrence)) return false;
        var ok = GameGui.OpenMapWithMapLink(new MapLinkPayload(Client.TerritoryType, Client.MapId,
            (int)MathF.Round(fate.Position.X * 1000), (int)MathF.Round(fate.Position.Z * 1000)));
        if (ok) { routeAutomation.CancelFlag(); Message = $"FATE 旗標：{fate.Name}"; }
        return ok;
    }

    internal void FlagGeneralFate(ushort id)
    {
        if (FateFlags.Active(DateTimeOffset.UtcNow).FirstOrDefault(f => f.Id == id) is not { } fate || !TryFlagFate(fate))
            Message = "FATE 地點目前無法確認，請等候更新。";
        else { FateFlags.HoldManual(fate, DateTimeOffset.UtcNow); fateNavigationWasActive = FateNavigationActive; }
    }
}
