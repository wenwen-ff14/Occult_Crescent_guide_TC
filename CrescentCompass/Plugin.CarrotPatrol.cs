using System.Numerics;
using CrescentCompass.Core;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace CrescentCompass;

public sealed partial class Plugin
{
    internal CarrotSearchWeights CarrotWeights { get; } = new();
    private readonly CarrotGathering carrotGathering = new();
    private readonly CarrotEmptyCheck carrotEmptyCheck = new();
    private readonly CarrotPickupTracker carrotPickupTracker = new();
    internal bool CarrotMode => Config.PatrolRoute == PatrolRouteKind.Carrot;
    internal int PatrolStartNumber => CarrotMode ? Config.CarrotStartNumber : Config.ChartStartNumber;
    internal string CarrotDetail => carrotGathering.Detail;
    internal bool CanConfirmCarrot => Active && Session.Territory == CarrotRoute.Territory;

    private void ResetCarrotInspection() => carrotEmptyCheck.Reset();
    private void ResetCarrotSession()
    {
        CarrotWeights.Reset(catalog.Where(s => s.Territory == Session.Territory));
        carrotGathering.Reset(); carrotPickupTracker.Reset(); ResetCarrotInspection();
    }
    internal void ResetCarrotWeights() => _ = Framework.RunOnFrameworkThread(() =>
    {
        if (!CanConfirmCarrot) return;
        CarrotWeights.Reset(catalog.Where(s => s.Territory == Session.Territory));
        Message = "蘿蔔搜尋權重已重設為 +2；路線保持不變。";
    });
    internal void ConfirmCarrotPickup(string expectedId, int expectedRevision) => _ = Framework.RunOnFrameworkThread(() =>
    {
        if (!CanConfirmCarrot || catalog.FirstOrDefault(s => s.Id == expectedId && CarrotRoute.Number(s) is not null) is not { } pad) return;
        if (!CarrotWeights.ConfirmReportedPickup(pad.Id, expectedRevision))
        { Message = "蘿蔔紀錄已更新，請重新確認要標記的拾取。"; return; }
        carrotPickupTracker.RecordManual(pad.Id);
        Message = $"已標記蘿蔔 #{CarrotRoute.Number(pad):00} 拾取，全圖加權（上限 +2）；原巡航保持不變。";
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

    private void FinishCarrotPad(Spot pad, bool empty, bool inspected = false)
    {
        if (Remaining.FirstOrDefault()?.Id != pad.Id) return;
        if (empty || inspected) CarrotWeights.CheckEmpty(pad.Id);
        if (empty) Session.Skip(pad.Id);
        else Session.Visit(pad.Id);
        Remaining.RemoveAt(0); ResetCarrotInspection(); StopGroundInspection(); routeAutomation.Reset();
        routeRevision = Session.Revision;
        if (Remaining.FirstOrDefault() is { } next) routeAutomation.ScheduleFlag(next.Id);
        Message = inspected ? "蘿蔔點已巡查，權重歸零並前往下一站。" : empty ? "已確認蘿蔔空點，權重歸零並前往下一站。" : "蘿蔔與兔子寶箱已處理，前往下一站。";
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

    private int? UpdateCarrotPickups(IReadOnlyList<Observation> observations, long now)
    {
        if (Session.Territory != CarrotRoute.Territory) return null;
        int? count = TryReadLoot(out var slots) ? (int)slots.Where(s => s.ItemId == CarrotGathering.ItemId).Sum(s => s.Quantity) : null;
        if (carrotPickupTracker.Update(catalog, observations, Position, count, Objects.LocalPlayer?.IsCasting == true, now) is { } pickup &&
            CarrotWeights.ConfirmPickup(pickup.PadId, pickup.Sequence))
            Message = $"已偵測蘿蔔 #{CarrotRoute.Number(catalog.First(s => s.Id == pickup.PadId)):00} 拾取，全圖加權（上限 +2）。";
        return count;
    }

    private void UpdateCarrotGathering(IReadOnlyList<Observation> observations, int? count, long now)
    {
        if (!CarrotMode || !autoPatrol.Enabled || IsPaused || IsPlanning || EventNavigationActive || Remaining.FirstOrDefault() is not { } pad) return;
        var context = AutoChestContext;
        var result = carrotGathering.Update(pad, observations, Position, count,
            autoPatrol.CanOpenChest && !context.Mounted && context.GetBlockReason(allowCombat: true).Length == 0,
            Objects.LocalPlayer?.IsCasting == true, now, carrot =>
            {
                if (!UseCarrot(carrot)) return false;
                carrotPickupTracker.RecordUse(pad, carrot, count!.Value, now, observations);
                return true;
            });
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
