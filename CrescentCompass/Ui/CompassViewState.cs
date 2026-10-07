using System.Numerics;
using CrescentCompass.Core;

namespace CrescentCompass.Ui;

internal sealed record CompassFilters(bool Carrots, bool Silver, bool Bronze, PointDisplayMode DisplayMode, bool Special = true, bool Tower = false, bool Exploration = false, bool OnlyUnexplored = true)
{
    public bool HideCandidates => DisplayMode != PointDisplayMode.Candidates;
}
internal sealed record CompassPotState(bool Active, bool AutoFlag, string Detail, int Candidates, bool Revealed, string? Coordinates,
    string AutomationDetail = "收到新提示時自動更新旗標。");
internal sealed record CompassPoint(Spot Spot, SpotStatus Status, string Coordinates, float Distance, DateTimeOffset? LastSeen, double? WalkingDistance = null, bool LivePath = false, int? ChartNumber = null, int? CarrotWeight = null);
internal sealed record CompassCarrotState(int Pickups, string Detail, bool CanConfirm);
internal sealed record CompassRouteControls(bool ChartMode, int StartNumber, bool Paused, ChestOpenProgress? LastOpen,
    IReadOnlyList<CompassPoint> ChartPoints, int MapRevision = 0, PatrolRouteKind RouteKind = PatrolRouteKind.Chart,
    int? NextChartNumber = null, bool BocchiAvailable = true, string RouteDetail = "");
internal sealed record CompassAutoPatrolState(bool Enabled, bool CanStart, string Detail);
internal sealed record CompassFatePoint(ushort Id, string Name, string Coordinates, string Status, bool CanFlag = true);
internal sealed record CompassFateState(bool Notify, string Countdown, string NextName, string Detail, IReadOnlyList<CompassFatePoint> Active,
    IReadOnlyList<CompassFatePoint>? Locations = null, bool NotifySoon = true, bool FetchSharedTimeOnEntry = true, string SharedTimeDetail = "",
    CompassPotTimeDebug? Debug = null);
internal sealed record CompassPotTimeDebugRow(string Label, string Value);
internal sealed record CompassPotTimeDebug(string Reason, IReadOnlyList<CompassPotTimeDebugRow> Rows, bool CanRetry = false, string RetryDetail = "")
{
    public string Report => "CrescentCompass 魔法罐共享時間診斷\n原因：" + Reason + "\n" +
        string.Join("\n", Rows.Select(row => $"{row.Label}：{row.Value}"));
}
internal sealed record CompassCeState(bool Enabled, CeCooldownSnapshot Snapshot, DateTimeOffset Now);
internal sealed record CompassPotOverlayOptions(bool Enabled);
internal sealed record CompassPotOverlayActions(Action<bool> SetVisible);
internal sealed record CompassPatrolOverlayOptions(bool Enabled);
internal sealed record CompassPatrolOverlayActions(Action<bool> SetVisible);
internal sealed record CompassPhantomOverlayOptions(bool Enabled);
internal sealed record CompassPhantomOverlayActions(Action<bool> SetVisible);
internal sealed record CompassGeneralFates(bool AutoFlag, bool Holding, string Detail, IReadOnlyList<CompassFatePoint> Active);
internal sealed record CompassWaymarkState(IReadOnlyList<WaymarkPreset> Presets, bool CanCapture, bool CanPlace, bool Busy, string Detail,
    string Error = "", bool Failed = false, string PlacementUnavailableReason = "", Guid SelectionRequest = default, int SelectionRevision = 0,
    bool IgnoreDistance = false);
internal sealed record CompassWaymarkActions(Action<string> Save, Action<string, bool> Import, Action<Guid> Place, Action<Guid> Delete,
    Action<Guid, string> Rename, Func<Guid, string?> Export, Action Cancel, Action<bool>? SetIgnoreDistance = null);
internal sealed record CompassPhantomJobs(byte? CurrentJob, bool CanSwitch, string Detail,
    string MacroIconDetail = "關閉遊戲巨集編輯視窗後，預設 M 會自動換成職業圖示。");
