using System.Numerics;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly CompassView view = new();
    private readonly CompassActions actions;

    public MainWindow(Plugin plugin) : base("新月島尋寶羅盤###CrescentCompass")
    {
        this.plugin = plugin;
        Flags |= ImGuiWindowFlags.MenuBar;
        Size = new Vector2(920, 880);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(650, 560), MaximumSize = new Vector2(1600, 1600) };
        actions = new CompassActions(SetFilters, SetWorldHints, plugin.Plan, plugin.Flag, plugin.Next, plugin.Restart,
            plugin.SetPotAutoFlag, () => plugin.FlagPot(), plugin.RestartPot, plugin.ManualPotHint, plugin.ClearSurvey,
            plugin.SetPotFateNotify, plugin.FlagPotFate, plugin.FlagPotFateLocation, plugin.CancelPlanning, plugin.SetHideOtherPlayers,
            plugin.SetChartStart, plugin.ContinueAfterLastChest, plugin.PauseRoute, plugin.ResumeRoute, plugin.StopRoute, plugin.ConfirmChestOpened,
            plugin.SetCeTracking, plugin.ClearCeCooldowns, plugin.SetFateAutoFlag, plugin.ReleaseFateNavigation, plugin.FlagGeneralFate, plugin.WaymarkActions,
            plugin.SetAutoOpenNearbyChests, plugin.SwitchPhantomJob, Plugin.DrawPhantomJobIcon,
            RefreshPhantomMacroIcons: plugin.RefreshPhantomMacroIcons, Loot: new(plugin.SetLootKeep, plugin.SetLootMode, plugin.SetLootArmed),
            GetGameTexture: Plugin.GetGameTexture, FlagCeLocation: plugin.FlagCeLocation,
            PotOverlay: new(plugin.SetPotOverlayVisible),
            PatrolOverlay: new(plugin.SetPatrolOverlayVisible),
            SetPotFateSoonNotify: plugin.SetPotFateSoonNotify, SetAutoPatrol: plugin.SetAutoPatrol,
            PhantomOverlay: new(plugin.SetPhantomOverlayVisible), SetFetchPotTimeOnEntry: plugin.SetFetchPotTimeOnEntry,
            SetPatrolRoute: plugin.SetPatrolRoute, RetryPotTime: plugin.RetryPotTime,
            ConfirmCarrotPickup: plugin.ConfirmCarrotPickup, ResetCarrotWeights: plugin.ResetCarrotWeights,
            CarrotDisplay: new(plugin.SetCarrotOverlayVisible),
            DrawCarrotToolButton: Plugin.DrawCarrotToolButton);
    }

    internal static string KindName(SpotKind kind) => CompassView.KindName(kind);
    internal void OpenSettings() { view.Page = CompassPage.Settings; view.ShowPhantomJobs = false; IsOpen = true; }
    internal void OpenPhantomJobs() { view.Page = CompassPage.Settings; view.ShowPhantomJobs = true; IsOpen = true; }
    internal void OpenCeCooldowns() { view.Page = CompassPage.Ce; IsOpen = true; }
    internal void OpenFates() { view.Page = CompassPage.Fate; IsOpen = true; }
    internal void OpenWaymarks() { view.Page = CompassPage.Waymarks; IsOpen = true; }
    internal void OpenLoot() { view.Page = CompassPage.Loot; IsOpen = true; }

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow;
        var config = plugin.Config;
        CompassPoint Display(TrackedSpot spot) => new(spot.Spot, spot.Status, Plugin.MapPosition(spot.Spot), Vector3.Distance(plugin.Position, spot.Spot.Position), spot.LastSeen,
            ChartNumber: CarrotRoute.Number(spot.Spot) ?? ChestChart.Number(spot.Spot), CarrotWeight: plugin.CarrotWeights.Weight(spot.Spot.Id));
        var chartPoints = plugin.Session.Snapshot().Where(s => plugin.CarrotMode ? CarrotRoute.Number(s.Spot) is not null : ChestChart.Number(s.Spot) is not null).Select(Display).OrderBy(s => s.ChartNumber).ToArray();
        var points = plugin.Session.Snapshot().Where(s => plugin.Session.CanDisplay(s.Spot.Id, config.DisplayMode)).Select(Display).ToArray();
        var currentLeg = plugin.CurrentLeg;
        var legs = plugin.Remaining.Select((stop, i) =>
        {
            if (i == 0 && currentLeg?.DestinationId == stop.Id) return currentLeg;
            var leg = plugin.WalkingRoute?.Legs.FirstOrDefault(l => l.DestinationId == stop.Id);
            // Filtering an intermediate stop invalidates that connection; never draw a fabricated bridge.
            return leg is not null && (i == 0 || leg.From == plugin.Remaining[i - 1].Position) &&
                leg.Path[^1] == plugin.Session.Get(stop.Id)?.Spot.Position ? leg : null;
        }).OfType<RouteLeg>().ToArray();
        var route = plugin.Remaining.Select(s => plugin.Session.Get(s.Id)).OfType<TrackedSpot>().Select(s =>
            Display(s) with { WalkingDistance = legs.FirstOrDefault(l => l.DestinationId == s.Spot.Id)?.Length,
                LivePath = currentLeg?.DestinationId == s.Spot.Id }).ToArray();
        var region = plugin.Session.Territory == 1346 ? "新月島北部" : "新月島南部";
        view.Draw(new CompassViewState(plugin.Active, region, plugin.Position,
            new CompassFilters(config.IncludeCarrots, config.IncludeSilver, config.IncludeBronze, config.DisplayMode, config.IncludeSpecial, config.IncludeTower, config.IncludeExploration, config.OnlyUnexplored),
            config.ShowWorldHints, points, route, plugin.Route.Stops.Count, plugin.Route.Length, plugin.Route.Exact, plugin.RouteChanged, plugin.Message,
            new CompassPotState(plugin.Pot.Active, config.AutoFlagPot, plugin.Pot.Detail, plugin.Pot.Candidates.Count, plugin.Pot.Revealed,
                plugin.Pot.Target is { } target ? Plugin.MapPosition(new Spot("pot-target", plugin.Pot.Territory, SpotKind.PotGold, 0, target)) : null, plugin.PotAutomationDetail),
            plugin.Route.Stops.Count(plugin.RouteStopCompleted), plugin.TreasureSurvey,
            plugin.Route.Stops.Count(s => plugin.Session.Get(s.Id)?.Status == SpotStatus.Skipped), plugin.AutomationDetail, FateState(),
            plugin.IsPlanning, plugin.NavigationDetail, legs,
            plugin.WalkingRoute?.Unreachable.Where(s => plugin.Session.CanPatrol(s.Id)).ToArray(),
            plugin.PatrolSuspended, plugin.ExplorationDetail, config.HideOtherPlayers, plugin.PlayerVisibilityDetail,
            new CompassRouteControls(config.UseChartRoute, plugin.PatrolStartNumber, plugin.IsPaused, plugin.LastChestOpen, chartPoints, plugin.MapRevision,
                config.PatrolRoute, plugin.NextPatrolChartNumber, plugin.BocchiRouteAvailable, plugin.PatrolRouteDetail),
            new CompassCeState(config.TrackCeCooldowns, plugin.CeCooldowns.Snapshot(now), now),
            PluginVersion: typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "未知版本",
            GeneralFates: new(config.AutoFlagFates, plugin.FateFlags.OwnsNavigation(now), plugin.FateFlags.Detail,
                plugin.FateFlags.Active(now).Select(f => new CompassFatePoint(f.Id, f.Name,
                    Coordinates.IsFinite(f.Position) ? Plugin.MapPosition(new Spot("fate", plugin.Session.Territory, SpotKind.Other, 0, f.Position)) : "座標尚未就緒",
                    $"{(f.Preparing ? "準備中" : $"進度 {f.Progress}%")}" + (f.EndsAt is { } end ? $" · 剩餘 {Math.Max(0, (int)(end - now).TotalMinutes):00}:{Math.Max(0, (int)(end - now).TotalSeconds) % 60:00}" : ""),
                    plugin.Active && Coordinates.IsFinite(f.Position))).ToArray()), Waymarks: plugin.WaymarkState(),
            AutoOpenNearbyChests: config.AutoOpenNearbyChests, AutoChestDetail: plugin.AutoChestDetail, PhantomJobs: plugin.PhantomJobState, Loot: plugin.LootState,
            PotOverlay: new(config.ShowPotCountdownOverlay),
            PatrolOverlay: new(config.ShowPatrolOverlay), AutoPatrol: plugin.AutoPatrolState,
            PhantomOverlay: new(config.ShowPhantomJobOverlay),
            Carrots: plugin.CarrotState, CarrotDisplay: plugin.CarrotDisplayOptions), actions);
    }

    private CompassFateState FateState()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = plugin.PotFates.Snapshot(now);
        static string Clock(TimeSpan remaining)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
        var points = snapshot.Active.Select(f => new CompassFatePoint(f.Definition.Id, $"{f.Definition.Side} · {f.Name}",
            Coordinates.IsFinite(f.Position) ? Plugin.MapPosition(new Spot("fate", f.Definition.Territory, SpotKind.Other, 0, f.Position)) : "座標尚未就緒",
            $"{(f.Preparing ? "準備中" : $"進度 {f.Progress}%")}{(f.EndsAt is { } end ? $" · 剩餘 {Clock(end - now)}" : "")}", Coordinates.IsFinite(f.Position))).ToArray();
        var countdown = snapshot.ExpectedAt is not { } expected ? "--:--" : expected > now ? Clock(expected - now) : "等待出現";
        var next = snapshot.Next is { } definition ? $"{definition.Side} · {definition.Name}" : "下一場尚未確定";
        var detail = snapshot.ExpectedAt is null ? "尚無本場紀錄，偵測到魔法罐 FATE 後自動開始倒數。" : snapshot.ExpectedAt <= now
            ? "已到預估時間；等待實際偵測，不會直接宣告 FATE 出現。"
            : snapshot.IsSharedEstimate ? $"預估 {snapshot.ExpectedAt.Value.ToLocalTime():HH:mm:ss} · 進島共享時間，由本機倒數。"
            : snapshot.UsesGameStart ? $"預估 {snapshot.ExpectedAt.Value.ToLocalTime():HH:mm:ss} · 依上次 FATE 開始時間加 30 分鐘。"
            : "依首次偵測時間加 30 分鐘推估，可能晚於實際出現時間。";
        if (!snapshot.ScanFresh && snapshot.ExpectedAt is not null) detail = "FATE 偵測暫停；" + detail;
        var locations = PotFateTracker.Definitions.Where(d => d.Territory == plugin.Session.Territory).Select(d =>
        {
            var location = plugin.PotFateLocation(d.Id);
            return new CompassFatePoint(d.Id, location?.Name ?? $"{(d.Side == "北側" ? "北罐" : "南罐")} · {d.Name}",
                location is null ? "座標資料尚未載入" : Plugin.MapPosition(location), "固定 FATE 地點", location is not null);
        }).ToArray();
        return new(plugin.Config.NotifyPotFateSpawn, countdown, next, detail, points, locations, plugin.Config.NotifyPotFateSoon,
            plugin.Config.FetchPotTimeOnEntry, plugin.PotTimeSyncDetail, plugin.PotTimeDebugState());
    }

    private void SetFilters(CompassFilters filters)
    {
        plugin.Config.IncludeCarrots = filters.Carrots;
        plugin.Config.IncludeSilver = filters.Silver;
        plugin.Config.IncludeBronze = filters.Bronze;
        plugin.Config.IncludeSpecial = filters.Special;
        plugin.Config.IncludeTower = filters.Tower;
        plugin.Config.IncludeExploration = filters.Exploration;
        plugin.Config.OnlyUnexplored = filters.OnlyUnexplored;
        plugin.SaveFilters();
    }

    private void SetWorldHints(bool enabled)
    {
        plugin.Config.ShowWorldHints = enabled;
        Plugin.PluginInterface.SavePluginConfig(plugin.Config);
    }

    internal void DrawWorldHints()
    {
        if (!plugin.Active || !plugin.Config.ShowWorldHints || Plugin.GameGui.GameUiHidden) return;
        var draw = ImGui.GetBackgroundDrawList();
        var labelBounds = new List<(Vector2 Min, Vector2 Max)>();
        var scale = ImGui.GetFontSize() / 17f;
        if (plugin.Pot.Active && plugin.Pot.Target is { } target && Plugin.GameGui.WorldToScreen(target + Vector3.UnitY, out var potScreen))
        {
            var color = plugin.Pot.Revealed ? Mint : Carrot;
            draw.AddCircle(potScreen, 16 * scale, Pack(color), 32, 2 * scale);
            var label = plugin.Pot.Revealed ? "魔法罐 · 附近現身寶箱" : "魔法罐搜尋點 · 待確認";
            var start = potScreen + new Vector2(20, -12) * scale;
            var end = start + ImGui.CalcTextSize(label) + new Vector2(12, 10) * scale;
            draw.AddRectFilled(start, end, Pack(Alpha(Background, 0.94f)), 5 * scale);
            draw.AddText(start + new Vector2(6, 5) * scale, Pack(color), label);
            labelBounds.Add((start, end));
        }
        foreach (var point in plugin.Filtered().Where(s => s.Status is SpotStatus.Visible or SpotStatus.Location)
                     .OrderBy(s => Vector3.DistanceSquared(plugin.Position, s.Spot.Position)).Take(24))
        {
            if (!Plugin.GameGui.WorldToScreen(point.Spot.Position + Vector3.UnitY, out var screen)) continue;
            var color = CompassView.KindColor(point.Spot.Kind);
            draw.AddCircleFilled(screen, 11 * scale, Pack(Alpha(Background, 0.88f)));
            CompassView.DrawPin(draw, screen, 7 * scale, color);
            var text = $"{point.Spot.Name ?? KindName(point.Spot.Kind)}{(point.Spot.RequiresTower ? " · 塔內" : "")}  {Vector3.Distance(plugin.Position, point.Spot.Position):F0} m";
            var start = screen + new Vector2(17, -13) * scale;
            var end = start + ImGui.CalcTextSize(text) + new Vector2(18, 12) * scale;
            if (labelBounds.Any(r => end.X > r.Min.X && start.X < r.Max.X && end.Y > r.Min.Y && start.Y < r.Max.Y)) continue;
            labelBounds.Add((start, end));
            draw.AddRectFilled(start, end, Pack(Alpha(Background, 0.92f)), 5 * scale);
            draw.AddRect(start, end, Pack(Alpha(color, 0.45f)), 5 * scale);
            draw.AddText(start + new Vector2(9, 6) * scale, Pack(color), text);
        }
        if (!plugin.IsPaused && !plugin.PotNavigationActive && !plugin.FateNavigationActive && plugin.Remaining.FirstOrDefault() is { } next &&
            Plugin.GameGui.WorldToScreen(next.Position + Vector3.UnitY, out var to))
        {
            draw.AddCircle(to, 14 * scale, Pack(Mint), 24, 2 * scale);
            var label = ChestChart.Number(next) is { } number ? $"下一站 · 圖表 #{number:00}" : "下一站 · 步行巡查目標";
            var origin = to + new Vector2(18, 23) * scale;
            draw.AddRectFilled(origin - new Vector2(6, 4) * scale, origin + ImGui.CalcTextSize(label) + new Vector2(6, 4) * scale, Pack(Alpha(Background, 0.92f)), 4 * scale);
            draw.AddText(origin, Pack(Mint), label);
        }
        if (!plugin.PotNavigationActive && plugin.CurrentLeg is { } leg)
            for (var i = 1; i < leg.Path.Count; i++)
            {
                var a = leg.Path[i - 1]; var b = leg.Path[i];
                if (Vector3.Distance(plugin.Position, a) > 250 || Vector3.Distance(plugin.Position, b) > 250) continue;
                if (!Plugin.GameGui.WorldToScreen(a + Vector3.UnitY * 0.2f, out var from) ||
                    !Plugin.GameGui.WorldToScreen(b + Vector3.UnitY * 0.2f, out var destination)) continue;
                draw.AddLine(from, destination, Pack(Alpha(Mint, 0.75f)), 2 * scale);
            }
    }
}
