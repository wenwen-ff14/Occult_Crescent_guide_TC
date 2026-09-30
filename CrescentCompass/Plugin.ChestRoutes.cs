using CrescentCompass.Core;

namespace CrescentCompass;

public sealed partial class Plugin
{
    internal void SetChartMode(bool enabled)
    {
        if (Config.UseChartRoute == enabled) return;
        StopRoute(); Config.UseChartRoute = enabled;
        PluginInterface.SavePluginConfig(Config);
        Message = enabled ? "已選擇南部 68 點圖表；選擇起點後開始。" : "已選擇地形最短順序；依篩選規劃。";
    }

    internal void SetChartStart(int number)
    {
        Config.ChartStartNumber = Math.Clamp(number, 1, ChestChart.Count);
        PluginInterface.SavePluginConfig(Config);
    }

    internal void StartChartRoute(int number)
    {
        if (!Active || Session.Territory != ChestChart.Territory)
        { Message = "這張 68 點圖表只適用新月島南部。"; return; }
        SetChartStart(number);
        var points = ChestChart.Order(catalog, Config.ChartStartNumber)
            .Select(s => Session.Get(s.Id)).OfType<TrackedSpot>()
            .Where(s => Session.CanPatrol(s.Spot.Id)).Select(s => s.Spot).ToArray();
        StopRoute();
        Route = new(points, 0, false); Remaining.AddRange(points); routeRevision = Session.Revision;
        if (points.Length == 0) { Message = "本場所有圖表點都已巡查或略過；需要重跑可清除巡查紀錄。"; return; }
        navigationStatus = GroundNavigation.Status();
        if (navigationStatus == "地形導航已就緒") StartWalkingPlan(points, new HashSet<string>(), true, ordered: true);
        else
        {
            if (!PotNavigationActive && !IsOccupied) TryFlag(points[0]);
            else routeAutomation.ScheduleFlag(points[0].Id);
        }
        Message = $"從圖表 #{ChestChart.Number(points[0]):00} 開始，共 {points.Length} 站；依編號遞增，68 後接 1，巡查一圈。" +
            (navigationStatus == "地形導航已就緒" ? " 正在計算站間步行路段。" : " 地形導航未就緒，先提供編號與旗標；空點判定等待可通行路徑。");
    }

    internal void ContinueAfterLastChest()
    {
        if (LastChestOpen is { } last) StartChartRoute(ChestChart.Next(last.Number));
    }

    internal void PauseRoute()
    {
        if (IsPaused || Remaining.Count == 0 && !IsPlanning) return;
        pendingResume = activeRequest ?? pendingResume;
        StopPlanning(); StopGroundInspection(); routeAutomation.SetPaused(true);
        Message = "巡查已暫停；保留目前站點，停止換站與自動換旗。魔法罐追蹤使用自己的開關。";
    }

    internal void ResumeRoute()
    {
        if (!IsPaused || !Active) return;
        routeAutomation.SetPaused(false);
        pendingResume ??= Remaining.Count == 0 ? null : new(Remaining.ToArray(), routePriorities, Config.AutoAdvanceChests, true, Config.UseChartRoute);
        navigationStatus = GroundNavigation.Status();
        ResumeWalkingPlan();
        Message = "巡查已繼續；保留站點與既有紀錄。";
    }

    internal void StopRoute()
    {
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
