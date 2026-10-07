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
        var snapshot = PotFates.Snapshot(now);
        var canFlagNext = Active && snapshot.Next is { } next && next.Territory == Client.TerritoryType &&
            PotFateLocation(next.Id) is not null;
        potCountdownOverlay.Draw(snapshot, now, visible,
            new(Config.PotCountdownOverlayX, Config.PotCountdownOverlayY), SavePotOverlayPosition, canFlagNext, FlagNextPotFateLocation);
    }

    private void FlagNextPotFateLocation(ushort expectedId)
    {
        var snapshot = PotFates.Snapshot(DateTimeOffset.UtcNow);
        if (!Active || snapshot.ExpectedAt is null || snapshot.Next is not { } next ||
            next.Id != expectedId || next.Territory != Client.TerritoryType) return;
        FlagPotFateLocation(next.Id);
    }

    internal void SetPotOverlayVisible(bool enabled)
    {
        Config.ShowPotCountdownOverlay = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    private void SavePotOverlayPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        Config.PotCountdownOverlayX = position.X;
        Config.PotCountdownOverlayY = position.Y;
        PluginInterface.SavePluginConfig(Config);
    }
}
