using CrescentCompass.Core;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private BocchiPatrolRoute? bocchiRoute;
    internal bool BocchiRouteAvailable => bocchiRoute is not null;
    private void InitializeBocchiRoute()
    {
        try
        {
            var assembly = typeof(Plugin).Assembly;
            using var route = assembly.GetManifestResourceStream("CrescentCompass.Data.SouthHorn.treasure_route.json")
                ?? throw new InvalidDataException("Missing BOCCHI route resource.");
            using var costs = assembly.GetManifestResourceStream("CrescentCompass.Data.SouthHorn.precomputed_treasure_hunt_data.json")
                ?? throw new InvalidDataException("Missing BOCCHI distance resource.");
            bocchiRoute = BocchiPatrolRoute.Load(route, costs, catalog);
        }
        catch (Exception error) { Log.Error(error, "BOCCHI route data failed validation; its route remains unavailable"); }
    }

    internal int? NextPatrolChartNumber => CarrotMode || LastChestOpen is not { } last ? null : Config.PatrolRoute == PatrolRouteKind.Bocchi
        ? bocchiRoute?.NextChartNumber(last.Number) : ChestChart.Next(last.Number);
    internal string PatrolRouteDetail
    {
        get
        {
            if (CarrotMode) return "蘿蔔圖表 1～25 · 固定巡航 · 權重 0 / +1 / +2";
            if (Config.PatrolRoute != PatrolRouteKind.Bocchi) return "原圖表順序 · 68 點";
            if (bocchiRoute is null) return "BOCCHI 路線資料驗證失敗，請查看記錄或切換原圖表順序。";
            var region = bocchiRoute.Region(Remaining.FirstOrDefault());
            var cost = bocchiRoute.ReferenceCosts(Remaining);
            return (region is null ? $"BOCCHI · 68 點／{bocchiRoute.SegmentCount} 區段" : $"BOCCHI · 區段 {region.Index}/{region.Count} · {region.Id}") +
                (cost is { TotalLegs: > 0 } ? $" · 站間參考 {cost.Distance / 1000:F1} km（{cost.KnownLegs}/{cost.TotalLegs} 段）" : "");
        }
    }

    internal void SetPatrolRoute(PatrolRouteKind kind)
    {
        if (!Enum.IsDefined(kind) || kind == Config.PatrolRoute) return;
        if (kind == PatrolRouteKind.Bocchi && bocchiRoute is null) { Message = "BOCCHI 路線資料不可用。"; return; }
        StopRoute();
        Config.PatrolRoute = kind;
        PluginInterface.SavePluginConfig(Config);
        Message = $"已切換為{(kind == PatrolRouteKind.Carrot ? "蘿蔔路線" : kind == PatrolRouteKind.Bocchi ? " BOCCHI 分區順序" : "原圖表順序")}；巡查紀錄保留，請重新啟動巡查。";
    }

    private void PlanPatrolRoute(PatrolRouteKind kind)
    {
        if (kind == PatrolRouteKind.Bocchi && bocchiRoute is null) { Message = "BOCCHI 路線資料不可用。"; return; }
        SetPatrolRoute(kind);
        Plan();
    }

    internal void SetChartStart(int number)
    {
        if (CarrotMode) Config.CarrotStartNumber = Math.Clamp(number, 1, CarrotRoute.Count);
        else Config.ChartStartNumber = Math.Clamp(number, 1, ChestChart.Count);
        PluginInterface.SavePluginConfig(Config);
    }

    internal void StartChartRoute(int number) => StartChartRoute(number, lazy: false);

    private void StartChartRoute(int number, bool lazy)
    {
        if (CarrotMode) { StartCarrotRoute(number, lazy); return; }
        if (!Active || Session.Territory != ChestChart.Territory)
        { Message = "這張 68 點圖表只適用新月島南部。"; return; }
        if (Config.PatrolRoute == PatrolRouteKind.Bocchi && bocchiRoute is null)
        { Message = "BOCCHI 路線資料不可用；請切換原圖表順序。"; return; }
        SetChartStart(number);
        ReleaseFateNavigation();
        var ordered = Config.PatrolRoute == PatrolRouteKind.Bocchi ? bocchiRoute!.Order(Config.ChartStartNumber)
            : ChestChart.Order(catalog, Config.ChartStartNumber);
        var points = ordered
            .Select(s => Session.Get(s.Id)).OfType<TrackedSpot>()
            .Where(s => Session.CanPatrol(s.Spot.Id)).Select(s => s.Spot).ToArray();
        StopRoute();
        Route = new(points, 0, false); Remaining.AddRange(points); routeRevision = Session.Revision;
        if (points.Length == 0) { Message = "本場所有圖表點都已巡查或略過；需要重跑可清除巡查紀錄。"; return; }
        navigationStatus = GroundNavigation.Status();
        if (lazy) SetLazyPatrolRoute(points);
        else if (navigationStatus == "地形導航已就緒") StartWalkingPlan(points, new HashSet<string>(), true, ordered: true);
        else
        {
            if (!PotNavigationActive && !IsOccupied) TryFlag(points[0]);
            else routeAutomation.ScheduleFlag(points[0].Id);
        }
        Message = $"從圖表 #{ChestChart.Number(points[0]):00} 開始，共 {points.Length} 站；" +
            (Config.PatrolRoute == PatrolRouteKind.Bocchi ? "依 BOCCHI 分區順序巡查一圈，跨區段仍使用地面尋路。" : "依編號遞增，68 後接 1，巡查一圈。") +
            (lazy ? " 逐段尋路，行進中預先準備下一段。" : navigationStatus == "地形導航已就緒" ? " 正在計算站間步行路段。" : " 地形導航未就緒，先提供編號與旗標；空點判定等待可通行路徑。");
    }

    private void SetLazyPatrolRoute(IEnumerable<Spot> points)
    {
        var pending = points.Where(s => Session.CanPatrol(s.Id)).ToArray();
        var history = Route.Stops.Where(s => RouteStopCompleted(s) || Session.Get(s.Id)?.Status == SpotStatus.Skipped).ToArray();
        pendingResume = null;
        StopPlanning(); StopGroundInspection(); WalkingRoute = null;
        routeAutomation.Reset();
        Route = new(history.Concat(pending).DistinctBy(s => s.Id).ToArray(), 0, false);
        Remaining.Clear(); Remaining.AddRange(pending); routeRevision = Session.Revision; MapRevision++;
        if (pending.FirstOrDefault() is { } first) routeAutomation.ScheduleFlag(first.Id);
    }

    internal void ContinueAfterLastChest()
    {
        if (NextPatrolChartNumber is { } next) StartChartRoute(next);
    }

    internal void PauseRoute()
    {
        SuspendAutoPatrol("巡查已暫停，停止自動移動。");
        if (IsPaused || Remaining.Count == 0 && !IsPlanning) return;
        pendingResume = activeRequest ?? pendingResume;
        StopPlanning(); StopGroundInspection(); routeAutomation.SetPaused(true);
        Message = "巡查已暫停；保留目前站點，停止換站與自動換旗。魔法罐追蹤使用自己的開關。";
    }

    internal void ResumeRoute()
    {
        if (!IsPaused || !Active) return;
        ReleaseFateNavigation();
        routeAutomation.SetPaused(false);
        pendingResume ??= Remaining.Count == 0 ? null : new(Remaining.ToArray(), routePriorities, Config.AutoAdvanceChests, true, Config.UseChartRoute);
        navigationStatus = GroundNavigation.Status();
        ResumeWalkingPlan();
        Message = "巡查已繼續；保留站點與既有紀錄。";
    }

    internal void StopRoute()
    {
        carrotGathering.Reset(); ResetCarrotInspection();
        StopAutoPatrol("已終止自動巡查。");
        pendingResume = null;
        StopPlanning(); StopGroundInspection(); WalkingRoute = null;
        routeAutomation.SetPaused(false);
        Remaining.Clear(); Route = new([], 0, false); routePriorities = new HashSet<string>(); MapRevision++;
        Message = "已終止巡查；傳送後也不會自行恢復。巡查與上次開箱紀錄保留，可指定起點重新開始。";
    }

    private bool CanSaveChestProgress => Active && PlayerState.IsLoaded && sessionCharacterId != 0 &&
        PlayerState.ContentId == sessionCharacterId && Objects.LocalPlayer?.EntityId == PlayerState.EntityId;

    private void SaveChestProgress(ChestOpenProgress progress)
    {
        if (!CanSaveChestProgress) return;
        Config.ChestProgress[$"{sessionCharacterId:X16}:{ChestChart.Id}"] = progress;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void ConfirmChestOpened()
    {
        if (IsPaused || !CanSaveChestProgress || Remaining.FirstOrDefault() is not { } head || ChestChart.Number(head) is not { } number) return;
        SaveChestProgress(new(ChestChart.Id, number, DateTimeOffset.UtcNow, true));
        Next();
        Message = $"已手動確認圖表 #{number:00} 開箱，並保留紀錄。";
    }
}
