using System.Numerics;
using System.Runtime.InteropServices;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;
using HexaGen.Runtime;

unsafe
{
    var output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/previews");
    Directory.CreateDirectory(output);
    using var native = new NativeLibraryContext(Path.Combine(AppContext.BaseDirectory, "cimgui.dll"));
    ImGui.InitApi(native);
    var context = ImGui.CreateContext();
    try
    {
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.LogFilename = null;
        io.DeltaTime = 1 / 60f;
        var config = ImGui.ImFontConfig();
        config.OversampleH = 2;
        config.OversampleV = 2;
        ushort[] ranges = [0x20, 0xFF, 0x2000, 0x206F, 0x2190, 0x21FF, 0x3000, 0x303F, 0x4E00, 0x9FFF, 0xFF00, 0xFFEF, 0];
        fixed (ushort* glyphs = ranges)
        {
            io.Fonts.AddFontFromFileTTF("C:/Windows/Fonts/msjh.ttc", 17, config, glyphs);
            if (!io.Fonts.Build()) throw new InvalidOperationException("Unable to build preview font atlas.");
        }
        ImGui.Destroy(config);
        var textures = new Dictionary<ulong, Texture>();
        for (var i = 0; i < io.Fonts.Textures.Size; i++)
        {
            byte* pixels; int width, height;
            io.Fonts.GetTexDataAsRGBA32(i, &pixels, &width, &height);
            var bytes = new byte[width * height * 4];
            Marshal.Copy((nint)pixels, bytes, 0, bytes.Length);
            var id = (ulong)i + 1;
            io.Fonts.SetTexID(i, new ImTextureID(id));
            textures[id] = new Texture(width, height, bytes);
        }

        using var catalogStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "carrot_locations.json"));
        var catalog = SpotCatalog.Load(catalogStream);
        var origin = new Vector3(520, 98, -220);
        var route = RoutePlanner.Plan(origin, catalog.Take(12)).Stops;
        var demo = route.Select((spot, i) =>
        {
            var kind = i % 3 == 0 ? SpotKind.Silver : SpotKind.Carrot;
            return new CompassPoint(spot with { Kind = kind }, i < 3 ? SpotStatus.Visible : i == 3 ? SpotStatus.LastSeen : SpotStatus.Candidate,
                $"X {Coordinates.ToMap(spot.Position.X, 100, 0):F1} / Y {Coordinates.ToMap(spot.Position.Z, 100, 0):F1}",
                Vector3.Distance(origin, spot.Position), i < 4 ? DateTimeOffset.Now : null);
        }).ToArray();
        // Synthetic bends exercise the renderer, not a claim about real island terrain.
        var groundLegs = demo.Select((p, i) =>
        {
            var from = i == 0 ? origin : demo[i - 1].Spot.Position;
            return WalkingRoutePlanner.ValidatePath(p.Spot.Id, from, p.Spot.Position,
                [from, (from + p.Spot.Position) / 2 + new Vector3(0, 0, 40), p.Spot.Position])!;
        }).ToArray();
        demo = demo.Select((p, i) => p with { WalkingDistance = groundLegs[i].Length }).ToArray();
        var live = demo.Where(p => p.Status == SpotStatus.Visible).ToArray();
        CompassPoint NearbyChest(float distance) => live[0] with
        {
            Spot = live[0].Spot with { Position = origin + new Vector3(distance, 0, 0) }, Status = SpotStatus.LastSeen, Distance = distance,
            Coordinates = $"X {Coordinates.ToMap(origin.X + distance, 100, 0):F1} / Y {Coordinates.ToMap(origin.Z, 100, 0):F1}",
        };
        var checkingRoute = new[] { NearbyChest(30) }.Concat(live.Skip(1)).ToArray();
        var distantRoute = new[] { NearbyChest(85) }.Concat(live.Skip(1)).ToArray();
        var fateLocations = PotFateTracker.Definitions.Where(d => d.Territory == 1252).Select(d => new CompassFatePoint(d.Id,
            $"{(d.Side == "北側" ? "北罐" : "南罐")} · {d.Name}",
            $"X {Coordinates.ToMap(d.Location.X, 100, 0):F1} / Y {Coordinates.ToMap(d.Location.Z, 100, 0):F1}", "固定 FATE 地點")).ToArray();
        var state = new CompassViewState(true, "新月島南部", origin, new CompassFilters(true, true, false, PointDisplayMode.Observed), true,
            live, live, live.Length + 4, 780, true, false, "已知位置會保留本場曾見目標；遠處是否仍有寶箱需到場確認。",
            new CompassPotState(false, true, "取得魔法罐後自動開始搜尋。", 0, false, null), 4,
            Fates: new CompassFateState(true, "--:--", "下一場尚未確定", "尚無本場紀錄，偵測到魔法罐 FATE 後自動開始倒數。", [], fateLocations));
        var fateCountdown = new CompassFateState(true, "18:24", "南側 · 瑟瑟發抖的魔法甕", "預估 13:00:00 · 依上次 FATE 開始時間加 30 分鐘。", [], fateLocations);
        var fateActive = fateCountdown with { Countdown = "28:24", Active = [new CompassFatePoint(1976, "北側 · 幸福的魔法甕", "X 25.5 / Y 17.2", "進度 25% · 剩餘 13:24")] };
        using var explorationStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "exploration_locations.json"));
        var exploration = SpotCatalog.Load(explorationStream).Select((s, i) => new CompassPoint(s, i < 3 ? SpotStatus.Explored : SpotStatus.Unexplored,
            $"X {Coordinates.ToMap(s.Position.X, 100, 0):F1} / Y {Coordinates.ToMap(s.Position.Z, 100, 0):F1}", Vector3.Distance(origin, s.Position), null)).ToArray();
        var explorationState = state with { Filters = new CompassFilters(false, false, false, PointDisplayMode.Observed, false, false, true),
            Points = exploration.Skip(3).ToArray(), Route = exploration.Skip(3).ToArray(), TotalStops = exploration.Length - 3, CompletedStops = 0,
            ExplorationDetail = "島上探索紀錄 · 未探索 9 / 12 · 已完成 3", Fates = null,
            TreasureSurvey = new TreasureSurvey(2, 10, DateTimeOffset.Now), Message = "已完成的 3 處探索筆記已隱藏。此圖使用離線示範資料。" };
        state = state with { GroundLegs = groundLegs.Take(live.Length).ToArray(), Exact = false };
        var walkingState = state with { Fates = null, Route = demo, Points = demo, GroundLegs = groundLegs, TotalStops = demo.Length,
            PlannedDistance = groundLegs.Sum(l => l.Length), CompletedStops = 0, Message = "已依地面路徑完成排序。此圖使用離線示範資料。" };
        var liveWalking = walkingState with { Route = demo.Select((p, i) => p with { LivePath = i == 0 }).ToArray(), Exact = true,
            Message = "已從傳送落點接續，巡查紀錄保留。下一站步行路段隨角色位置更新。" };
        using var chestStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "treasure_locations.json"));
        var chestCatalog = SpotCatalog.Load(chestStream);
        var chartPoints = ChestChart.Order(chestCatalog, 1).Select(s => new CompassPoint(s, SpotStatus.Candidate,
            $"X {Coordinates.ToMap(s.Position.X, 100, 0):F1} / Y {Coordinates.ToMap(s.Position.Z, 100, 0):F1}",
            Vector3.Distance(origin, s.Position), null, ChartNumber: ChestChart.Number(s))).ToArray();
        var chartControls = new CompassRouteControls(true, 24, false, new(ChestChart.Id, 23, DateTimeOffset.Now, false), chartPoints);
        var chartState = state with { Fates = null, Route = chartPoints.Skip(23).Concat(chartPoints.Take(23)).ToArray(), Points = chartPoints,
            GroundLegs = [], TotalStops = 68, CompletedStops = 0, SkippedStops = 0, Controls = chartControls,
            Message = "從圖表 #24 出發，24 → 68 → 1 → 23。此圖為離線介面預覽，未模擬地形路徑。", NavigationDetail = "地形路段待計算", RouteChanged = false };
        var actionCount = 0;
        void ActionCalled() => actionCount++;
        var actions = new CompassActions(_ => ActionCalled(), _ => ActionCalled(), ActionCalled, _ => ActionCalled(), ActionCalled, ActionCalled,
            SetPotAutoFlag: _ => ActionCalled(), FlagPot: ActionCalled, SetPotFateNotify: _ => ActionCalled(), SetHideOtherPlayers: _ => ActionCalled(),
            ConfirmOpened: ActionCalled, SetCeTracking: _ => ActionCalled(), ClearCeCooldowns: ActionCalled);
        var ceNow = new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.FromHours(8));
        var ceEntries = CeCooldownTracker.Definitions.Select((d, i) => i switch
        {
            0 => new CeCooldownEntry(d, CeStatus.Battle, 45, ceNow, null, null),
            1 => new CeCooldownEntry(d, CeStatus.Register, 0, ceNow, null, null),
            2 or 4 => new CeCooldownEntry(d, CeStatus.Cooldown, 100, ceNow.AddMinutes(-23), ceNow.AddMinutes(-23), ceNow.AddMinutes(-23) + d.Cooldown),
            5 => new CeCooldownEntry(d, CeStatus.Eligible, 100, ceNow.AddHours(-3), ceNow.AddHours(-3), ceNow.AddHours(-3) + d.Cooldown),
            6 => new CeCooldownEntry(d, CeStatus.EndUnobserved, 10, ceNow.AddMinutes(-5), null, null),
            _ => new CeCooldownEntry(d, CeStatus.Unknown, 0, null, null, null),
        }).ToArray();
        var ceState = state with { Ce = new CompassCeState(true, new(true, ceEntries), ceNow) };
        foreach (var scenario in new[]
        {
            (Name: "ce-cooldowns", Width: 960, Height: 1080, Scale: 1f, State: ceState),
            (Name: "ce-compact", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "ce-scaled", Width: 1020, Height: 1380, Scale: 1.5f, State: ceState),
            (Name: "ce-unknown", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Ce = new(true, new CeCooldownTracker().Snapshot(ceNow), ceNow) }),
            (Name: "ce-transit", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Active = false, Transit = true }),
            (Name: "ce-disabled", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Ce = ceState.Ce! with { Enabled = false } }),
            (Name: "ce-north", Width: 690, Height: 740, Scale: 1f, State: ceState with { Region = "新月島北部" }),
            (Name: "menu-navigation", Width: 960, Height: 1000, Scale: 1f, State: ceState),
            (Name: "menu-settings", Width: 690, Height: 740, Scale: 1f, State: state),
            (Name: "chart-route", Width: 960, Height: 1300, Scale: 1f, State: chartState),
            (Name: "chart-compact", Width: 690, Height: 1550, Scale: 1f, State: chartState),
            (Name: "chart-scaled", Width: 1400, Height: 2000, Scale: 1.5f, State: chartState),
            (Name: "chart-paused", Width: 690, Height: 1550, Scale: 1f, State: chartState with { Controls = chartControls with { Paused = true }, Planning = true, AutomationDetail = "使用者已暫停，停止換站與自動換旗；魔法罐追蹤另行運作。", NavigationDetail = "路線已暫停；按繼續後接回保留的站點。" }),
            (Name: "chart-stopped", Width: 960, Height: 1300, Scale: 1f, State: chartState with { Route = [], TotalStops = 0, Message = "已終止巡查，上次開箱紀錄保留。", Controls = chartControls with { LastOpen = chartControls.LastOpen! with { Number = 68, Manual = true } } }),
            (Name: "chart-zoomed", Width: 960, Height: 1300, Scale: 1f, State: chartState),
            (Name: "chart-transit-paused", Width: 690, Height: 1120, Scale: 1f, State: chartState with { Active = false, Transit = true, Planning = true, Controls = chartControls with { Paused = true } }),
            (Name: "chart-transit-stopped", Width: 690, Height: 1120, Scale: 1f, State: chartState with { Active = false, Transit = true, Route = [], TotalStops = 0 }),
            (Name: "wide", Width: 960, Height: 940, Scale: 1f, State: state),
            (Name: "player-visibility", Width: 960, Height: 1200, Scale: 1f, State: state with { HideOtherPlayers = true, PlayerVisibilityDetail = "已隱藏 48 名玩家模型 · 保留自己、小隊、好友及倒地者" }),
            (Name: "player-visibility-compact", Width: 690, Height: 1400, Scale: 1f, State: state with { HideOtherPlayers = true, PlayerVisibilityDetail = "已隱藏 48 名玩家模型 · 保留自己、小隊、好友及倒地者" }),
            (Name: "player-visibility-waiting", Width: 690, Height: 1100, Scale: 1f, State: state with { Active = false, HideOtherPlayers = true, PlayerVisibilityDetail = "等待進入新月島；傳送、過場與合照模式暫停隱藏。", Points = [], Route = [], TotalStops = 0 }),
            (Name: "walking-route", Width: 960, Height: 1120, Scale: 1f, State: walkingState),
            (Name: "walking-compact", Width: 690, Height: 1400, Scale: 1f, State: walkingState),
            (Name: "walking-scaled", Width: 1400, Height: 1800, Scale: 1.5f, State: walkingState),
            (Name: "teleport-paused", Width: 690, Height: 1050, Scale: 1f, State: walkingState with { Active = false, Transit = true, Planning = true, CompletedStops = 2, SkippedStops = 1, Route = demo.Skip(3).ToArray(), GroundLegs = [],
                NavigationDetail = "路線已保留；等待落地與地形導航就緒後重算。", AutomationDetail = "傳送中，暫停巡查判定。" }),
            (Name: "teleport-resumed", Width: 960, Height: 1140, Scale: 1f, State: liveWalking),
            (Name: "live-walking-compact", Width: 690, Height: 1450, Scale: 1f, State: liveWalking),
            (Name: "walking-planning", Width: 690, Height: 1400, Scale: 1f, State: walkingState with { Planning = true, NavigationDetail = "比較全部站間步行路程 · 0 / 12 站 · 已查詢 87 條路徑", AutomationDetail = "地形路線計算中，暫停巡查判定。" }),
            (Name: "walking-unavailable", Width: 690, Height: 1200, Scale: 1f, State: walkingState with { Route = [], GroundLegs = [], TotalStops = 0, NavigationDetail = "vnavmesh 正在準備地圖，請就緒後再規劃。", Message = "導航未就緒；尚未產生步行路線。" }),
            (Name: "compact", Width: 690, Height: 960, Scale: 1f, State: state),
            (Name: "scaled", Width: 1400, Height: 1380, Scale: 1.5f, State: state),
            (Name: "waiting", Width: 960, Height: 640, Scale: 1f, State: state with { Active = false, Points = [], Route = [], TotalStops = 0 }),
            (Name: "fate-active", Width: 960, Height: 1300, Scale: 1f, State: state with { Fates = fateActive }),
            (Name: "fate-compact", Width: 690, Height: 1400, Scale: 1f, State: state with { Fates = fateActive }),
            (Name: "fate-scaled", Width: 1400, Height: 1800, Scale: 1.5f, State: state with { Fates = fateActive }),
            (Name: "fate-countdown", Width: 960, Height: 1150, Scale: 1f, State: state with { Fates = fateCountdown }),
            (Name: "fate-overdue", Width: 690, Height: 1300, Scale: 1f, State: state with { Fates = fateCountdown with { Countdown = "等待出現", Detail = "已到預估時間；等待實際偵測，不會直接宣告 FATE 出現。" } }),
            (Name: "pot", Width: 960, Height: 1140, Scale: 1f, State: state with { Pot = new CompassPotState(true, true, "方向 東北 · 不遠 · 剩餘 3 個候選，尚未確認寶箱。", 3, false, "X 27.1 / Y 18.2", "已自動標示搜尋候選點；使用聖靈藥後繼續更新。"), AutomationDetail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" }),
            (Name: "pot-compact", Width: 690, Height: 1320, Scale: 1f, State: state with { Pot = new CompassPotState(true, true, "已取得尋寶狀態。先標搜尋候選點；使用聖靈藥後會依方向縮小範圍。", 80, false, "X 27.1 / Y 18.2", "已自動標示搜尋候選點；使用聖靈藥後繼續更新。"), AutomationDetail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" }),
            (Name: "pot-scaled", Width: 1400, Height: 1650, Scale: 1.5f, State: state with { Pot = new CompassPotState(true, true, "財寶提示後偵測到附近寶箱，已改用物件實際座標。", 1, true, "X 27.1 / Y 18.2", "已自動標示現身寶箱；等待下一次提示。"), AutomationDetail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" }),
            (Name: "pot-waiting", Width: 690, Height: 1320, Scale: 1f, State: state with { Pot = new CompassPotState(true, true, "收到發現財寶提示，正在確認附近可互動的寶箱。", 1, false, null, "等待有效提示或現身寶箱，再自動更新旗標。"), AutomationDetail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" }),
            (Name: "pot-retry", Width: 690, Height: 1320, Scale: 1f, State: state with { Pot = new CompassPotState(true, true, "方向 東北 · 不遠 · 剩餘 3 個候選，尚未確認寶箱。", 3, false, "X 27.1 / Y 18.2", "插旗暫未成功，會自動重試。"), AutomationDetail = "魔法罐尋寶中，暫停一般巡查與自動換旗。" }),
            (Name: "empty-current", Width: 960, Height: 1140, Scale: 1f, State: state with { Filters = state.Filters with { DisplayMode = PointDisplayMode.Visible }, Points = [], Route = live.Select(p => p with { Status = SpotStatus.LastSeen }).ToArray(), TotalStops = 3, CompletedStops = 0, Message = "目標已離開載入範圍。已規劃站點保留，靠近後確認箱子狀態。" }),
            (Name: "exploration", Width: 960, Height: 1140, Scale: 1f, State: explorationState),
            (Name: "exploration-compact", Width: 690, Height: 1400, Scale: 1f, State: explorationState),
            (Name: "exploration-scaled", Width: 1400, Height: 1740, Scale: 1.5f, State: explorationState with { Route = exploration.Skip(3).Reverse().ToArray() }),
            (Name: "exploration-all", Width: 960, Height: 1140, Scale: 1f, State: explorationState with { Filters = explorationState.Filters with { OnlyUnexplored = false }, Points = exploration, Route = exploration, TotalStops = 12, Message = "已取消未探索篩選，可查看遊戲內完成的固定地點。離線示範資料。" }),
            (Name: "exploration-unknown", Width: 690, Height: 1200, Scale: 1f, State: explorationState with { Points = [], Route = [], TotalStops = 0, ExplorationDetail = "等待角色探索紀錄；未讀取前不列入未探索清單。", Message = "資料尚未就緒；不將未知當作未探索。" }),
            (Name: "exploration-finished", Width: 690, Height: 1200, Scale: 1f, State: explorationState with { Points = [], Route = [], TotalStops = 0, ExplorationDetail = "島上探索紀錄 · 未探索 0 / 12 · 已完成 12", Message = "島上 12 處筆記皆已完成，沒有未探索地點。離線示範資料。" }),
            (Name: "exploration-stale", Width: 690, Height: 1400, Scale: 1f, State: explorationState with { ExplorationDetail = "探索紀錄暫無法更新，沿用本角色上次讀取結果。" }),
            (Name: "island-known", Width: 960, Height: 1140, Scale: 1f, State: state with { Points = demo.Take(4).ToArray(), Route = demo.Take(4).ToArray(), TreasureSurvey = new TreasureSurvey(0, 0, DateTimeOffset.Now), TotalStops = 4, CompletedStops = 0 }),
            (Name: "auto-next", Width: 960, Height: 1320, Scale: 1f, State: state with { Points = checkingRoute, Route = checkingRoute, SkippedStops = 2, TotalStops = live.Length + 6, AutomationDetail = "箱體持續不可選取 · 確認 1.5 / 3.0 秒", Message = "已略過 2 處沒有可用寶箱的地點，下一站旗標已更新。" }),
            (Name: "auto-finished", Width: 690, Height: 1250, Scale: 1f, State: state with { Points = [], Route = [], TotalStops = 3, CompletedStops = 2, SkippedStops = 1, Message = "本輪巡查結束：已巡查 2 站，略過 1 個空點。" }),
            (Name: "auto-distance", Width: 690, Height: 1540, Scale: 1f, State: state with { Points = distantRoute, Route = distantRoute, TotalStops = 3, CompletedStops = 0, EmptyCheckRadius = 60, AutomationDetail = "距目標 85 m／判定 60 m，高差 0.0 m／上限 8 m。", Message = "保留巡查紀錄；優先續巡上輪 3 個未巡查點，共 3 站。 已更新首站旗標。" }),
        })
        {
            var view = new CompassView { Page = scenario.Name switch
            {
                var name when name.StartsWith("ce-") => CompassPage.Ce,
                var name when name.StartsWith("pot") || name.StartsWith("fate") => CompassPage.Pot,
                var name when name.StartsWith("exploration") => CompassPage.Exploration,
                var name when name.StartsWith("player-visibility") || name == "menu-settings" => CompassPage.Settings,
                _ => CompassPage.Patrol,
            } };
            if (args.Length > 1 && !scenario.Name.StartsWith(args[1], StringComparison.Ordinal)) continue;
            io.FontGlobalScale = scenario.Scale;
            io.DisplaySize = new Vector2(scenario.Width, scenario.Height);
            io.MousePos = new Vector2(-1000);
            float scrollBefore = 0, scrollAfter = 0;
            var beforeActions = actionCount;
            CompassPage[] navigation = [CompassPage.Ce, CompassPage.Exploration, CompassPage.Settings, CompassPage.Pot, CompassPage.Patrol];
            for (var frame = 0; frame < (scenario.Name == "menu-navigation" ? 23 : scenario.Name == "chart-zoomed" ? 7 : 3); frame++)
            {
                if (scenario.Name == "menu-navigation" && frame >= 3)
                {
                    var index = (frame - 3) / 4;
                    var step = (frame - 3) % 4;
                    if (step == 0) io.AddMousePosEvent(view.MenuTargets.Single(t => t.Page == navigation[index]).Center.X, view.MenuTargets.Single(t => t.Page == navigation[index]).Center.Y);
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                    if (step == 3 && view.Page != navigation[index]) throw new InvalidOperationException($"Menu click failed: {navigation[index]}");
                }
                if (scenario.Name == "chart-zoomed")
                {
                    if (frame == 3) io.MousePos = view.MapArea.Origin + view.MapArea.Size * new Vector2(0.65f, 0.55f);
                    if (frame == 4) { io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddMouseWheelEvent(0, 3); }
                    if (frame == 5) io.AddKeyEvent(ImGuiKey.ModCtrl, false);
                    if (frame == 6) io.MousePos = new Vector2(-1000);
                }
                ImGui.NewFrame();
                using (new CompassTheme())
                {
                    ImGui.SetNextWindowPos(new Vector2(20));
                    ImGui.SetNextWindowSize(new Vector2(scenario.Width - 40, scenario.Height - 40));
                    if (ImGui.Begin($"新月島尋寶羅盤 · 示範資料##{scenario.Name}", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                    {
                        view.Draw(scenario.State, actions);
                        if (frame == 3) scrollBefore = view.PageScroll;
                        if (frame == 6) scrollAfter = view.PageScroll;
                    }
                    ImGui.End();
                }
                ImGui.Render();
            }
            if (scenario.Name == "menu-navigation" && actionCount != beforeActions)
                throw new InvalidOperationException("Changing feature pages must not change filters, routing, or tracking settings.");
            if (scenario.Name == "chart-zoomed" && (view.MapZoom < 1.5f || MathF.Abs(scrollAfter - scrollBefore) > 0.1f))
                throw new InvalidOperationException($"Ctrl-wheel must zoom map without scrolling parent: zoom={view.MapZoom}, scroll={scrollBefore}->{scrollAfter}");
            var path = Path.Combine(output, $"{scenario.Name}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, scenario.Width, scenario.Height, path);
            Console.WriteLine($"Rendered {scenario.Name}: {scenario.Width}x{scenario.Height}, {ImGui.GetDrawData().TotalVtxCount} vertices -> {path}");
        }
    }
    finally { ImGui.DestroyContext(context); }
}
