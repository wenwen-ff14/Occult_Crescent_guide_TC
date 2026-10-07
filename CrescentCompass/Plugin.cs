using System.Numerics;
using System.Reflection;
using System.Collections.Concurrent;
using CrescentCompass.Core;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using NativeTreasure = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace CrescentCompass;

public sealed partial class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IPartyList Party { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    // Experimental in API 13, verified against the bundled TC implementation; keep the dependency isolated.
#pragma warning disable Dalamud001
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
#pragma warning restore Dalamud001
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IToastGui Toasts { get; private set; } = null!;
    [PluginService] internal static IFateTable Fates { get; private set; } = null!;
    [PluginService] internal static INotificationManager Notifications { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private readonly List<Spot> catalog = [];
    private readonly List<PotCandidate> potCatalog = [];
    private readonly RouteAutomation routeAutomation = new();
    private readonly ChestOpenTracker chestOpenTracker = new();
    internal bool IsPaused => routeAutomation.Paused;
    internal int MapRevision { get; private set; }
    internal ChestOpenProgress? LastChestOpen => sessionCharacterId != 0 &&
        Config.ChestProgress.TryGetValue($"{sessionCharacterId:X16}:{ChestChart.Id}", out var saved) &&
        saved is { ChartId: ChestChart.Id, Number: >= 1 and <= ChestChart.Count } ? saved : null;
    private readonly PotHintQueue potHints = new();
    private readonly PotFlagAutomation potAutomation = new();
    private readonly PlayerVisibility playerVisibility = new();
    private bool retryPlayerVisibility;
    private bool PlayerVisibilityActive => !disposed && Client.IsLoggedIn && Objects.LocalPlayer is not null &&
        SpotCatalog.IsSupported(Client.TerritoryType) && !IsLoading && !Client.IsGPosing &&
        !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] && !Conditions[ConditionFlag.OccupiedInCutSceneEvent];
    internal string PlayerVisibilityDetail => playerVisibility.Faulted ? "玩家顯示更新失敗，已暫停隱藏；可關閉後重新開啟。" :
        !Config.HideOtherPlayers ? $"只在新月島生效，預設關閉。{playerVisibility.FriendDetail}" :
        !PlayerVisibilityActive ? $"等待進入新月島；傳送、過場與合照模式暫停隱藏。{playerVisibility.FriendDetail}" :
        !playerVisibility.HasFriendRoster ? "尚無此角色的好友快取，暫停隱藏。請在島外開啟好友名單，等待快取完成再進島。" :
        $"已隱藏 {playerVisibility.HiddenCount} 名玩家模型 · 好友快取 {playerVisibility.FriendCount} 人 · 保留自己、小隊、好友及倒地者";
    private readonly ConcurrentQueue<(TreasureSurvey Survey, ushort Territory, uint Instance)> treasureSurveys = new();
    private IReadOnlyList<Observation> lastObservations = [];
    private readonly WindowSystem windows = new("CrescentCompass");
    private readonly MainWindow window;
    private readonly IClientState.LogoutDelegate logoutHandler;
    private readonly Dictionary<uint, SpotKind?> cofferKinds = [];
    private long lastScan;
    private readonly PatrolContext patrolContext = new();
    private bool routeSuspended;
    private sealed record PlanningRequest(Spot[] Points, IReadOnlySet<string> Priority, bool FlagFirst, bool Reconnect = false, bool Ordered = false);
    private PlanningRequest? activeRequest;
    private PlanningRequest? pendingResume;
    private IReadOnlySet<string> routePriorities = new HashSet<string>();
    private bool disposed;
    private int routeRevision;
    private long lastError;
    private long lastFateError;
    private long lastExplorationError;
    private ulong sessionCharacterId;
    internal string ExplorationDetail { get; private set; } = "等待角色探索紀錄；未讀取前不列入未探索清單。";
    private CancellationTokenSource? routeCancellation;
    private Task<WalkingRoute>? routeTask;
    private bool flagAfterPlanning;
    private volatile string routeProgress = "";
    internal bool IsPlanning => routeTask is not null || pendingResume is not null;
    internal bool PatrolSuspended => Session.Territory != 0 && patrolContext.IsSuspended;
    internal string NavigationDetail => IsPaused ? "路線已暫停；按繼續後接回保留的站點。" : routeTask is not null ? routeProgress : pendingResume is not null
        ? PotNavigationActive ? "路線已保留；魔法罐尋寶結束後接續計算。" : "路線已保留；等待角色與地形導航就緒後重算。" : navigationStatus;
    private string navigationStatus = "進入新月島後載入地形導航。";
    internal WalkingRoute? WalkingRoute { get; private set; }
    private CancellationTokenSource? inspectionCancellation;
    private Task<RouteLeg?>? inspectionTask;
    private Spot? inspectionTarget;
    private RouteLeg? inspectionPath;
    private long inspectionQueriedAt;
    private long inspectionSampleAt;
    internal RouteLeg? CurrentLeg => Active && !IsPaused && !IsPlanning && !EventNavigationActive && inspectionPath is { } path &&
        Remaining.FirstOrDefault()?.Id == path.DestinationId && Environment.TickCount64 - inspectionSampleAt <= 3000 &&
        Vector3.Distance(Position, path.From) <= 20 ? path : null;

