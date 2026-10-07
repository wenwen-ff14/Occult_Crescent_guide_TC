using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private readonly CarrotTable carrotTable = new();
    internal Dictionary<string, Vector2> CarrotTargets { get; } = [];
    internal int CarrotRowsDrawn => carrotTable.RowsDrawn;

    private void DrawCarrotWeights(CompassViewState state, CompassActions actions)
    {
        CarrotTargets.Clear();
        if (state.Controls is not { RouteKind: PatrolRouteKind.Carrot } controls) return;
        DrawCarrotDisplayControls(state, actions);
        carrotTable.Draw(new(controls.ChartPoints, state.Carrots ?? new(0, "", false), state.Active, state.Route.FirstOrDefault()?.Spot.Id),
            new(spot => actions.Flag(spot), actions.ConfirmCarrotPickup, actions.ResetCarrotWeights, actions.DrawCarrotToolButton), U(220));
        foreach (var target in carrotTable.Targets) CarrotTargets[target.Key] = target.Value;
        ImGui.Spacing();
    }

    private void DrawCarrotDisplayControls(CompassViewState state, CompassActions actions)
    {
        var options = state.CarrotDisplay ?? new(false);
        var overlay = options.Overlay;
        ImGui.BeginDisabled(actions.CarrotDisplay is null);
        if (ImGui.Checkbox("在畫面顯示蘿蔔表格", ref overlay)) actions.CarrotDisplay?.SetOverlay(overlay);
        CarrotTargets["overlay"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        HoverHint("南部島內顯示獨立表格，可拖曳標題列；關閉不會停止本機權重追蹤。");
        ImGui.EndDisabled();
        ImGui.Spacing();
    }
}
