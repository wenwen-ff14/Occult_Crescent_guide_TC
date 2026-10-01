using System.Numerics;
using CrescentCompass.Core;

namespace CrescentCompass.Ui;

internal sealed record CompassFilters(bool Carrots, bool Silver, bool Bronze, PointDisplayMode DisplayMode, bool Special = true, bool Tower = false, bool Exploration = false, bool OnlyUnexplored = true)
{
    public bool HideCandidates => DisplayMode != PointDisplayMode.Candidates;
}
internal sealed record CompassPotState(bool Active, bool AutoFlag, string Detail, int Candidates, bool Revealed, string? Coordinates,
    string AutomationDetail = "收到新提示時自動更新旗標。");
internal sealed record CompassPoint(Spot Spot, SpotStatus Status, string Coordinates, float Distance, DateTimeOffset? LastSeen, double? WalkingDistance = null, bool LivePath = false, int? ChartNumber = null);
internal sealed record CompassRouteControls(bool ChartMode, int StartNumber, bool Paused, ChestOpenProgress? LastOpen,
    IReadOnlyList<CompassPoint> ChartPoints, int MapRevision = 0);
internal sealed record CompassFatePoint(ushort Id, string Name, string Coordinates, string Status, bool CanFlag = true);
internal sealed record CompassFateState(bool Notify, string Countdown, string NextName, string Detail, IReadOnlyList<CompassFatePoint> Active,
    IReadOnlyList<CompassFatePoint>? Locations = null);
internal sealed record CompassCeState(bool Enabled, CeCooldownSnapshot Snapshot, DateTimeOffset Now);
internal sealed record CompassViewState(
    bool Active, string Region, Vector3 Position, CompassFilters Filters, bool WorldHints,
    IReadOnlyList<CompassPoint> Points, IReadOnlyList<CompassPoint> Route,
    int TotalStops, double PlannedDistance, bool Exact, bool RouteChanged, string Message, CompassPotState? Pot = null, int? CompletedStops = null, TreasureSurvey? TreasureSurvey = null,
    bool AutoAdvanceChests = true, int SkippedStops = 0, float EmptyCheckRadius = RouteAutomation.CheckRadius,
    string AutomationDetail = "先規劃路線，再開始自動巡查。", CompassFateState? Fates = null,
    bool Planning = false, string NavigationDetail = "地形導航已就緒", IReadOnlyList<RouteLeg>? GroundLegs = null,
    IReadOnlyList<Spot>? Unreachable = null, bool Transit = false,
    string ExplorationDetail = "等待角色探索紀錄；未讀取前不列入未探索清單。",
    bool HideOtherPlayers = false, string PlayerVisibilityDetail = "只在新月島生效，預設關閉。", CompassRouteControls? Controls = null,
    CompassCeState? Ce = null);
internal sealed record CompassActions(
    Action<CompassFilters> SetFilters, Action<bool> SetWorldHints, Action Plan,
    Action<Spot?> Flag, Action Next, Action Restart,
    Action<bool>? SetPotAutoFlag = null, Action? FlagPot = null, Action? RestartPot = null, Action<int>? PotHint = null, Action<bool>? SetAutoAdvance = null,
    Action<float>? SetEmptyCheckRadius = null, Action? ClearSurvey = null,
    Action<bool>? SetPotFateNotify = null, Action<ushort>? FlagPotFate = null, Action<ushort>? FlagPotFateLocation = null,
    Action? CancelPlanning = null, Action<bool>? SetHideOtherPlayers = null,
    Action<bool>? SetChartMode = null, Action<int>? SetChartStart = null, Action? ContinueAfterLast = null,
    Action? Pause = null, Action? Resume = null, Action? Stop = null, Action? ConfirmOpened = null,
    Action<bool>? SetCeTracking = null, Action? ClearCeCooldowns = null);