    internal Configuration Config { get; }
    internal SurveySession Session { get; } = new();
    internal PotSession Pot { get; } = new();
    internal PotFateTracker PotFates { get; } = new();
    internal TreasureSurvey? TreasureSurvey { get; private set; }
    internal bool PotNavigationActive => Config.AutoFlagPot && Pot.Searching;
    internal string PotAutomationDetail => potAutomation.Detail;
    internal PlannedRoute Route { get; private set; } = new([], 0, true);
    internal List<Spot> Remaining { get; } = [];
    internal Vector3 Position => Objects.LocalPlayer?.Position ?? Vector3.Zero;
    internal bool Active => !disposed && Client.IsLoggedIn && Objects.LocalPlayer != null &&
        SpotCatalog.IsSupported(Client.TerritoryType) && Session.Territory == Client.TerritoryType && !IsLoading && !patrolContext.IsSuspended;
    internal bool RouteChanged => routeRevision != Session.Revision;
    internal string AutomationDetail => IsPaused ? "使用者已暫停巡查；魔法罐與 FATE 使用獨立開關。" : !Active ? "進入新月島後才會開始判定。" : IsPlanning ? "地形路線計算中，暫停巡查判定。" : PotNavigationActive ? "魔法罐尋寶中，暫停一般巡查與自動換旗。" : FateNavigationActive ? "FATE 標點中，巡查保留；可在 FATE 選單解除並接續。" : routeAutomation.Detail;
    internal string Message { get; private set; } = "進入新月島後會自動開始偵測。";
    private static bool IsLoading => Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.BetweenAreas51];

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Config.Version < 4)
        {
            Config.Version = 4;
            PluginInterface.SavePluginConfig(Config);
        }
        Config.ChartStartNumber = Math.Clamp(Config.ChartStartNumber, 1, ChestChart.Count);
        if (!Enum.IsDefined(Config.PatrolRoute)) Config.PatrolRoute = PatrolRouteKind.Bocchi;
        Config.ChestProgress ??= [];
        InitializeLoot();
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith("_locations.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            catalog.AddRange(SpotCatalog.Load(stream));
        }
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith("pot_candidates.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            potCatalog.AddRange(PotCatalog.Load(stream));
        }
        InitializeBocchiRoute();
        waymarks = new WaymarkLibrary(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "waymarks.json"));
        window = new MainWindow(this);
        windows.AddWindow(window);
        Commands.AddHandler("/crescent", new CommandInfo(OnCommand)
        {
            HelpMessage = "新月島尋寶羅盤。autopatrol 自動巡查寶箱，autopatrol stop 停止自動巡查；loot 背包整理保留／丟棄／售出，jobs 職業圖標與巨集，job 職業名稱或編號 切換幻影職業，jobicons 更新快捷列巨集圖示；waymarks 標點，ce 冷卻，fate 事件，route 規劃，pause 暫停，resume 繼續，stop 終止，flag 旗標，pot 魔法罐，next 已巡查，reset 續巡，clear 清除巡查。",
        });
        Framework.Update += Update;
        Chat.ChatMessage += OnChatMessage;
        Toasts.Toast += OnToast;
        Toasts.QuestToast += OnQuestToast;
        Toasts.ErrorToast += OnErrorToast;
        Client.TerritoryChanged += TerritoryChanged;
        logoutHandler = (_, _) => { SetLootArmed(false); lootTracker.Reset(); CancelWaymarks("已登出，停止標點還原。"); patrolContext.Logout(); ResetSession(0); PotFates.Reset(); };
        Client.Logout += logoutHandler;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenWindow;
        PluginInterface.UiBuilder.OpenConfigUi += window.OpenSettings;
        Log.Information($"CrescentCompass {assembly.GetName().Version} loaded; auto-next={Config.AutoAdvanceChests}, empty-radius={Config.EmptyCheckRadius:F0}m, mode={Config.DisplayMode}, pot-auto={Config.AutoFlagPot}, pot-fate-notify={Config.NotifyPotFateSpawn}, fate-auto={Config.AutoFlagFates}, hide-players={Config.HideOtherPlayers}");
        Log.Information($"[PotTime] diagnostic=pot-debug-2; provider=OccultTrackerV3; entry-query={Config.FetchPotTimeOnEntry}; uploads=disabled");
    }

    private void TerritoryChanged(ushort _) { CancelWaymarks("區域切換，停止標點還原。"); patrolContext.Suspend(); SuspendPatrol(); }

    private void SuspendPatrol()
    {
        SuspendAutoPatrol("傳送讀取中，保留自動巡查與站點。");
        FateFlags.Suspend();
        CeCooldowns.Suspend();
        if (routeSuspended) return;
        routeSuspended = true;
        pendingResume = activeRequest is { } request ? request with { Reconnect = true }
            : pendingResume ?? (Remaining.Count > 0 ? new(Remaining.ToArray(), routePriorities, Config.AutoAdvanceChests, true, Config.UseChartRoute) : null);
        StopPlanning(); StopGroundInspection(); routeAutomation.Reset();
        chestOpenTracker.Reset();
        // Visibility and queued observations are transient; survey history and the selected stops are not.
        Session.Observe([], DateTimeOffset.UtcNow);
        potHints.Clear(); treasureSurveys.Clear(); lastObservations = [];
        WalkingRoute = null;
        Message = $"傳送讀取中，保留 {Remaining.Count} 個待巡查點與巡查紀錄。";
    }

    private void ResetSession(ushort territory)
    {
        ResetPotEntryTimeSync(territory);
        GroundNavigation.PatrolPaths.Clear();
        StopAutoPatrol("區域、分流或角色已變更，停止自動巡查。");
        autoChestOpener.Reset();
        FateFlags.Reset(); fateNavigationWasActive = false;
        CeCooldowns.Reset();
        StopPlanning(); StopGroundInspection(); WalkingRoute = null;
        pendingResume = null; routeSuspended = false; routePriorities = new HashSet<string>();
        Session.Reset(territory, catalog);
        ResetCarrotSession();
        Session.OnlyUnexplored = Config.OnlyUnexplored;
        sessionCharacterId = territory == 0 || !PlayerState.IsLoaded ? 0 : PlayerState.ContentId;
        ExplorationDetail = "等待角色探索紀錄；未讀取前不列入未探索清單。";
        Pot.Reset(); potHints.Clear(); potAutomation.Reset(); lastObservations = [];
        TreasureSurvey = null; treasureSurveys.Clear();
        Remaining.Clear();
        routeAutomation.SetPaused(false); chestOpenTracker.Reset(); MapRevision++;
        Route = new PlannedRoute([], 0, true);
        routeRevision = Session.Revision;
        Message = territory == 0 ? "等待進入新月島。" : "偵測中。選擇目標後按「規劃巡查路線」。";
    }

    private void Update(IFramework _)
    {
        UpdateLoot();
        UpdatePhantomMacroIcons();
        UpdatePhantomJobs();
        UpdateWaymarks();
        if (retryPlayerVisibility) { retryPlayerVisibility = false; playerVisibility.Retry(); }
        // Must run before the patrol's loading/territory early returns so island exits also restore models.
        playerVisibility.Update(Config.HideOtherPlayers && PlayerVisibilityActive, Config);
        var now = Environment.TickCount64;
        var change = patrolContext.Update(Client.IsLoggedIn, IsLoading, Objects.LocalPlayer is not null, Client.TerritoryType, Client.Instance, now);
        if (change == PatrolContextChange.Suspended) { SuspendPatrol(); return; }
        if (change == PatrolContextChange.Reset) { ResetSession(patrolContext.Territory); PotFates.Reset(); }
        else if (change == PatrolContextChange.Resumed)
        {
            routeSuspended = false;
            Message = IsPaused ? "已返回同島同分流，仍保持使用者暫停；按繼續才接回巡查。"
                : Remaining.Count == 0 && pendingResume is null ? "已返回同島同分流；目前沒有進行中的巡查。"
                : "已返回同島同分流，巡查紀錄保留；將從傳送落點接續規劃。";
        }
        UpdateAutoPatrol(now);
        if (!Active || Objects.LocalPlayer is not { } player) return;
        if (PlayerState.IsLoaded && PlayerState.ContentId != 0 && sessionCharacterId != PlayerState.ContentId)
        { ResetSession(Client.TerritoryType); PotFates.Reset(); }
        if (now - lastScan < 500) return;
        lastScan = now;
        navigationStatus = GroundNavigation.Status();
        UpdateFates(now);
        UpdateCeCooldowns(now);
        UpdateExploration(now);
        try
        {
            var observations = ScanObjects();
            var time = DateTimeOffset.UtcNow;
            Pot.UpdateBuff(player.StatusList.Any(s => s.StatusId == PotSession.StatusId), Client.TerritoryType, Position, potCatalog, lastObservations);
            potHints.ApplyTo(Pot, Client.TerritoryType, Client.Instance, time);
            Pot.ObserveReveal(observations, time);
            while (treasureSurveys.TryDequeue(out var survey))
                if (survey.Territory == Client.TerritoryType && survey.Instance == Client.Instance && time - survey.Survey.CapturedAt < TimeSpan.FromSeconds(3))
                    TreasureSurvey = survey.Survey;
            lastObservations = observations;
            Session.Observe(observations, time);
            if (chestOpenTracker.Update(catalog, observations, Position, player.IsCasting ? player.CastTargetObjectId : 0, now, time) is { } opened)
                SaveChestProgress(opened);
            potAutomation.Update(Pot, Config.AutoFlagPot, !IsOccupied, now, FlagPot);
            UpdateFateNavigation();
            ResumeWalkingPlan();
            PollWalkingPlan();
            if (!IsPaused && !IsPlanning) UpdateRoute(observations, now);
            UpdateAutoPatrol(now);
            UpdateAutoChests(observations, now);
            UpdateCarrotGathering(observations, now);
            // Keep pot navigation in charge for the whole search, including waits for a reveal or new hint.
            if (!IsPaused && !IsPlanning && !EventNavigationActive && (Config.AutoAdvanceChests || autoPatrol.Enabled) && !IsOccupied && routeAutomation.CanAttemptFlag(Remaining.FirstOrDefault()?.Id, now))
            {
                var flagged = false;
                try { flagged = TryFlag(Remaining[0]); }
                finally { routeAutomation.RecordFlagAttempt(now, flagged); }
            }
        }
        catch (Exception error)
        {
            if (autoPatrol.Enabled) { autoPatrol.Fail("寶箱偵測失敗，已停止自動巡查。"); PauseRoute(); }
            routeAutomation.ResetInspection();
            chestOpenTracker.Reset();
            Message = "偵測失敗；請查看 Dalamud 記錄。";
            if (now - lastError > 10_000) { lastError = now; Log.Error(error, "CrescentCompass object scan failed"); }
        }
    }

    private static bool IsOccupied => Conditions[ConditionFlag.OccupiedInEvent] || Conditions[ConditionFlag.OccupiedInCutSceneEvent] ||
        Conditions[ConditionFlag.WatchingCutscene] || Conditions[ConditionFlag.WatchingCutscene78] || Objects.LocalPlayer?.IsCasting == true;

    private void UpdateFates(long tick)
    {
        try
        {
            if (Fates.Address == 0) { potTimeScanIssue = "FATE 表尚未就緒。"; FateFlags.Suspend(); return; }
            List<PotFateObservation> observed = [];
            List<FateFlagObservation> allFates = [];
            List<PotFingerprintFate> fingerprints = [];
            var tableRows = 0; var validRows = 0;
            foreach (var fate in Fates)
            {
                tableRows++;
                if (!Fates.IsValid(fate) || fate.TerritoryType.RowId != Client.TerritoryType) continue;
                validRows++;
                var phase = fate.State switch
                {
                    FateState.Running => PotFatePhase.Running,
                    FateState.Preparation => PotFatePhase.Preparation,
                    FateState.Ended or FateState.Failed or FateState.WaitingForEnd => PotFatePhase.Finished,
                    _ => (PotFatePhase?)null,
                };
                if (phase is null) continue;
                if (phase != PotFatePhase.Finished && fate.Position != Vector3.Zero && Coordinates.IsFinite(fate.Position))
                    fingerprints.Add(new(fate.FateId, fate.StartTimeEpoch));
                allFates.Add(new(fate.FateId, Client.TerritoryType, fate.Name.TextValue, fate.Position, fate.StartTimeEpoch,
                    fate.Duration, fate.Progress, phase != PotFatePhase.Finished, phase == PotFatePhase.Preparation));
                if (PotFateTracker.Find(fate.FateId, Client.TerritoryType) is null) continue;
                observed.Add(new(fate.FateId, Client.TerritoryType, phase.Value, fate.StartTimeEpoch, fate.Duration,
                    fate.Progress, fate.Position, fate.Name.TextValue));
            }
            var now = DateTimeOffset.UtcNow;
            FateFlags.Observe(Client.TerritoryType, Client.Instance, allFates, now, Config.AutoFlagFates, Position);
            var notices = PotFates.Update(Client.TerritoryType, Client.Instance, observed, now, Config.NotifyPotFateSpawn);
            UpdatePotEntryTimeSync(fingerprints, tableRows, validRows, now);
            foreach (var fate in notices)
            {
                var position = Coordinates.IsFinite(fate.Position) ? MapPosition(new Spot("pot-fate", Client.TerritoryType, SpotKind.Other, 0, fate.Position)) : "座標尚未就緒";
                var message = $"{fate.Definition.Side} · {fate.Name} · {position}（{(fate.Preparing ? "準備中" : $"進度 {fate.Progress}%")}）";
                Chat.Print($"[新月島羅盤] 偵測到魔法罐 FATE：{message}");
                Notifications.AddNotification(new Notification
                {
                    Title = "魔法罐 FATE 已出現", Content = message, Type = NotificationType.Info,
                    InitialDuration = TimeSpan.FromSeconds(10),
                });
            }
            if (PotFates.TakeUpcomingReminder(now, Config.NotifyPotFateSoon) is { } reminder)
            {
                var side = reminder.Definition.Side == "北側" ? "北罐" : "南罐";
                var seconds = (int)Math.Ceiling((reminder.ExpectedAt - now).TotalSeconds);
                var message = $"{side} · {reminder.Definition.Name} · 預估再 {seconds / 60:00}:{seconds % 60:00} 出現（{reminder.ExpectedAt.ToLocalTime():HH:mm:ss}）。實際時間以遊戲出現為準。";
                Chat.Print($"[新月島羅盤] 魔法罐預估 5 分鐘內出現：{message}");
                Notifications.AddNotification(new Notification
                {
                    Title = "魔法罐預估 5 分鐘內出現", Content = message, Type = NotificationType.Info,
                    InitialDuration = TimeSpan.FromSeconds(10),
                });
            }
        }
        catch (Exception error)
        {
            potTimeScanIssue = $"FATE 掃描錯誤：{error.GetType().Name}";
            FateFlags.Suspend();
            if (tick - lastFateError > 10_000) { lastFateError = tick; Log.Error(error, "CrescentCompass FATE scan failed"); }
        }
    }

    internal void SetPotFateNotify(bool enabled)
    {
        Config.NotifyPotFateSpawn = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void SetPotFateSoonNotify(bool enabled)
    {
        Config.NotifyPotFateSoon = enabled;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void FlagPotFate(ushort id) => FlagGeneralFate(id);

    internal Spot? PotFateLocation(ushort id)
    {
        var definition = PotFateTracker.Find(id, Session.Territory);
        if (definition is null || !Coordinates.IsFinite(definition.Location) ||
            !Data.GetExcelSheet<TerritoryType>().TryGetRow(definition.Territory, out var territory) ||
            !Data.GetExcelSheet<Map>().TryGetRow(territory.Map.RowId, out var map) || map.SizeFactor == 0 || map.TerritoryType.RowId != definition.Territory) return null;
        return new($"pot-fate-location-{id}", definition.Territory, SpotKind.Other, 0, definition.Location,
            $"{(definition.Side == "北側" ? "北罐" : "南罐")} · {definition.Name}", map.RowId);
    }

    internal void FlagPotFateLocation(ushort id)
    {
        if (!Active || PotFateLocation(id) is not { } location || location.Territory != Client.TerritoryType)
        { Message = "目前沒有此區域的魔法罐固定座標可插旗。"; return; }
        ReleaseFateNavigation();
        routeAutomation.CancelFlag();
        var ok = GameGui.OpenMapWithMapLink(new MapLinkPayload(location.Territory, location.MapId,
            (int)MathF.Round(location.Position.X * 1000), (int)MathF.Round(location.Position.Z * 1000)));
        Message = ok ? $"固定 FATE 地點：{location.Name} {MapPosition(location)}；不代表目前已出現。" : "固定地點插旗未成功，可稍後再試。";
    }

    private void UpdateRoute(IReadOnlyList<Observation> observations, long now)
    {
        if (EventNavigationActive) { routeAutomation.ResetInspection(); StopGroundInspection(); return; }
        if (CarrotMode) { UpdateCarrotRoute(observations, now); return; }
        UpdateHeadPath(now);
        var groundBlock = GroundInspectionBlock(now);
        var result = routeAutomation.Update(Session, Remaining, observations, Position, now, Config.DisplayMode, Config.AutoAdvanceChests || autoPatrol.Enabled,
            !IsOccupied && (!autoPatrol.Enabled || AutoChestContext.GetBlockReason(allowCombat: true).Length == 0), Config.EmptyCheckRadius, groundBlock, preservePlannedStops: true,
            emptyWaitMs: autoPatrol.Enabled ? PatrolEmptyCheck.ConfirmationMs : RouteAutomation.EmptyWaitMs);
        if (result.Reason != RouteAdvanceReason.None)
        {
            Message = result.Reason == RouteAdvanceReason.Empty ? autoPatrol.Enabled ? "已提前確認此點沒有可用寶箱，前往下一站。" : "附近連續 3 秒沒有可用寶箱，已略過此點。" : "目前寶箱已開啟，已移至下一站。";
            routeRevision = Session.Revision;
            if (Remaining.Count == 0) Message += " " + RouteEndMessage();
        }
        else if (result.Removed > 0) Message = "路線已移除開啟、已巡查或不符合目前顯示條件的目標。";
    }

    private void UpdateHeadPath(long now)
    {
        var head = Remaining.FirstOrDefault();
        if (IsOccupied || head is null || !Session.CanPatrol(head.Id)) { StopGroundInspection(); return; }
        if (head != inspectionTarget) { StopGroundInspection(); inspectionTarget = head; }
        if (autoPatrol.Enabled)
        {
            var path = autoPatrol.PathFrom(Position);
            if (path is not null || autoPatrol.Recovering || Vector3.DistanceSquared(Position, head.Position) > PatrolEmptyCheck.EarlyRadius * PatrolEmptyCheck.EarlyRadius)
            {
                if (inspectionTask is not null) { StopGroundInspection(); inspectionTarget = head; }
                inspectionPath = path is not null ? WalkingRoutePlanner.ValidatePath(head.Id, Position, head.Position, path) : null;
                inspectionSampleAt = now;
                return;
            }
        }
        if (inspectionTask is { IsCompleted: true } finished)
        {
            inspectionTask = null;
            inspectionCancellation?.Dispose(); inspectionCancellation = null;
            try { inspectionPath = finished.GetAwaiter().GetResult(); inspectionSampleAt = inspectionQueriedAt; }
            catch { inspectionPath = null; }
        }
        if (navigationStatus != "地形導航已就緒") { StopGroundInspection(); return; }
        if (GroundNavigation.PatrolPaths.TryGet(Position, head.Position, out var cached))
        {
            inspectionPath = WalkingRoutePlanner.ValidatePath(head.Id, Position, head.Position, cached);
            inspectionSampleAt = now;
            return;
        }
        if (inspectionTask is null && now - inspectionQueriedAt >= 1000)
        {
            inspectionCancellation = new CancellationTokenSource();
            var token = inspectionCancellation.Token;
            var from = Position;
            inspectionQueriedAt = now;
            inspectionTask = Task.Run(async () => WalkingRoutePlanner.ValidatePath(head.Id, from, head.Position,
                await GroundNavigation.FindPatrolPath(from, head.Position, token).ConfigureAwait(false)), token);
        }
    }

    private string? GroundInspectionBlock(long now)
    {
        var head = Remaining.FirstOrDefault();
        if (!(Config.AutoAdvanceChests || autoPatrol.Enabled) || head is null || !CofferKinds.IsCoffer(head.Kind) || !RouteAutomation.IsNear(head, Position, Config.EmptyCheckRadius)) return null;
        if (navigationStatus != "地形導航已就緒") return "地形導航未就緒，暫停空點確認。";
        if (autoPatrol.Enabled && autoPatrol.Recovering) return "正在恢復卡住路段，暫停空點確認。";
        if (autoPatrol.Enabled)
            return PatrolEmptyCheck.BlockReason(Position, head, lastObservations, CurrentLeg, Config.EmptyCheckRadius);
        if (CurrentLeg is not { } path)
            return "正在確認可通行距離；找不到地面路徑時不會自動略過。";
        var distance = path.Length + Vector3.Distance(Position, path.From);
        return distance <= Config.EmptyCheckRadius ? null : $"仍需繞路約 {distance:F0} m，靠近後才確認空點（{Config.EmptyCheckRadius:F0} m）。";
    }

    private void StopGroundInspection()
    {
        inspectionCancellation?.Cancel(); inspectionCancellation?.Dispose(); inspectionCancellation = null;
        if (inspectionTask is { } task)
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        inspectionTask = null; inspectionTarget = null; inspectionPath = null; inspectionQueriedAt = 0; inspectionSampleAt = 0;
    }

    private string RouteEndMessage()
    {
        var completed = Route.Stops.Count(RouteStopCompleted);
        var skipped = Route.Stops.Count(s => Session.Get(s.Id)?.Status == SpotStatus.Skipped);
        return completed + skipped == Route.Stops.Count
            ? $"本輪巡查結束：已巡查 {completed} 站，略過 {skipped} 站。"
            : "目前路線已無可用站點；尚未巡查的目標可在靠近後重新規劃。";
    }

    internal void SetPotAutoFlag(bool enabled)
    {
        Config.AutoFlagPot = enabled;
        potAutomation.Reset();
        routeAutomation.ResetInspection();
        PluginInterface.SavePluginConfig(Config);
    }

    private unsafe List<Observation> ScanObjects()
    {
        var result = new List<Observation>();
        HashSet<ulong> looted = [];
        var loot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot.Instance();
        if (loot != null)
            foreach (var item in loot->Items)
                if (item.ItemId != 0) looted.Add(item.ChestObjectId);
        foreach (var obj in Objects)
        {
            if (!obj.IsValid() || obj.IsDead) continue;
            if (obj.ObjectKind == ObjectKind.EventObj && CofferKinds.EventObject(obj.BaseId) is { } eventKind)
            {
                // Keep untargetable event coffers as presence evidence; they must not be mistaken for empty pads.
                result.Add(new Observation(obj.GameObjectId, obj.BaseId, eventKind, obj.Position,
                    Available: !looted.Contains(obj.GameObjectId), Targetable: obj.IsTargetable));
                continue;
            }
            if (obj.ObjectKind != ObjectKind.Treasure) continue;
            if (!cofferKinds.TryGetValue(obj.BaseId, out var kind))
            {
                kind = Data.GetExcelSheet<Treasure>().TryGetRow(obj.BaseId, out var row) ? CofferKinds.TreasureModel(row.SGB.RowId) : SpotKind.Other;
                cofferKinds[obj.BaseId] = kind;
            }
            if (kind is null || obj.Address == 0) continue;
            if (catalog.Any(s => s.Territory == Client.TerritoryType && s.Kind == SpotKind.Tower && Vector3.DistanceSquared(s.Position, obj.Position) <= 16))
                kind = SpotKind.Tower;
            var native = (NativeTreasure*)obj.Address;
            var spent = looted.Contains(obj.GameObjectId) || (native->Flags & (NativeTreasure.TreasureFlags.Opened | NativeTreasure.TreasureFlags.FadedOut)) != 0 ||
                native->State is NativeTreasure.TreasureState.Opened or NativeTreasure.TreasureState.FadingOut or NativeTreasure.TreasureState.FadedOut;
            result.Add(new Observation(obj.GameObjectId, obj.BaseId, kind.Value, obj.Position, !spent,
                obj.IsTargetable && native->State == NativeTreasure.TreasureState.Unopened,
                native->State == NativeTreasure.TreasureState.Opening));
        }
        return result;
    }

    private void UpdateExploration(long now)
    {
        var points = catalog.Where(s => s.Territory == Session.Territory && s.Kind == SpotKind.Exploration).ToArray();
        if (points.Length == 0) { ExplorationDetail = "此區域尚無已核對的島上探索地點。"; return; }
        var read = false;
        try
        {
            if (ExplorationReader.TryRead(points, out var snapshot))
            {
                read = true;
                if (Session.UpdateExploration(snapshot) && Config.OnlyUnexplored && !IsPaused)
                {
                    var head = Remaining.FirstOrDefault()?.Id;
                    var removed = Remaining.RemoveAll(s => s.Kind == SpotKind.Exploration && !Session.CanPatrol(s.Id));
                    if (removed > 0)
                    {
                        StopGroundInspection(); WalkingRoute = null; routeAutomation.Reset();
                        if (head != Remaining.FirstOrDefault()?.Id && Config.AutoAdvanceChests && Remaining.FirstOrDefault() is { } next)
                            routeAutomation.ScheduleFlag(next.Id);
                        Message = "已完成的探索筆記已移出路線；可重新規劃其餘路程。";
                        if (Remaining.Count == 0) Message = "已完成的探索筆記已移出路線。 " + RouteEndMessage();
                    }
                    // Do not let an in-flight plan reintroduce a note completed during path queries.
                    if (activeRequest is { } request && request.Points.Any(s => !Session.CanPatrol(s.Id)))
                    {
                        pendingResume = request with { Reconnect = true };
                        StopPlanning();
                    }
                }
            }
        }
        catch (Exception error)
        {
            if (now - lastExplorationError > 10_000)
            { lastExplorationError = now; Log.Warning(error, "CrescentCompass exploration unlock query failed"); }
        }
        var progress = Session.Exploration;
        ExplorationDetail = read
            ? $"島上探索紀錄 · 未探索 {points.Length - progress.CompletedCount} / {points.Length} · 已完成 {progress.CompletedCount}"
            : progress.KnownCount > 0 ? "探索紀錄暫無法更新，沿用本角色上次讀取結果。"
            : "等待角色探索紀錄；未讀取前不列入未探索清單。";
    }

    internal bool RouteStopCompleted(Spot spot) => Session.Get(spot.Id)?.Status == SpotStatus.Visited ||
        Config.OnlyUnexplored && Session.Exploration.State(spot) == ExplorationState.Explored;

    internal IReadOnlyList<TrackedSpot> Filtered() => Session.Snapshot().Where(s => s.Spot.Kind switch
    {
        SpotKind.Carrot => Config.IncludeCarrots,
        SpotKind.Silver => Config.IncludeSilver,
        SpotKind.Bronze => Config.IncludeBronze,
        SpotKind.Tower => Config.IncludeTower,
        SpotKind.Exploration => Config.IncludeExploration,
        _ => Config.IncludeSpecial,
    }).Where(s => Session.CanDisplay(s.Spot.Id, Config.DisplayMode)).ToArray();

    internal void Plan()
    {
        ReleaseFateNavigation();
        if (!Active) { Message = "請先進入新月島。"; return; }
        if (Config.UseChartRoute) { StartChartRoute(PatrolStartNumber); return; }
        routeAutomation.SetPaused(false);
        StartWalkingPlan(Filtered().Where(s => s.Status != SpotStatus.Visited).Select(s => s.Spot).ToArray(), new HashSet<string>(), false);
    }

    private void ResumeWalkingPlan()
    {
        if (IsPaused || pendingResume is not { } request || EventNavigationActive || IsOccupied) return;
        if (navigationStatus != "地形導航已就緒")
        {
            // The chart order and map flags are usable even before a navigation mesh is available.
            if (request.Ordered) { pendingResume = null; if (Remaining.FirstOrDefault() is { } first && request.FlagFirst) routeAutomation.ScheduleFlag(first.Id); }
            return;
        }
        var points = request.Points.Select(s => Session.Get(s.Id)).OfType<TrackedSpot>()
            .Where(s => Session.CanPatrol(s.Spot.Id)).Select(s => s.Spot).ToArray();
        pendingResume = null;
        if (points.Length == 0) { Message = "保留巡查紀錄，目前沒有待續巡的目標。"; return; }
        if (autoPatrol.Enabled && request.Ordered)
        {
            SetLazyPatrolRoute(points);
            Message = "已接續固定巡查順序；沿用快取，逐段準備地面路徑。";
            return;
        }
        StartWalkingPlan(points, request.Priority, request.FlagFirst, true, request.Ordered);
    }

    private void StartWalkingPlan(Spot[] points, IReadOnlySet<string> priority, bool flagFirst, bool reconnect = false, bool ordered = false)
    {
        SuspendAutoPatrol("路線計算中，暫停自動移動。");
        pendingResume = null;
        StopPlanning(); StopGroundInspection();
        routeAutomation.Reset();
        if (points.Length == 0) { Message = "目前篩選沒有可巡查的目標。"; return; }
        navigationStatus = GroundNavigation.Status();
        if (navigationStatus != "地形導航已就緒") { Message = navigationStatus; return; }
        var cancellation = routeCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var start = Position;
        flagAfterPlanning = flagFirst;
        activeRequest = new(points, priority, flagFirst, reconnect, ordered);
        routeProgress = $"計算地形步行路線 · 0 / {points.Length} 站";
        Message = "正在依可通行路徑排序；上次路線與巡查紀錄保留至計算完成。";
        Action<WalkingProgress> progress = p => { if (!token.IsCancellationRequested) routeProgress = $"{p.Phase} · {p.Stops} / {p.Total} 站 · 已查詢 {p.Queries} 條路徑"; };
        routeTask = Task.Run(() => ordered ? WalkingRoutePlanner.PlanOrderedAsync(start, points, GroundNavigation.FindPatrolPath, progress, token)
            : WalkingRoutePlanner.PlanAsync(start, points, priority, GroundNavigation.FindPath, progress, token), token);
    }

    private void PollWalkingPlan()
    {
        if (IsPaused || routeTask is not { IsCompleted: true } task) return;
        var request = activeRequest;
        activeRequest = null;
        routeTask = null;
        routeCancellation?.Dispose(); routeCancellation = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (!Active || result.Route.Stops.Any(s => !(request?.Reconnect == true || request?.Ordered == true ? Session.CanPatrol(s.Id) : Session.CanDisplay(s.Id, Config.DisplayMode)) || Session.Get(s.Id)?.Spot.Position != s.Position))
            { Message = "計算期間目標已變動，請重新規劃；保留原本巡查紀錄。"; return; }
            if (request?.Reconnect == true && request.Ordered == false && Remaining.Count > 0 && result.Unreachable.Count > 0)
            { Message = "部分點暫時找不到接續路徑，已保留原路線及紀錄，可稍後重新規劃。"; return; }
            var history = request?.Reconnect == true
                ? Route.Stops.Where(s => RouteStopCompleted(s) || Session.Get(s.Id)?.Status == SpotStatus.Skipped).ToArray() : [];
            WalkingRoute = result; Route = result.Route;
            if (history.Length > 0) Route = Route with { Stops = history.Concat(result.Route.Stops).DistinctBy(s => s.Id).ToArray() };
            routePriorities = request?.Priority ?? new HashSet<string>();
            Remaining.Clear(); Remaining.AddRange(result.Route.Stops);
            if (request?.Reconnect != true && request?.Ordered != true) MapRevision++;
            routeRevision = Session.Revision;
            var flagged = flagAfterPlanning && !EventNavigationActive && !IsOccupied && Remaining.Count > 0 && TryFlag(Remaining[0]);
            Message = request?.Ordered == true
                ? $"{(CarrotMode ? "蘿蔔路線" : Config.PatrolRoute == PatrolRouteKind.Bocchi ? "BOCCHI 分區順序" : "圖表順序")}已保留 {Remaining.Count} 站，已取得 {result.Legs.Count} 條步行路段。" +
                  (result.Unreachable.Count > 0 ? $" {result.Unreachable.Count} 段待確認，站點仍保留，可手動插旗查看。" : "")
                : $"步行路線已規劃 {Remaining.Count} 站，約 {Route.Length:F0} m。" +
                  (result.Unreachable.Count > 0 ? $" {result.Unreachable.Count} 點未找到完整路徑，未納入且未記為已巡查。" : "");
            Message +=
                (flagged ? " 已更新首站旗標。" : " 可按「在地圖插旗」開始。") +
                (request?.Reconnect == true ? " 已從目前位置接續，巡查紀錄保留。" : flagAfterPlanning && request?.Ordered != true ? " 已優先安排上輪未巡查點。" : "");
        }
        catch (Exception error)
        {
            Log.Warning(error, "CrescentCompass ground route planning failed");
            Message = error is TimeoutException or InvalidOperationException ? error.Message : "地形路線計算中斷；請確認 vnavmesh 已就緒後重試。";
        }
    }

    private void StopPlanning()
    {
        routeCancellation?.Cancel(); routeCancellation?.Dispose(); routeCancellation = null;
        if (routeTask is { } task)
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        routeTask = null;
        activeRequest = null;
    }

    internal void CancelPlanning() { if (autoPatrol.Enabled) PauseRoute(); pendingResume = null; StopPlanning(); Message = "已取消地形計算，保留原本路線與巡查紀錄。"; }

    internal void SaveFilters()
    {
        Session.OnlyUnexplored = Config.OnlyUnexplored;
        if (Config.UseChartRoute) { PluginInterface.SavePluginConfig(Config); return; }
        pendingResume = null;
        StopPlanning(); StopGroundInspection(); WalkingRoute = null;
        routeAutomation.SetPaused(false); MapRevision++;
        PluginInterface.SavePluginConfig(Config);
        Remaining.Clear();
        Route = new PlannedRoute([], 0, true);
        Message = "篩選已變更，請重新規劃路線。";
    }

    internal void SetHideOtherPlayers(bool enabled)
    {
        Config.HideOtherPlayers = enabled;
        retryPlayerVisibility = true;
        PluginInterface.SavePluginConfig(Config);
    }

    internal void Flag(Spot? spot)
    {
        if (autoPatrol.Enabled && spot?.Id != Remaining.FirstOrDefault()?.Id) PauseRoute();
        ReleaseFateNavigation();
        if (!IsPaused) { pendingResume = null; StopPlanning(); }
        routeAutomation.CancelFlag();
        TryFlag(spot);
    }

    private bool TryFlag(Spot? spot)
    {
        if (!Active || spot is null || spot.Territory != Client.TerritoryType || Client.MapId == 0)
        { Message = "目前沒有可插旗的目標，或不在相同區域。"; return false; }
        if (!Session.CanDisplay(spot.Id, Config.DisplayMode) &&
            !(CarrotMode && CarrotRoute.Number(spot) is not null) &&
            !((Remaining.Any(s => s.Id == spot.Id) || Config.UseChartRoute && ChestChart.Number(spot) is not null) && Session.CanPatrol(spot.Id)))
        { Message = "目標已不符合目前顯示條件，未更新旗標。"; return false; }
        spot = Session.Get(spot.Id)!.Spot;
        var link = new MapLinkPayload(spot.Territory, spot.MapId != 0 ? spot.MapId : Client.MapId,
            (int)MathF.Round(spot.Position.X * 1000), (int)MathF.Round(spot.Position.Z * 1000));
        var ok = GameGui.OpenMapWithMapLink(link);
        Message = ok ? $"旗標：{spot.Name ?? MainWindow.KindName(spot.Kind)} {MapPosition(spot)}{(spot.RequiresTower ? " · 塔內，需解鎖進度" : "")}" : "地圖旗標未成功開啟，可手動重試。";
        return ok;
    }

    internal bool FlagPot()
    {
        if (!Active || !Pot.Active || Pot.Territory != Client.TerritoryType || Pot.Target is not { } position || Client.MapId == 0)
        { Message = "目前沒有可確認的魔法罐搜尋位置，等待新提示。"; return false; }
        var ok = GameGui.OpenMapWithMapLink(new MapLinkPayload(Client.TerritoryType, Client.MapId,
            (int)MathF.Round(position.X * 1000), (int)MathF.Round(position.Z * 1000)));
        Message = ok ? (Pot.Revealed ? "魔法罐旗標：附近已現身寶箱。" : "魔法罐旗標：搜尋候選點，尚未確認寶箱；請使用聖靈藥。") : Config.AutoFlagPot ? "魔法罐插旗暫未成功，稍後自動重試。" : "魔法罐插旗失敗，可在手動備援中重試。";
        return ok;
    }

    internal void RestartPot() => Pot.Restart(Position);
    internal void ManualPotHint(int direction) => Pot.Apply(new(PotHintKind.Direction, direction, "手動輸入"), Position, DateTimeOffset.UtcNow);

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool handled)
    {
        // API 13 has no LogMessage-ID event. Restrict the exact TC template parser to system/NPC channels.
        var channel = (XivChatType)((ushort)type & 0x7f);
        if (disposed || channel is not (XivChatType.SystemMessage or XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements) ||
            !Client.IsLoggedIn || !SpotCatalog.IsSupported(Client.TerritoryType) || IsLoading || Objects.LocalPlayer is not { } player) return;
        QueuePotHint(message.TextValue);
        if (channel == XivChatType.SystemMessage && Core.TreasureSurvey.Parse(message.TextValue, DateTimeOffset.UtcNow) is { } survey)
            treasureSurveys.Enqueue((survey, Client.TerritoryType, Client.Instance));
    }

    private void QueuePotHint(string text)
    {
        if (disposed || !Client.IsLoggedIn || IsLoading || !SpotCatalog.IsSupported(Client.TerritoryType) || Objects.LocalPlayer is not { } player) return;
        if (PotHints.Parse(text) is { } hint)
            potHints.Enqueue(hint, player.Position, Client.TerritoryType, Client.Instance, DateTimeOffset.UtcNow);
    }

    // Read API 13 notification events as a fallback; never suppress or modify the game's messages.
    private void OnToast(ref SeString message, ref ToastOptions options, ref bool handled) => QueuePotHint(message.TextValue);
    private void OnQuestToast(ref SeString message, ref QuestToastOptions options, ref bool handled) => QueuePotHint(message.TextValue);
    private void OnErrorToast(ref SeString message, ref bool handled) => QueuePotHint(message.TextValue);

    internal void Next()
    {
        if (IsPaused) { Message = "巡查已暫停，請先繼續。"; return; }
        SuspendAutoPatrol("切換巡查站點，重新確認路徑。");
        pendingResume = null;
        StopPlanning();
        if (!Active || Remaining.Count == 0) return;
        if (CarrotMode) { FinishCarrotPad(Remaining[0], empty: false); carrotGathering.Reset(); return; }
        routeAutomation.Reset();
        Session.Visit(Remaining[0].Id);
        Remaining.RemoveAt(0);
        if (Remaining.Count > 0) Flag(Remaining[0]);
        else Message = RouteEndMessage();
    }

    internal void Restart()
    {
        if (!Active) { Message = "請先進入新月島。"; return; }
        ReleaseFateNavigation();
        if (Config.UseChartRoute)
        {
            if (CarrotMode) { StartCarrotRoute(CarrotRoute.Number(Remaining.FirstOrDefault()) ?? Config.CarrotStartNumber, lazy: false); return; }
            var first = Remaining.FirstOrDefault(s => Session.CanPatrol(s.Id)) ?? Route.Stops.FirstOrDefault(s => Session.CanPatrol(s.Id));
            StartChartRoute(ChestChart.Number(first) ?? Config.ChartStartNumber);
            return;
        }
        routeAutomation.SetPaused(false);
        routeAutomation.Reset();
        var unfinished = Route.Stops.Where(s => Session.CanPatrol(s.Id))
            .Select(s => s.Id).ToHashSet();
        var pending = Filtered().Select(s => s.Spot).ToArray();
        if (pending.Length == 0)
        {
            Message = unfinished.Count > 0
                ? $"上輪仍有 {unfinished.Count} 個未巡查點，目前不符合篩選；可切換全島已知位置後續巡。"
                : "目前沒有未巡查點。需要重跑時，請在顯示設定中清除巡查紀錄。";
            return;
        }
        StartWalkingPlan(pending, unfinished, true);
    }

    internal void ClearSurvey()
    {
        if (!Active) { Message = "請先進入新月島。"; return; }
        Session.RestartSurvey();
        if (CarrotMode) ResetCarrotSession();
        Plan();
    }

    internal static string MapPosition(Spot spot)
    {
        if (!Data.GetExcelSheet<Map>().TryGetRow(spot.MapId != 0 ? spot.MapId : Client.MapId, out var map) || map.SizeFactor == 0) return "座標資料尚未載入";
        var x = Coordinates.ToMap(spot.Position.X, map.SizeFactor, map.OffsetX);
        var y = Coordinates.ToMap(spot.Position.Z, map.SizeFactor, map.OffsetY);
        return $"X {x:F1} / Y {y:F1}";
    }

    private void OnCommand(string command, string args)
    {
        if (HandlePhantomCommand(args)) return;
        switch (args.Trim().ToLowerInvariant())
        {
            case "loot": window.OpenLoot(); return;
            case "ce": window.OpenCeCooldowns(); return;
            case "fate": window.OpenFates(); return;
            case "waymark":
            case "waymarks": window.OpenWaymarks(); return;
            case "route": Plan(); break;
            case "route bocchi": PlanPatrolRoute(PatrolRouteKind.Bocchi); break;
            case "route chart": PlanPatrolRoute(PatrolRouteKind.Chart); break;
            case "route carrot": PlanPatrolRoute(PatrolRouteKind.Carrot); break;
            case "autopatrol": SetAutoPatrol(true); break;
            case "autopatrol stop": SetAutoPatrol(false); break;
            case "flag": Flag(Remaining.FirstOrDefault()); break;
            case "next": Next(); break;
            case "reset": Restart(); break;
            case "clear": ClearSurvey(); break;
            case "pot": FlagPot(); break;
            case "pause": PauseRoute(); break;
            case "resume": ResumeRoute(); break;
            case "stop": StopRoute(); break;
            default: OpenWindow(); return;
        }
        Chat.Print($"[新月島羅盤] {Message}");
    }

    private void OpenWindow() => window.IsOpen = true;
    private void Draw()
    {
        using (new Ui.CompassTheme()) windows.Draw();
        window.DrawWorldHints();
        DrawPotCountdownOverlay();
        DrawPatrolOverlay();
        DrawPhantomJobOverlay();
    }

    public void Dispose()
    {
        disposed = true;
        CancelPotTimeQuery();
        potEntryTimeSync.Disable();
        potTimeHttp.Dispose();
        try { if (!Framework.IsFrameworkUnloading) Framework.RunOnFrameworkThread(() => autoPatrol.Stop("插件已卸載，停止自動巡查。")).GetAwaiter().GetResult(); }
        catch (Exception error) { Log.Warning(error, "CrescentCompass could not stop owned navigation during unload"); }
        CancelWaymarks("插件已卸載，停止標點還原。");
        StopPlanning(); StopGroundInspection();
        Framework.Update -= Update;
        if (playerVisibility.HiddenCount > 0 && !Framework.IsFrameworkUnloading)
        {
            try { Framework.Run(playerVisibility.Restore).GetAwaiter().GetResult(); }
            catch (Exception error) { Log.Warning(error, "CrescentCompass player visibility restoration failed during unload"); }
        }
        Chat.ChatMessage -= OnChatMessage;
        Toasts.Toast -= OnToast;
        Toasts.QuestToast -= OnQuestToast;
        Toasts.ErrorToast -= OnErrorToast;
        Client.TerritoryChanged -= TerritoryChanged;
        Client.Logout -= logoutHandler;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= window.OpenSettings;
        Commands.RemoveHandler("/crescent");
        windows.RemoveAllWindows();
    }
}
