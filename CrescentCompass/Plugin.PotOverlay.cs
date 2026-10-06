using System.Numerics;
using CrescentCompass.Ui;
using Dalamud.Game.ClientState.Conditions;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly PotCountdownOverlay potCountdownOverlay = new();

    private void DrawPotCountdownOverlay()
    {
        var visible = Config.ShowPotCountdownOverlay && Active && !GameGui.GameUiHidden && !Client.IsGPosing &&
            !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] &&
            !Conditions[ConditionFlag.OccupiedInCutSceneEvent];
        var now = DateTimeOffset.UtcNow;
        potCountdownOverlay.Draw(PotFates.Snapshot(now), now, visible, Config.LockPotCountdownOverlay,
            new(Config.PotCountdownOverlayX, Config.PotCountdownOverlayY), SavePotOverlayPosition);
    }

    internal void SetPotOverlayVisible(bool enabled)
    {
        Config.ShowPotCountdownOverlay = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void SetPotOverlayLocked(bool locked)
    {
        Config.LockPotCountdownOverlay = locked;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void ResetPotOverlayPosition() => SavePotOverlayPosition(new(24, 160));

    private void SavePotOverlayPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        Config.PotCountdownOverlayX = position.X;
        Config.PotCountdownOverlayY = position.Y;
        PluginInterface.SavePluginConfig(Config);
    }
}