internal sealed record CompassViewState(
    bool Active, string Region, Vector3 Position, CompassFilters Filters, bool WorldHints,
    IReadOnlyList<CompassPoint> Points, IReadOnlyList<CompassPoint> Route,
    int TotalStops, double PlannedDistance, bool Exact, bool RouteChanged, string Message, CompassPotState? Pot = null, int? CompletedStops = null, TreasureSurvey? TreasureSurvey = null,
    int SkippedStops = 0,
    string AutomationDetail = "先規劃路線，再開始自動巡查。", CompassFateState? Fates = null,
    bool Planning = false, string NavigationDetail = "地形導航已就緒", IReadOnlyList<RouteLeg>? GroundLegs = null,
    IReadOnlyList<Spot>? Unreachable = null, bool Transit = false,
    string ExplorationDetail = "等待角色探索紀錄；未讀取前不列入未探索清單。",
    bool HideOtherPlayers = false, string PlayerVisibilityDetail = "只在新月島生效，預設關閉。", CompassRouteControls? Controls = null,
    CompassCeState? Ce = null, string PluginVersion = "預覽", CompassGeneralFates? GeneralFates = null, CompassWaymarkState? Waymarks = null,
    bool AutoOpenNearbyChests = false, string AutoChestDetail = "關閉；勾選後自動開啟 2 公尺內的寶箱。", CompassPhantomJobs? PhantomJobs = null, CompassLootState? Loot = null,
    CompassPotOverlayOptions? PotOverlay = null, CompassPatrolOverlayOptions? PatrolOverlay = null, CompassAutoPatrolState? AutoPatrol = null,
    CompassPhantomOverlayOptions? PhantomOverlay = null, CompassCarrotState? Carrots = null);
internal sealed record CompassActions(
    Action<CompassFilters> SetFilters, Action<bool> SetWorldHints, Action Plan,
    Action<Spot?> Flag, Action Next, Action Restart,
    Action<bool>? SetPotAutoFlag = null, Action? FlagPot = null, Action? RestartPot = null, Action<int>? PotHint = null, Action? ClearSurvey = null,
    Action<bool>? SetPotFateNotify = null, Action<ushort>? FlagPotFate = null, Action<ushort>? FlagPotFateLocation = null,
    Action? CancelPlanning = null, Action<bool>? SetHideOtherPlayers = null,
    Action<int>? SetChartStart = null, Action? ContinueAfterLast = null,
    Action? Pause = null, Action? Resume = null, Action? Stop = null, Action? ConfirmOpened = null,
    Action<bool>? SetCeTracking = null, Action? ClearCeCooldowns = null,
    Action<bool>? SetFateAutoFlag = null, Action? ReleaseFateNavigation = null, Action<ushort>? FlagGeneralFate = null, CompassWaymarkActions? Waymarks = null,
    Action<bool>? SetAutoOpenNearbyChests = null, Action<byte>? SwitchPhantomJob = null,
    Func<uint, Vector2, bool>? DrawPhantomJobIcon = null, Action<string>? CopyPhantomMacro = null, Action? RefreshPhantomMacroIcons = null, CompassLootActions? Loot = null,
    Func<string, ulong>? GetGameTexture = null, Action<ushort, bool>? FlagCeLocation = null,
    CompassPotOverlayActions? PotOverlay = null, CompassPatrolOverlayActions? PatrolOverlay = null,
    Action<bool>? SetPotFateSoonNotify = null, Action<bool>? SetAutoPatrol = null, CompassPhantomOverlayActions? PhantomOverlay = null,
    Action<bool>? SetFetchPotTimeOnEntry = null, Action<string>? CopyPotTimeDebug = null,
    Action<PatrolRouteKind>? SetPatrolRoute = null, Action? RetryPotTime = null,
    Action<string>? ConfirmCarrotPickup = null, Action? ResetCarrotWeights = null);
