using System.Numerics;
using CrescentCompass.Core;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace CrescentCompass;

public sealed partial class Plugin
{
    internal CarrotSearchWeights CarrotWeights { get; } = new();
    private readonly CarrotGathering carrotGathering = new();
    private readonly CarrotEmptyCheck carrotEmptyCheck = new();
    internal bool CarrotMode => Config.PatrolRoute == PatrolRouteKind.Carrot;
    internal int PatrolStartNumber => CarrotMode ? Config.CarrotStartNumber : Config.ChartStartNumber;
    internal string CarrotDetail => carrotGathering.Detail;
    internal bool CanConfirmCarrot => Active && CarrotMode && !IsPaused && !IsPlanning && !IsOccupied &&
        Remaining.FirstOrDefault() is { } pad && CarrotRoute.Number(pad) is not null && Vector3.DistanceSquared(Position, pad.Position) <= 36;

    private void ResetCarrotInspection() => carrotEmptyCheck.Reset();
    private void ResetCarrotSession()
    {
        CarrotWeights.Reset(catalog.Where(s => s.Territory == Session.Territory));
        carrotGathering.Reset(); ResetCarrotInspection();
    }
    internal void ResetCarrotWeights() => _ = Framework.RunOnFrameworkThread(() =>
    {
        if (!Active || !CarrotMode) return;
        CarrotWeights.Reset(catalog.Where(s => s.Territory == Session.Territory));
        Message = "蘿蔔搜尋權重已重設為 +2；路線保持不變。";
    });
    internal void ConfirmCarrotPickup(string expectedId) => _ = Framework.RunOnFrameworkThread(() =>
    {
        if (!CanConfirmCarrot || Remaining.FirstOrDefault() is not { } pad || pad.Id != expectedId) return;
        if (!carrotGathering.PickupConfirmed) CarrotWeights.ConfirmPickup(pad.Id);
        // Manual confirmation completes only this pad; no chest-open history is written.
        FinishCarrotPad(pad, empty: false);
        carrotGathering.Reset();
        Message = "已手動確認蘿蔔拾取與寶箱處理。";
    });

    private void StartCarrotRoute(int number, bool lazy)
    {
        if (!Active || Session.Territory != CarrotRoute.Territory) { Message = "蘿蔔路線僅支援新月島南部。"; return; }
        var points = CarrotRoute.Order(catalog, number);
        StopRoute(); SetChartStart(number); ReleaseFateNavigation();
        Session.RestartCarrots();
        Route = new(points, 0, false); Remaining.AddRange(points); routeRevision = Session.Revision;
        if (lazy) SetLazyPatrolRoute(points);
        else if (GroundNavigation.Status() == "地形導航已就緒") StartWalkingPlan(points.ToArray(), new HashSet<string>(), true, ordered: true);
        else routeAutomation.ScheduleFlag(points[0].Id);
        Message = $"蘿蔔路線從 #{CarrotRoute.Number(points[0]):00} 開始，依提供的圖表巡查 25 點；搜尋權重保留。";
    }

    private void FinishCarrotPad(Spot pad, bool empty)
    {
        if (Remaining.FirstOrDefault()?.Id != pad.Id) return;
        if (empty) { CarrotWeights.CheckEmpty(pad.Id); Session.Skip(pad.Id); }
        else Session.Visit(pad.Id);
        Remaining.RemoveAt(0); ResetCarrotInspection(); StopGroundInspection(); routeAutomation.Reset();
        routeRevision = Session.Revision;
        if (Remaining.FirstOrDefault() is { } next) routeAutomation.ScheduleFlag(next.Id);
        Message = empty ? "已確認蘿蔔空點，權重歸零並前往下一站。" : "蘿蔔與兔子寶箱已處理，前往下一站。";
        if (Remaining.Count == 0) Message += " 本輪完成；重新啟動可再查 25 點，權重保留。";
    }

    private void UpdateCarrotRoute(IReadOnlyList<Observation> observations, long now)
    {
        if (Remaining.FirstOrDefault() is not { } pad || CarrotRoute.Number(pad) is null) return;
        UpdateHeadPath(now);
        if (carrotEmptyCheck.Update(pad, observations, Position, CurrentLeg,
            !carrotGathering.Busy && !IsOccupied && AutoChestContext.GetBlockReason(allowCombat: true).Length == 0 && !autoPatrol.Recovering, now))
            FinishCarrotPad(pad, empty: true);
    }

    private void UpdateCarrotGathering(IReadOnlyList<Observation> observations, long now)
    {
        if (!CarrotMode || !autoPatrol.Enabled || IsPaused || IsPlanning || EventNavigationActive || Remaining.FirstOrDefault() is not { } pad) return;
        int? count = TryReadLoot(out var slots) ? (int)slots.Where(s => s.ItemId == CarrotGathering.ItemId).Sum(s => s.Quantity) : null;
        var context = AutoChestContext;
        var result = carrotGathering.Update(pad, observations, Position, count,
            autoPatrol.CanOpenChest && !context.Mounted && context.GetBlockReason(allowCombat: true).Length == 0,
            Objects.LocalPlayer?.IsCasting == true, now, UseCarrot);
        if (result.PickedObject != 0) CarrotWeights.ConfirmPickup(pad.Id, result.PickupSequence);
        if (result.Failed) { autoPatrol.Fail(carrotGathering.Detail); PauseRoute(); Message = carrotGathering.Detail; }
        else if (result.Finished) FinishCarrotPad(pad, empty: false);
    }

    private unsafe bool UseCarrot(Observation carrot)
    {
        if (!CarrotMode || !PatrolAutoOpen || !autoPatrol.CanOpenChest || AutoChestContext.Mounted ||
            AutoChestContext.GetBlockReason(allowCombat: true).Length != 0) return false;
        var obj = Objects.SearchById(carrot.ObjectId);
        if (obj is null || !obj.IsValid() || obj.IsDead || obj.BaseId != 2010139 ||
            Vector3.DistanceSquared(obj.Position, carrot.Position) > 1 || !CarrotGathering.InRange(Position, carrot with { Position = obj.Position })) return false;
        var agent = AgentInventoryContext.Instance();
        if (agent == null) return false;
        agent->UseItem(CarrotGathering.ItemId);
        return true;
    }
}
