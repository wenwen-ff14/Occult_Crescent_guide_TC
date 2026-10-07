using System.Numerics;
using CrescentCompass.Ui;
using Dalamud.Game.ClientState.Conditions;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly PhantomJobOverlay phantomJobOverlay = new();

    private void DrawPhantomJobOverlay()
    {
        var visible = Config.ShowPhantomJobOverlay && Active && !GameGui.GameUiHidden && !Client.IsGPosing &&
            !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] &&
            !Conditions[ConditionFlag.OccupiedInCutSceneEvent];
        phantomJobOverlay.Draw(PhantomJobState, visible, new(Config.PhantomJobOverlayX, Config.PhantomJobOverlayY),
            SavePhantomOverlayPosition, () => SetPhantomOverlayVisible(false), SwitchPhantomJob, DrawPhantomJobIcon);
    }

    internal void SetPhantomOverlayVisible(bool enabled)
    {
        Config.ShowPhantomJobOverlay = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    private void SavePhantomOverlayPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        Config.PhantomJobOverlayX = position.X;
        Config.PhantomJobOverlayY = position.Y;
        PluginInterface.SavePluginConfig(Config);
    }
}
