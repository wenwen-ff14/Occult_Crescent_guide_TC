using CrescentCompass.Core;

namespace CrescentCompass;

public sealed partial class Plugin
{
    internal CeCooldownTracker CeCooldowns { get; } = new();
    private long lastCeError;

    private void UpdateCeCooldowns(long tick)
    {
        if (!Config.TrackCeCooldowns) { CeCooldowns.Suspend(); return; }
        try
        {
            if (CeReader.TryRead(out var observations))
                CeCooldowns.Update(Client.TerritoryType, Client.Instance, observations, DateTimeOffset.UtcNow);
            else CeCooldowns.Suspend();
        }
        catch (Exception error)
        {
            CeCooldowns.Suspend();
            if (tick - lastCeError > 10_000)
            { lastCeError = tick; Log.Warning(error, "CrescentCompass CE observation failed"); }
        }
    }

    internal void SetCeTracking(bool enabled)
    {
        Config.TrackCeCooldowns = enabled;
        CeCooldowns.Suspend();
        PluginInterface.SavePluginConfig(Config);
    }

    internal void ClearCeCooldowns() => CeCooldowns.Reset();
}
