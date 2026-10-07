using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Game.ClientState.Conditions;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly PatrolOverlay patrolOverlay = new();
    private CompassPatrolOverlayCommands? patrolOverlayCommands;
    private bool HasPatrolOverlayRoute => Remaining.Count > 0 || IsPlanning;

    private void DrawPatrolOverlay()
    {
        var visible = Config.ShowPatrolOverlay && Active && !GameGui.GameUiHidden && !Client.IsGPosing &&
            !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] &&
            !Conditions[ConditionFlag.OccupiedInCutSceneEvent] &&
            HasPatrolOverlayRoute;
        patrolOverlayCommands ??= new(AdvanceFromPatrolOverlay, PauseFromPatrolOverlay, ResumeFromPatrolOverlay, StopFromPatrolOverlay);
        patrolOverlay.Draw(visible ? PatrolOverlayState() : null, visible,
            new(Config.PatrolOverlayX, Config.PatrolOverlayY), SavePatrolOverlayPosition, patrolOverlayCommands);
    }

    private CompassPatrolOverlayState PatrolOverlayState()
    {
        var next = Remaining.FirstOrDefault();
        if (next is not null) next = Session.Get(next.Id)?.Spot ?? next;
        var total = Route.Stops.Count;
        var completed = Route.Stops.Count(RouteStopCompleted);
        var skipped = Route.Stops.Count(s => Session.Get(s.Id)?.Status == SpotStatus.Skipped);
        var finished = total > 0 && completed + skipped >= total && next is null;
        var waiting = IsPaused || IsPlanning || EventNavigationActive;
        var status = IsPaused ? "已暫停" : PotNavigationActive ? "魔法罐優先" : FateNavigationActive ? "FATE 優先" :
            IsPlanning ? "規劃路線中" : autoPatrol.Enabled ? "自動巡查" : next is not null ? "巡查中" : finished ? "本輪巡查結束" : total > 0 ? "等待可用站點" : "尚未開始";
        var target = next is null ? "目前沒有下一站" : $"{(waiting ? "保留站點" : "下一站")} {((CarrotRoute.Number(next) ?? ChestChart.Number(next)) is { } number ? $"#{number:00} · " : "")}{CompassView.KindName(next.Kind)}";
        var distance = next is null ? float.NaN : Vector3.Distance(Position, next.Position);
        var coordinates = next is null ? IsPlanning ? "等待路線計算完成。" : "在巡查頁選擇起點或規劃路線。" :
            $"{MapPosition(next)}{(float.IsFinite(distance) ? $" · 直線 {distance:F0} m" : "")}";
        var detail = autoPatrol.Enabled || autoPatrol.Faulted ? autoPatrol.Detail : next is not null || waiting ? AutomationDetail : finished ? "可在巡查頁開始下一輪；巡查紀錄保留。" :
            "啟動巡查後自動更新；關閉主介面也會顯示。";
        return new(status, target, coordinates, completed, skipped, Remaining.Count, total, detail, waiting, IsPaused, IsPlanning, IsOccupied);
    }

    private bool CanControlPatrolOverlay => Config.ShowPatrolOverlay && Active && HasPatrolOverlayRoute;

    private void AdvanceFromPatrolOverlay()
    {
        if (CanControlPatrolOverlay && Remaining.Count > 0 && !IsPaused && !IsPlanning && !EventNavigationActive && !IsOccupied) Next();
    }

    private void PauseFromPatrolOverlay()
    {
        if (CanControlPatrolOverlay) PauseRoute();
    }

    private void ResumeFromPatrolOverlay()
    {
        if (CanControlPatrolOverlay) ResumeRoute();
    }

    private void StopFromPatrolOverlay()
    {
        if (CanControlPatrolOverlay) StopRoute();
    }

    internal void SetPatrolOverlayVisible(bool enabled)
    {
        Config.ShowPatrolOverlay = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    private void SavePatrolOverlayPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
        Config.PatrolOverlayX = position.X;
        Config.PatrolOverlayY = position.Y;
        PluginInterface.SavePluginConfig(Config);
    }
}
