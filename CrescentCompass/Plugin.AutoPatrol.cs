using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly AutoChestPatrol autoPatrol = new(new ChestPatrolNavigation());
    internal CompassAutoPatrolState AutoPatrolState => new(autoPatrol.Enabled,
        Active && Session.Territory == ChestChart.Territory && (Config.PatrolRoute != PatrolRouteKind.Bocchi || BocchiRouteAvailable),
        $"{autoPatrol.Detail} · 快取 {GroundNavigation.PatrolPaths.Count} 段" + (CarrotMode ? $" · {(AutoChestContext.Mounted ? "蘿蔔互動需要下坐騎" : carrotGathering.Detail)}" : ""));
    private bool PatrolAutoOpen => autoPatrol.Enabled && !IsPaused && !IsPlanning && !EventNavigationActive &&
        Active && Environment.TickCount64 - lastScan <= 1500;
    private bool AutoChestEnabled => (Config.AutoOpenNearbyChests || PatrolAutoOpen) &&
        (!autoPatrol.Enabled || autoPatrol.CanOpenChest);

    internal void SetAutoPatrol(bool enabled)
    {
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            if (!enabled) { autoPatrol.Stop(); Message = autoPatrol.Detail; return; }
            if (!Active || Session.Territory != ChestChart.Territory)
            { Message = "自動巡查僅支援新月島南部。"; autoPatrol.Stop(Message); return; }
            if (Config.PatrolRoute == PatrolRouteKind.Bocchi && !BocchiRouteAvailable)
            { Message = "BOCCHI 路線資料不可用；請切換原圖表順序。"; autoPatrol.Stop(Message); return; }
            if (GroundNavigation.Status() != "地形導航已就緒")
            { Message = GroundNavigation.Status(); autoPatrol.Stop(Message); return; }
            if (Remaining.Count == 0 && !IsPlanning) StartChartRoute(PatrolStartNumber, lazy: true);
            // Ordered patrol does not need a full-island planning barrier before its first move.
            if (activeRequest is { Ordered: true } request) SetLazyPatrolRoute(request.Points);
            if (IsPaused)
            {
                routeAutomation.SetPaused(false);
                if (pendingResume is { } resume) SetLazyPatrolRoute(resume.Points);
            }
            if (Remaining.Count == 0 && !IsPlanning) { autoPatrol.Stop("目前沒有待巡查的箱點。"); Message = autoPatrol.Detail; return; }
            autoChestFaulted = false; autoChestOpener.Reset();
            autoPatrol.Start();
            UpdateAutoPatrol(Environment.TickCount64);
            Message = autoPatrol.Detail;
        });
    }

    private void SuspendAutoPatrol(string reason) => _ = Framework.RunOnFrameworkThread(() => { if (autoPatrol.Enabled) autoPatrol.Suspend(reason); });
    private void StopAutoPatrol(string reason) => _ = Framework.RunOnFrameworkThread(() => autoPatrol.Stop(reason));

    private Observation? PatrolChest(IReadOnlyList<Observation> observations) => Remaining.FirstOrDefault() is not { } head ? null :
        CarrotMode ? carrotGathering.Bunny ?? carrotGathering.LiveCarrot(head, observations) :
        observations.Where(o => CofferKinds.IsCoffer(o.Kind) && o.Available && (o.Targetable || o.Opening) &&
            Coordinates.IsFinite(o.Position) && Vector3.DistanceSquared(o.Position, head.Position) <= 16)
        .OrderBy(o => Vector3.DistanceSquared(o.Position, head.Position)).FirstOrDefault();

    private void UpdateAutoPatrol(long now)
    {
        var enabled = autoPatrol.Enabled;
        var chest = PatrolChest(lastObservations);
        var head = Remaining.FirstOrDefault();
        autoPatrol.Update(new(AutoChestContext, Position,
            head is not null && chest is not null ? head with { Position = chest.Position } : head,
            IsPlanning, EventNavigationActive, PatrolSuspended || IsLoading,
            now - lastScan <= 1500,
            chest is not null && (chest.Kind == SpotKind.Carrot ? CarrotGathering.InRange(Position, chest) : AutoChestOpener.Eligible(chest, Position)),
            chest?.Opening == true || CarrotMode && carrotGathering.Busy && carrotGathering.Bunny is null,
            Remaining.Skip(1).FirstOrDefault(), CarrotMode), now);
        if (enabled && autoPatrol.Faulted)
        {
            var reason = autoPatrol.Detail;
            PauseRoute();
            Message = reason;
            Log.Warning("CrescentCompass auto patrol stopped: {Reason}", reason);
        }
    }

}
