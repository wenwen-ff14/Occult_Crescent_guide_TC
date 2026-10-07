using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly CarrotOverlay carrotOverlay = new();
    private CarrotTableActions? carrotTableActions;
    private bool CanDrawCarrotDisplay => Active && Session.Territory == CarrotRoute.Territory && !GameGui.GameUiHidden && !Client.IsGPosing &&
        !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] && !Conditions[ConditionFlag.OccupiedInCutSceneEvent];

    internal CompassCarrotDisplayOptions CarrotDisplayOptions => new(Config.ShowCarrotOverlay);
    internal CompassCarrotState CarrotState => new(CarrotWeights.Pickups, CarrotDetail, CanConfirmCarrot, CarrotWeights.Revision);
    internal IReadOnlyList<CompassPoint> CarrotPoints() => Session.Snapshot().Where(s => CarrotRoute.Number(s.Spot) is not null)
        .Select(s => new CompassPoint(s.Spot, s.Status, MapPosition(s.Spot), Vector3.Distance(Position, s.Spot.Position), s.LastSeen,
            ChartNumber: CarrotRoute.Number(s.Spot), CarrotWeight: CarrotWeights.Weight(s.Spot.Id))).OrderBy(p => p.ChartNumber).ToArray();

    private void DrawCarrotOverlay()
    {
        var visible = Config.ShowCarrotOverlay && CanDrawCarrotDisplay;
        carrotTableActions ??= new(spot => Flag(spot), ConfirmCarrotPickup, ResetCarrotWeights, DrawCarrotToolButton);
        carrotOverlay.Draw(new(visible ? CarrotPoints() : [], CarrotState, Active, Remaining.FirstOrDefault()?.Id), carrotTableActions,
            visible, new(Config.CarrotOverlayX, Config.CarrotOverlayY), SaveCarrotOverlayPosition, () => SetCarrotOverlayVisible(false));
    }

    internal void SetCarrotOverlayVisible(bool enabled) { Config.ShowCarrotOverlay = enabled; PluginInterface.SavePluginConfig(Config); }
    private void SaveCarrotOverlayPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        Config.CarrotOverlayX = position.X; Config.CarrotOverlayY = position.Y;
        PluginInterface.SavePluginConfig(Config);
    }

    internal static bool DrawCarrotToolButton(CarrotTool tool, Vector2 size)
    {
        var icon = tool switch { CarrotTool.Flag => FontAwesomeIcon.Flag, CarrotTool.Collected => FontAwesomeIcon.Check,
            CarrotTool.Reset => FontAwesomeIcon.RedoAlt, _ => FontAwesomeIcon.Times };
        ImGui.PushFont(UiBuilder.IconFont);
        var pressed = ImGui.Button(icon.ToIconString() + "##" + tool, size);
        ImGui.PopFont();
        return pressed;
    }
}
