using CrescentCompass.Core;
using Dalamud.Game.Text.SeStringHandling.Payloads;

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

    internal static ulong GetGameTexture(string path) => Textures.GetFromGame(path).GetWrapOrDefault()?.Handle.Handle ?? 0;

    internal void FlagCeLocation(ushort id, bool trigger)
    {
        if (!Active || Client.TerritoryType != CeCooldownTracker.Territory || CeMapCatalog.FlagPosition(id, trigger) is not { } position)
        { Message = "CE 旗標：請先進入新月島南部並等待載入完成。"; return; }
        var link = new MapLinkPayload(CeCooldownTracker.Territory, CeMapCatalog.MapId,
            (int)MathF.Round(position.X * 1000), (int)MathF.Round(position.Y * 1000));
        var ok = GameGui.OpenMapWithMapLink(link);
        var name = trigger ? CeCooldownTracker.Find(id)?.TriggerMob : CeMapCatalog.Find(id)?.BossName;
        var map = CeMapCatalog.ToMap(position);
        Message = ok ? $"CE 旗標：{name} · X {map.X:F1} / Y {map.Y:F1}" : "CE 旗標：地圖未成功開啟，請重試。";
    }
}
