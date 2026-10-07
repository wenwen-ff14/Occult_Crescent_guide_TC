using System.Numerics;
using System.Runtime.InteropServices;
using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;
using HexaGen.Runtime;

unsafe
{
    var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ui-preview"));
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

        // Optional local game textures exported by PhantomAudit; never bundled as standalone assets.
        var iconDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/phantom-icons"));
        foreach (var job in PhantomJobs.All)
        {
            var iconFile = Path.Combine(iconDirectory, $"{job.IconId}.rgba");
            if (!File.Exists(iconFile)) continue;
            using var reader = new BinaryReader(File.OpenRead(iconFile));
            var width = reader.ReadInt32(); var height = reader.ReadInt32();
            textures[job.IconId] = new(width, height, reader.ReadBytes(width * height * 4));
        }
        using var catalogStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "carrot_locations.json"));
        var ceTextureDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ce-map-textures"));
        var ceTextureIds = new Dictionary<string, ulong>();
        foreach (var (name, gamePath, textureId) in new[] {
            ("map-967", CeMapCatalog.TexturePath, 9000967UL),
            ("63909", "ui/icon/063000/063909.tex", 63909UL),
            ("63911", "ui/icon/063000/063911.tex", 63911UL) })
        {
            var file = Path.Combine(ceTextureDirectory, name + ".rgba");
            if (!File.Exists(file)) continue;
            using var reader = new BinaryReader(File.OpenRead(file));
            var width = reader.ReadInt32(); var height = reader.ReadInt32();
            textures[textureId] = new(width, height, reader.ReadBytes(width * height * 4));
            ceTextureIds[gamePath] = textureId;
        }
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
        var previewVersion = System.Xml.Linq.XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../CrescentCompass/CrescentCompass.csproj")))
            .Descendants("Version").Single().Value;
        var state = new CompassViewState(true, "新月島南部", origin, new CompassFilters(true, true, false, PointDisplayMode.Observed), true,
            live, live, live.Length + 4, 780, true, false, "已知位置會保留本場曾見目標；遠處是否仍有寶箱需到場確認。",
            new CompassPotState(false, true, "取得魔法罐後自動開始搜尋。", 0, false, null), 4,
            Fates: new CompassFateState(true, "--:--", "下一場尚未確定", "尚無本場紀錄，偵測到魔法罐 FATE 後自動開始倒數。", [], fateLocations), PluginVersion: previewVersion);
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
        using var bocchiStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "treasure_route.json"));
        using var bocchiDistances = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "precomputed_treasure_hunt_data.json"));
        var bocchi = BocchiPatrolRoute.Load(bocchiStream, bocchiDistances, chestCatalog);
        var chartPoints = ChestChart.Order(chestCatalog, 1).Select(s => new CompassPoint(s, SpotStatus.Candidate,
            $"X {Coordinates.ToMap(s.Position.X, 100, 0):F1} / Y {Coordinates.ToMap(s.Position.Z, 100, 0):F1}",
            Vector3.Distance(origin, s.Position), null, ChartNumber: ChestChart.Number(s))).ToArray();
        var chartControls = new CompassRouteControls(true, 24, false, new(ChestChart.Id, 23, DateTimeOffset.Now, false), chartPoints);
        var chartState = state with { Fates = null, Route = chartPoints.Skip(23).Concat(chartPoints.Take(23)).ToArray(), Points = chartPoints,
            GroundLegs = [], TotalStops = 68, CompletedStops = 0, SkippedStops = 0, Controls = chartControls,
            Message = "從圖表 #24 出發，24 → 68 → 1 → 23。此圖為離線介面預覽，未模擬地形路徑。", NavigationDetail = "地形路段待計算", RouteChanged = false };
        var actionCount = 0;
        var bocchiStops = bocchi.Order(24);
        var bocchiPoints = bocchiStops.Select(s => chartPoints.Single(p => p.Spot.Id == s.Id)).ToArray();
        var bocchiState = chartState with { Route = bocchiPoints,
            Controls = chartControls with { RouteKind = PatrolRouteKind.Bocchi, NextChartNumber = bocchi.NextChartNumber(23),
                RouteDetail = $"BOCCHI · 區段 1/7 · base-camp · 站間參考 {bocchi.ReferenceCosts(bocchiStops)!.Distance / 1000:F1} km（66/67 段）" },
            Message = "依 BOCCHI 分區順序巡查，跨區段使用地面尋路。", AutoPatrol = new(false, true, "未啟動") };
        PatrolRouteKind? selectedPatrolRoute = null;
        List<PatrolRouteKind> patrolRouteCalls = [];
        var carrotWeights = new CarrotSearchWeights();
        var carrotPads = CarrotRoute.Order(catalog);
        carrotWeights.Reset(carrotPads);
        foreach (var pad in carrotPads.Take(4)) carrotWeights.CheckEmpty(pad.Id);
        carrotWeights.ConfirmPickup(carrotPads[4].Id);
        var carrotPoints = carrotPads.Select((s, i) => new CompassPoint(s, i < 5 ? SpotStatus.Visited : SpotStatus.Candidate,
            $"X {Coordinates.ToMap(s.Position.X, 100, 0):F1} / Y {Coordinates.ToMap(s.Position.Z, 100, 0):F1}",
            Vector3.Distance(origin, s.Position), null, ChartNumber: CarrotRoute.Number(s), CarrotWeight: carrotWeights.Weight(s.Id))).ToArray();
        var carrotState = chartState with { Route = carrotPoints.Skip(5).ToArray(), Points = carrotPoints, TotalStops = 25, CompletedStops = 5,
            Controls = new(true, 1, false, null, carrotPoints, RouteKind: PatrolRouteKind.Carrot, RouteDetail: "蘿蔔圖表 1～25 · 固定巡航 · 權重 0 / +1 / +2"),
            Carrots = new(1, "已確認使用蘿蔔。", true), AutoPatrol = new(false, true, "未啟動"),
            Message = "蘿蔔 #5 已拾取，先前空點 +1；尚未探過的點 +2。" };
        List<string> carrotConfirmations = [];
        var carrotResets = 0;
        var potOverlayOptions = new CompassPotOverlayOptions(false);
        var patrolOverlayOptions = new CompassPatrolOverlayOptions(false);
        var phantomOverlayOptions = new CompassPhantomOverlayOptions(false);
        List<bool> phantomOverlayCalls = [];
        var fetchPotTime = true;
        List<bool> fetchPotTimeCalls = [];
        List<string> potDebugCopies = [];
        var potDebugRetries = 0;
        List<string> patrolOverlayCalls = [];
        List<string> potOverlayCalls = [];
        var waymarkCalls = new List<string>();
        string importedWaymarks = "";
        Guid lastPlacedWaymark = Guid.Empty;
        bool? ignoreDistanceSetting = null;
        bool? autoChestSetting = null;
        List<bool> autoChestCalls = [];
        bool? autoPatrolSetting = null;
        List<bool> autoPatrolCalls = [];
        List<byte> phantomSwitchCalls = [];
        List<string> phantomCopies = [];
        var phantomIconRefreshes = 0;
        List<(ushort Id, bool Trigger)> ceFlags = [];
        void ActionCalled() => actionCount++;
        var actions = new CompassActions(_ => ActionCalled(), _ => ActionCalled(), ActionCalled, _ => ActionCalled(), ActionCalled, ActionCalled,
            SetPotAutoFlag: _ => ActionCalled(), FlagPot: ActionCalled, SetPotFateNotify: _ => ActionCalled(), SetHideOtherPlayers: _ => ActionCalled(),
            SetPotFateSoonNotify: _ => ActionCalled(),
            ConfirmOpened: ActionCalled, SetCeTracking: _ => ActionCalled(), ClearCeCooldowns: ActionCalled,
            SetFateAutoFlag: _ => ActionCalled(), ReleaseFateNavigation: ActionCalled, FlagGeneralFate: _ => ActionCalled(),
            Waymarks: new(_ => { ActionCalled(); waymarkCalls.Add("save"); }, (json, _) => { ActionCalled(); importedWaymarks = json; waymarkCalls.Add("import"); },
                id => { ActionCalled(); lastPlacedWaymark = id; waymarkCalls.Add("place"); }, _ => { ActionCalled(); waymarkCalls.Add("delete"); },
                (_, _) => ActionCalled(), _ => null, () => { ActionCalled(); waymarkCalls.Add("cancel"); },
                value => { ActionCalled(); ignoreDistanceSetting = value; waymarkCalls.Add(value ? "distance-on" : "distance-off"); }),
            SetAutoOpenNearbyChests: value => { ActionCalled(); autoChestSetting = value; autoChestCalls.Add(value); },
            SetAutoPatrol: value => { ActionCalled(); autoPatrolSetting = value; autoPatrolCalls.Add(value); },
            SwitchPhantomJob: id => phantomSwitchCalls.Add(id), CopyPhantomMacro: text => phantomCopies.Add(text),
            DrawPhantomJobIcon: (id, size) => { if (!textures.ContainsKey(id)) return false; ImGui.Image(new ImTextureID(id), size); return true; },
            RefreshPhantomMacroIcons: () => phantomIconRefreshes++, Loot: new((_, _) => ActionCalled(), _ => ActionCalled(), _ => ActionCalled()),
            GetGameTexture: path => ceTextureIds.GetValueOrDefault(path), FlagCeLocation: (id, trigger) => ceFlags.Add((id, trigger)),
            PotOverlay: new(value => { potOverlayOptions = potOverlayOptions with { Enabled = value }; potOverlayCalls.Add("visible-" + value); }),
            PatrolOverlay: new(value => { patrolOverlayOptions = patrolOverlayOptions with { Enabled = value }; patrolOverlayCalls.Add("visible-" + value); }),
            PhantomOverlay: new(value => { phantomOverlayOptions = phantomOverlayOptions with { Enabled = value }; phantomOverlayCalls.Add(value); }),
            SetFetchPotTimeOnEntry: value => { fetchPotTime = value; fetchPotTimeCalls.Add(value); }, CopyPotTimeDebug: potDebugCopies.Add,
            SetPatrolRoute: value => { ActionCalled(); selectedPatrolRoute = value; patrolRouteCalls.Add(value); },
            RetryPotTime: () => potDebugRetries++, ConfirmCarrotPickup: carrotConfirmations.Add, ResetCarrotWeights: () => carrotResets++);
        var demoWaymark = new WaymarkPreset(Guid.NewGuid(), "南部 · 戰鬥集合點（示範）", 1252,
            new DateTimeOffset(2026, 10, 3, 18, 25, 0, TimeSpan.FromHours(8)),
            Enumerable.Range(0, 8).Select(i => new SavedWaymark(120 + i * 2, 5, -240 + i * 3, i < 6)).ToArray());
        var waymarkState = state with { Waymarks = new([demoWaymark, demoWaymark with { Id = Guid.NewGuid(), Name = "塔內 · 一樓（示範）" }],
            true, true, false, "示範資料 · 選擇預設後按「放置標點」。") };
        CompassViewState TowerPreview(int index)
        {
            var arena = WaymarkPreview.Arenas[index];
            Vector2[] offsets = [new(-10, -18), new(0, -18), new(10, -18), new(10, 0), new(10, 18), new(0, 18), new(-10, 18), new(-10, 0)];
            var preset = demoWaymark with { Id = Guid.NewGuid(), Name = arena.Name + "（示範配置）",
                Markers = offsets.Select(p => new SavedWaymark(arena.Center.X + p.X, -481, arena.Center.Y + p.Y, true)).ToArray() };
            return waymarkState with { Waymarks = waymarkState.Waymarks! with { Presets = [preset] } };
        }
        var unchangedPlacement = new WaymarkPlacement();
        unchangedPlacement.Start(demoWaymark, demoWaymark.Markers.ToArray(), new(1252, 1, 1, true, false), 0);
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
        var generalFates = state with { GeneralFates = new(true, true, "已標點：幸福的魔法甕；巡查保留，事件結束或解除後接續。",
            [new(1976, "幸福的魔法甕", "X 25.5 / Y 17.2", "進度 25% · 剩餘 13:24"),
             new(1963, "一般 FATE（示範）", "X 20.0 / Y 15.0", "進度 42% · 剩餘 08:25")]) };
        using var lootStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "loot_catalog.json"));
        var lootState = state with { Loot = new(LootCleanup.Load(lootStream), new HashSet<uint> { 8143, 21057 }, new Dictionary<uint, int> { [8143] = 99, [21057] = 2, [48204] = 1 }, LootCleanupMode.Discard, false, "未啟動；先標記垃圾再啟動。", 0) };
        if (args.Length <= 1 || args[1] == "pot-overlay") PotOverlayPreview.Run(output, textures);
        if (args.Length <= 1 || args[1] == "patrol-overlay") PatrolOverlayPreview.Run(output, textures);
        if (args.Length <= 1 || args[1] is "phantom-" or "phantom-overlay") PhantomOverlayPreview.Run(output, textures);
        if (args.Length <= 1 || args[1] is "ce-" or "ce-interaction") CeInteractionPreview.Run(output, textures, ceState, actions);
        var debugKey = PotEntryTimeSync.Fingerprint(3, new(1963, 1_800_000_000));
        var debugRows = new CompassPotTimeDebugRow[]
        {
            new("診斷版本", "0.10.20.0 / pot-debug-3"), new("查詢狀態", "NotFound · 查無共享時間，等待本機觀測；不自動重試。"),
            new("共享來源", "OccultOverlay / Eureka Linker"), new("單次比對指紋數", "2 個有效 FATE；共用一個 GET"),
            new("目前阻擋條件", "無；僅進島自動查詢或手動重試"), new("HTTP / 結果", "200 / NotFound / not-found"),
            new("耗時 / 讀取量", "186 ms / 2 bytes"), new("本場請求次數", "1（自動 1 / 手動 0）；不上傳、不自動重試"),
            new("手動重試", "手動查詢一次最新共享時間；每次請求間隔至少 10 秒，不上傳本機資料。"),
            new("場次建立 (UTC)", "2026-10-07 01:00:00 UTC"), new("送出 / 完成 (UTC)", "2026-10-07 01:00:03 UTC\n2026-10-07 01:00:04 UTC"),
            new("目前區域 / 分流", "1252 / 1"), new("目前資料中心", "3 · 目前世界資料已就緒"),
            new("FATE 掃描 (UTC)", "2026-10-07 01:01:00 UTC · 掃描成功"),
            new("FATE 篩選數", "表內 12 → 有效同區 10 → 進行／準備且位置有效 8 → 時間戳有效 7"),
            new("請求使用的場次", "區域 1252 / 分流 1 / DC 3"), new("請求來源 FATE", "ID 1963 / StartUnix 1800000000"),
            new("請求指紋 SHA256", string.Join("\n", new[] { debugKey, PotEntryTimeSync.Fingerprint(3, new(1964, 1800000001)) }
                .Select(key => key[..32] + "\n" + key[32..]))),
            new("回應計時資料", "--"), new("目前倒數來源", "未知"), new("預估下次 (UTC)", "--"), new("服務", PotSharedTimeClient.Endpoint),
        };
        var debugState = state with { Fates = state.Fates! with { SharedTimeDetail = "查無共享時間，等待本機觀測；不自動重試。",
            Debug = new("OccultTrackerV3 回傳空陣列：目前副本指紋沒有共享紀錄；不是連線失敗。", debugRows, true,
                "手動查詢一次最新共享時間；每次請求間隔至少 10 秒，不上傳本機資料。") } };
        foreach (var scenario in new[]
        {
            (Name: "carrot-route", Width: 960, Height: 1700, Scale: 1f, State: carrotState),
            (Name: "carrot-route-compact", Width: 690, Height: 1400, Scale: 1f, State: carrotState),
            (Name: "carrot-route-short", Width: 690, Height: 900, Scale: 1f, State: carrotState),
            (Name: "carrot-route-scaled", Width: 1020, Height: 2200, Scale: 1.5f, State: carrotState),
            (Name: "carrot-route-switch", Width: 690, Height: 1400, Scale: 1f, State: carrotState),
            (Name: "carrot-actions", Width: 690, Height: 1400, Scale: 1f, State: carrotState),
            (Name: "carrot-stale-confirm", Width: 690, Height: 1400, Scale: 1f, State: carrotState),
            (Name: "carrot-off-island", Width: 690, Height: 1400, Scale: 1f, State: carrotState with { Active = false,
                Carrots = carrotState.Carrots! with { CanConfirm = false }, AutoPatrol = new(false, false, "請先進島。") }),
            (Name: "bocchi-route", Width: 960, Height: 1500, Scale: 1f, State: bocchiState),
            (Name: "bocchi-route-compact", Width: 690, Height: 1250, Scale: 1f, State: bocchiState),
            (Name: "bocchi-route-scaled", Width: 1020, Height: 1600, Scale: 1.5f, State: bocchiState),
            (Name: "bocchi-route-switch", Width: 690, Height: 1250, Scale: 1f, State: bocchiState),
            (Name: "bocchi-route-invalid", Width: 690, Height: 1250, Scale: 1f, State: bocchiState with {
                Controls = bocchiState.Controls! with { RouteKind = PatrolRouteKind.Chart, BocchiAvailable = false,
                    RouteDetail = "BOCCHI 路線資料驗證失敗。" } }),
            (Name: "pot-debug", Width: 960, Height: 1700, Scale: 1f, State: debugState),
            (Name: "pot-debug-compact", Width: 690, Height: 1700, Scale: 1f, State: debugState),
            (Name: "pot-debug-short", Width: 690, Height: 900, Scale: 1f, State: debugState),
            (Name: "pot-debug-scaled", Width: 1020, Height: 2000, Scale: 1.5f, State: debugState),
            (Name: "pot-debug-off-island", Width: 690, Height: 1200, Scale: 1f, State: debugState with { Active = false,
                Fates = debugState.Fates! with { Debug = debugState.Fates.Debug! with { CanRetry = false, RetryDetail = "目前不在新月島。" } } }),
            (Name: "pot-debug-collapse", Width: 690, Height: 1000, Scale: 1f, State: debugState),
            (Name: "pot-debug-retry", Width: 690, Height: 1200, Scale: 1f, State: debugState),
            (Name: "pot-debug-retry-scaled", Width: 1020, Height: 1800, Scale: 1.5f, State: debugState),
            (Name: "pot-debug-retry-cooldown", Width: 690, Height: 1200, Scale: 1f, State: debugState with {
                Fates = debugState.Fates! with { Debug = debugState.Fates.Debug! with { CanRetry = false, RetryDetail = "請等待 5 秒後重試。" } } }),
            (Name: "pot-time-sync-shared", Width: 690, Height: 1200, Scale: 1f, State: state with { Fates = fateCountdown with {
                Detail = "預估 13:00:00 · 進島共享時間，由本機倒數。", SharedTimeDetail = "已取得共享時間，後續由本機計時。" } }),
            (Name: "pot-time-sync-scaled", Width: 1020, Height: 1500, Scale: 1.5f, State: state with { Fates = fateCountdown with {
                Detail = "預估 13:00:00 · 進島共享時間，由本機倒數。", SharedTimeDetail = "已取得共享時間，後續由本機計時。" } }),
            (Name: "pot-time-sync-toggle", Width: 690, Height: 1200, Scale: 1f, State: state with { Fates = state.Fates! with {
                SharedTimeDetail = "查無共享時間，等待本機觀測；本次不重查。" } }),
            (Name: "pot-time-sync-off-island", Width: 690, Height: 1200, Scale: 1f, State: state with { Active = false, Fates = state.Fates! with {
                SharedTimeDetail = "進島後查詢一次共享時間。" } }),
            (Name: "phantom-overlay-options-settings", Width: 690, Height: 1300, Scale: 1f, State: state),
            (Name: "phantom-overlay-options-off-island", Width: 690, Height: 1300, Scale: 1f, State: state with { Active = false }),
            (Name: "phantom-jobs-overlay-options", Width: 690, Height: 1300, Scale: 1f, State: state with { PhantomJobs = new(0, true, "已切換為輔助自由人。") }),
            (Name: "auto-patrol", Width: 960, Height: 1320, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "自動前往 #24 · 距離 83 m · 快取 68 段") }),
            (Name: "auto-patrol-compact", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(false, true, "未啟動") }),
            (Name: "auto-patrol-scaled", Width: 1020, Height: 1500, Scale: 1.5f, State: chartState with { AutoPatrol = new(true, true, "已靠近寶箱，等待開箱完成。") }),
            (Name: "auto-patrol-toggle", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(false, true, "未啟動") }),
            (Name: "auto-patrol-off-island", Width: 690, Height: 980, Scale: 1f, State: chartState with { Active = false, AutoPatrol = new(false, false, "自動巡查僅支援新月島南部。") }),
            (Name: "auto-patrol-paused", Width: 690, Height: 1200, Scale: 1f, State: chartState with { Controls = chartControls with { Paused = true }, AutoPatrol = new(true, true, "巡查已暫停，停止自動移動。") }),
            (Name: "auto-patrol-blocked", Width: 690, Height: 1200, Scale: 1f, State: chartState with { Controls = chartControls with { Paused = true }, AutoPatrol = new(false, true, "找不到可接近此箱點的完整地面路徑，保留站點並停止。") }),
            (Name: "auto-patrol-recovery-query", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "正在檢查替代路徑（2/3）。 · 快取 68 段") }),
            (Name: "auto-patrol-jump", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "路程無進展，嘗試跳躍脫困（2/5）。 · 快取 68 段") }),
            (Name: "auto-patrol-airborne-scaled", Width: 1020, Height: 1500, Scale: 1.5f, State: chartState with { AutoPatrol = new(true, true, "跳躍通過障礙物；落地後確認路程進展。 · 快取 68 段") }),
            (Name: "auto-patrol-recovered", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "脫困前往 #24 · 快取 68 段") }),
            (Name: "auto-patrol-early-empty", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "自動前往 #24 · 距離 38 m · 快取 2 段"), AutomationDetail = "未偵測到可用寶箱 · 確認 0.5 / 1.0 秒", Message = "已提前確認上一點沒有可用寶箱，前往下一站。" }),
            (Name: "auto-patrol-recovery", Width: 1020, Height: 1500, Scale: 1.5f, State: chartState with { AutoPatrol = new(true, true, "移動 6 秒無進展，檢查側移／後退路徑（1/3）。 · 快取 12 段") }),
            (Name: "auto-patrol-settling", Width: 690, Height: 1200, Scale: 1f, State: chartState with { AutoPatrol = new(true, true, "靠近寶箱，等待角色停穩。 · 快取 12 段") }),
            (Name: "auto-patrol-recovery-query-scaled", Width: 1020, Height: 1500, Scale: 1.5f, State: chartState with { AutoPatrol = new(true, true, "正在檢查替代路徑（2/3）。 · 快取 68 段") }),
            (Name: "patrol-overlay-options", Width: 690, Height: 1250, Scale: 1f, State: chartState),
            (Name: "patrol-overlay-options-off-island", Width: 690, Height: 1000, Scale: 1f, State: chartState with { Active = false }),
            (Name: "patrol-overlay-options-settings", Width: 690, Height: 1300, Scale: 1f, State: chartState with { PotOverlay = new(true) }),
            (Name: "patrol-overlay-enabled", Width: 690, Height: 1450, Scale: 1f, State: chartState with { PatrolOverlay = new(true) }),
            (Name: "pot-overlay-options", Width: 690, Height: 1200, Scale: 1f, State: state),
            (Name: "pot-overlay-options-off-island", Width: 690, Height: 1000, Scale: 1f, State: state with { Active = false }),
            (Name: "pot-overlay-enabled", Width: 690, Height: 1200, Scale: 1f, State: state with { PotOverlay = new(true) }),
            (Name: "loot", Width: 940, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-compact", Width: 690, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-scaled", Width: 1020, Height: 1250, Scale: 1.5f, State: lootState),
            (Name: "loot-minions", Width: 690, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-orchestrion", Width: 690, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-category-reset", Width: 940, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-filter-combined", Width: 690, Height: 980, Scale: 1f, State: lootState),
            (Name: "loot-filter-empty", Width: 690, Height: 980, Scale: 1f, State: lootState),
            (Name: "phantom-jobs", Width: 940, Height: 980, Scale: 1f, State: state with {
                PhantomJobs = new(0, true, "示範資料 · 選擇職業切換，或複製巨集指令。") }),
            (Name: "phantom-jobs-compact", Width: 690, Height: 850, Scale: 1f, State: state with {
                PhantomJobs = new(1, true, "示範資料 · 已切換為輔助騎士。") }),
            (Name: "phantom-jobs-scaled", Width: 1020, Height: 1150, Scale: 1.5f, State: state with {
                PhantomJobs = new(0, true, "示範資料 · 已放大介面文字。") }),
            (Name: "phantom-jobs-actions", Width: 940, Height: 980, Scale: 1f, State: state with {
                PhantomJobs = new(0, true, "示範資料 · 測試複製與切換操作。") }),
            (Name: "phantom-jobs-last", Width: 690, Height: 850, Scale: 1f, State: state with {
                PhantomJobs = new(0, true, "示範資料 · 測試捲動至最後一個職業。") }),
            (Name: "phantom-jobs-off-island", Width: 940, Height: 980, Scale: 1f, State: state with {
                Active = false, PhantomJobs = new(null, false, "請進入新月島，等待職業資料載入。") }),
            (Name: "auto-chests-settings", Width: 690, Height: 740, Scale: 1f, State: state with {
                AutoOpenNearbyChests = true, AutoChestDetail = "偵測中；請靠近可開啟的寶箱（3 公尺內）。" }),
            (Name: "auto-chests-patrol", Width: 960, Height: 1100, Scale: 1f, State: chartState with {
                AutoOpenNearbyChests = true, AutoChestDetail = "已嘗試開啟；仍在附近便持續嘗試，直到遊戲確認開啟。" }),
            (Name: "auto-chests-toggle", Width: 690, Height: 740, Scale: 1f, State: state),
            (Name: "auto-chests-paused", Width: 690, Height: 740, Scale: 1f, State: state with {
                AutoOpenNearbyChests = true, AutoChestDetail = "戰鬥中，暫停開箱。" }),
            (Name: "auto-chests-scaled", Width: 1020, Height: 1000, Scale: 1.5f, State: state with {
                AutoOpenNearbyChests = true, AutoChestDetail = "附近持續開箱中（每秒最多一次）；等待遊戲更新。" }),
            (Name: "waymarks", Width: 960, Height: 940, Scale: 1f, State: waymarkState),
            (Name: "waymarks-preview-1", Width: 1100, Height: 1500, Scale: 1f, State: TowerPreview(0)),
            (Name: "waymarks-preview-2", Width: 1100, Height: 1500, Scale: 1f, State: TowerPreview(1)),
            (Name: "waymarks-preview-3", Width: 1100, Height: 1500, Scale: 1f, State: TowerPreview(2)),
            (Name: "waymarks-preview-4", Width: 1100, Height: 1500, Scale: 1f, State: TowerPreview(3)),
            (Name: "waymarks-compact", Width: 690, Height: 740, Scale: 1f, State: waymarkState),
            (Name: "waymarks-scaled", Width: 1020, Height: 1200, Scale: 1.5f, State: waymarkState),
            (Name: "waymarks-off-island", Width: 960, Height: 1000, Scale: 1f, State: waymarkState with { Active = false, Waymarks = waymarkState.Waymarks! with { CanCapture = false, CanPlace = false } }),
            (Name: "waymarks-empty", Width: 690, Height: 740, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with { Presets = [] } }),
            (Name: "waymarks-distance", Width: 690, Height: 950, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with {
                Presets = [demoWaymark], IgnoreDistance = true, Detail = "已啟用忽略距離；依預設 XYZ 放置，不校正地面高度。" } }),
            (Name: "waymarks-distance-toggle", Width: 960, Height: 1100, Scale: 1f, State: waymarkState),
            (Name: "waymarks-placement-error", Width: 690, Height: 850, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with {
                Presets = [demoWaymark], Failed = true, Detail = "標點 A：標點距離超過 200 公尺，請靠近預設位置再套用。" } }),
            (Name: "waymarks-unchanged", Width: 690, Height: 850, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with {
                Presets = [demoWaymark], Detail = unchangedPlacement.Detail } }),
            (Name: "waymarks-combat", Width: 690, Height: 850, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with {
                Presets = [demoWaymark], CanPlace = false, PlacementUnavailableReason = "目前在戰鬥中，結束戰鬥後才能放置標點。" } }),
            (Name: "waymarks-button-check", Width: 960, Height: 1000, Scale: 1f, State: waymarkState),
            (Name: "waymarks-select-new", Width: 960, Height: 1000, Scale: 1f, State: waymarkState),
            (Name: "waymarks-import-check", Width: 960, Height: 1000, Scale: 1f, State: waymarkState),
            (Name: "waymarks-cancel-check", Width: 960, Height: 1000, Scale: 1f, State: waymarkState with { Waymarks = waymarkState.Waymarks! with { CanCapture = false, CanPlace = false, Busy = true, Detail = "正在放置標點 B · 1／6" } }),
            (Name: "general-fates", Width: 960, Height: 900, Scale: 1f, State: generalFates),
            (Name: "general-fates-default", Width: 690, Height: 900, Scale: 1f, State: state with { GeneralFates = null }),
            (Name: "general-fates-disabled", Width: 690, Height: 900, Scale: 1f, State: generalFates with { GeneralFates = generalFates.GeneralFates! with { AutoFlag = false, Holding = false, Detail = "自動標點已關閉；手動插旗仍可使用。" } }),
            (Name: "general-fates-compact", Width: 690, Height: 900, Scale: 1f, State: generalFates),
            (Name: "general-fates-scaled", Width: 1020, Height: 1200, Scale: 1.5f, State: generalFates),
            (Name: "general-fates-off-island", Width: 690, Height: 900, Scale: 1f, State: generalFates with { Active = false, GeneralFates = new(true, false, "等待進入新月島。", []) }),
            (Name: "ce-cooldowns", Width: 960, Height: 1080, Scale: 1f, State: ceState),
            (Name: "ce-actions", Width: 960, Height: 1450, Scale: 1f, State: ceState),
            (Name: "ce-zoomed", Width: 960, Height: 1080, Scale: 1f, State: ceState),
            (Name: "ce-zoomed-scaled", Width: 1020, Height: 1380, Scale: 1.5f, State: ceState),
            (Name: "ce-zoomed-max", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "ce-zoomed-out", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "ce-compact", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "ce-scaled", Width: 1020, Height: 1380, Scale: 1.5f, State: ceState),
            (Name: "ce-unknown", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Ce = new(true, new CeCooldownTracker().Snapshot(ceNow), ceNow) }),
            (Name: "ce-transit", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Active = false, Transit = true }),
            (Name: "ce-disabled", Width: 690, Height: 1000, Scale: 1f, State: ceState with { Ce = ceState.Ce! with { Enabled = false } }),
            (Name: "ce-north", Width: 690, Height: 740, Scale: 1f, State: ceState with { Region = "新月島北部" }),
            (Name: "ce-off-island", Width: 690, Height: 740, Scale: 1f, State: ceState with { Active = false }),
            (Name: "menu-navigation", Width: 960, Height: 1000, Scale: 1f, State: ceState),
            (Name: "cards-navigation", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "cards-navigation-scaled", Width: 1020, Height: 1200, Scale: 1.35f, State: ceState),
            (Name: "menu-ce-open", Width: 690, Height: 1000, Scale: 1f, State: ceState),
            (Name: "menu-settings", Width: 690, Height: 740, Scale: 1f, State: state),
            (Name: "chart-route", Width: 960, Height: 1300, Scale: 1f, State: chartState),
            (Name: "chart-settings", Width: 960, Height: 1600, Scale: 1f, State: chartState),
            (Name: "chart-settings-scaled", Width: 1400, Height: 2300, Scale: 1.5f, State: chartState),
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
            (Name: "auto-distance", Width: 690, Height: 1540, Scale: 1f, State: state with { Points = distantRoute, Route = distantRoute, TotalStops = 3, CompletedStops = 0, AutomationDetail = "距目標 85 m／判定 60 m，高差 0.0 m／上限 8 m。", Message = "保留巡查紀錄；優先續巡上輪 3 個未巡查點，共 3 站。 已更新首站旗標。" }),
        })
        {
            var view = new CompassView { Page = scenario.Name switch
            {
                "patrol-overlay-options-settings" => CompassPage.Settings,
                var name when name.StartsWith("phantom-overlay-options") => CompassPage.Settings,
                var name when name.StartsWith("loot") => CompassPage.Loot,
                var name when name.StartsWith("waymarks") => CompassPage.Waymarks,
                var name when name.StartsWith("phantom-jobs") => CompassPage.Settings,
                var name when name.StartsWith("auto-chests") && name != "auto-chests-patrol" => CompassPage.Settings,
                var name when name.StartsWith("general-fates") => CompassPage.Fate,
                var name when name.StartsWith("ce-") || name == "menu-ce-open" => CompassPage.Ce,
                var name when name.StartsWith("pot") || name.StartsWith("fate") => CompassPage.Pot,
                var name when name.StartsWith("exploration") => CompassPage.Exploration,
                var name when name.StartsWith("player-visibility") || name == "menu-settings" => CompassPage.Settings,
                _ => CompassPage.Patrol,
            } };
            if (args.Length > 1 && !scenario.Name.StartsWith(args[1], StringComparison.Ordinal)) continue;
            view.ShowPhantomJobs = scenario.Name.StartsWith("phantom-jobs");
            io.FontGlobalScale = scenario.Scale;
            io.DisplaySize = new Vector2(scenario.Width, scenario.Height);
            io.MousePos = new Vector2(-1000);
            float scrollBefore = 0, scrollAfter = 0;
            var beforeActions = actionCount;
            waymarkCalls.Clear();
            ignoreDistanceSetting = null;
            autoChestSetting = null; autoChestCalls.Clear();
            autoPatrolSetting = null; autoPatrolCalls.Clear();
            selectedPatrolRoute = null; patrolRouteCalls.Clear();
            carrotConfirmations.Clear(); carrotResets = 0;
            phantomSwitchCalls.Clear(); phantomCopies.Clear(); phantomIconRefreshes = 0; ceFlags.Clear();
            potOverlayOptions = scenario.State.PotOverlay ?? new(false); potOverlayCalls.Clear();
            patrolOverlayOptions = scenario.State.PatrolOverlay ?? new(false); patrolOverlayCalls.Clear();
            phantomOverlayOptions = scenario.State.PhantomOverlay ?? new(false); phantomOverlayCalls.Clear();
            fetchPotTime = scenario.State.Fates?.FetchSharedTimeOnEntry ?? true; fetchPotTimeCalls.Clear();
            potDebugCopies.Clear(); potDebugRetries = 0;
            string[] clicks = scenario.Name switch { "waymarks-button-check" => ["save", "place", "delete", "delete-confirm"],
                "waymarks-off-island" => ["save", "place"], "waymarks-empty" => ["import"], "waymarks-combat" => ["place"], "waymarks-cancel-check" => ["cancel"],
                "waymarks-import-check" => ["import-header", "json", "import"], _ => [] };
            if (scenario.Name == "waymarks-select-new") clicks = ["save", "place", "preset-" + demoWaymark.Id, "place"];
            if (scenario.Name == "waymarks-distance-toggle") clicks = ["ignore-distance", "place", "ignore-distance"];
            if (scenario.Name == "waymarks-cancel-check") clicks = ["ignore-distance", "cancel"];
            if (scenario.Name == "auto-chests-toggle") clicks = ["auto-chest", "auto-chest"];
            if (scenario.Name == "auto-patrol-toggle") clicks = ["auto-patrol", "auto-patrol"];
            if (scenario.Name == "bocchi-route-switch") clicks = ["route", "Chart", "route", "Bocchi"];
            if (scenario.Name == "bocchi-route-invalid") clicks = ["route", "Bocchi"];
            if (scenario.Name == "carrot-route-switch") clicks = ["route", "Bocchi", "route", "Carrot"];
            if (scenario.Name == "carrot-actions") clicks = ["sort", "flag-1", "reset", "confirm", "confirm-yes"];
            if (scenario.Name == "carrot-stale-confirm") clicks = ["confirm", "confirm-yes"];
            if (scenario.Name == "carrot-off-island") clicks = ["sort", "flag-1", "reset", "confirm"];
            if (scenario.Name is "auto-patrol-off-island" or "auto-patrol-paused") clicks = ["auto-patrol"];
            if (scenario.Name == "phantom-jobs-actions") clicks = ["refresh-icons", "copy-1", "switch-0", "switch-1"];
            if (scenario.Name == "phantom-jobs-off-island") clicks = ["copy-1", "switch-1"];
            if (scenario.Name == "phantom-jobs-last") clicks = ["job-scroll", "copy-12", "switch-12"];
            if (scenario.Name == "ce-actions") clicks = ["boss-33", "trigger-flag", "boss-37", "trigger-flag", "boss-39", "trigger-flag",
                "boss-41", "trigger-flag", "boss-42", "trigger-flag", "boss-44", "trigger-flag", "boss-34", "boss-flag"];
            if (scenario.Name is "ce-off-island" or "ce-north" or "ce-transit") clicks = ["trigger-flag", "boss-flag"];
            if (scenario.Name.StartsWith("pot-overlay-options")) clicks = ["visible", "visible"];
            if (scenario.Name.StartsWith("patrol-overlay-options")) clicks = ["visible", "visible"];
            if (scenario.Name.StartsWith("phantom-overlay-options") || scenario.Name == "phantom-jobs-overlay-options") clicks = ["phantom-overlay", "phantom-overlay"];
            if (scenario.Name is "pot-time-sync-toggle" or "pot-time-sync-off-island") clicks = ["pot-time-sync", "pot-time-sync"];
            if (scenario.Name.StartsWith("pot-debug")) clicks = scenario.Name == "pot-debug-collapse" ? ["header", "copy", "header"] : ["header", "copy"];
            if (scenario.Name.StartsWith("pot-debug-retry") || scenario.Name == "pot-debug-off-island") clicks = ["header", "copy", "retry", "retry"];
            if (scenario.Name.StartsWith("chart-settings")) clicks = ["filters", "status"];
            if (scenario.Name == "loot-minions") clicks = ["category", "category-Minion"];
            if (scenario.Name == "loot-orchestrion") clicks = ["category", "category-Orchestrion"];
            if (scenario.Name == "loot-category-reset") clicks = ["category", "category-Minion", "category", "category-all"];
            if (scenario.Name is "loot-filter-combined" or "loot-filter-empty") clicks = ["category", "category-Minion", "only-bag", "only-garbage", "search"];
            CompassPage[] navigation = [CompassPage.Ce, CompassPage.Exploration, CompassPage.Settings, CompassPage.Fate, CompassPage.Pot, CompassPage.Waymarks, CompassPage.Patrol];
            for (var frame = 0; frame < (clicks.Length > 0 ? 3 + clicks.Length * 4 : scenario.Name.StartsWith("cards-navigation") ? 3 + navigation.Length * 4 : scenario.Name == "menu-navigation" ? 3 + navigation.Length * 7 : scenario.Name == "ce-zoomed" ? 15 : scenario.Name.StartsWith("ce-zoomed") || scenario.Name is "chart-zoomed" or "menu-ce-open" ? 7 : 3); frame++)
            {
                if (scenario.Name.StartsWith("cards-navigation") && frame >= 3)
                {
                    var index = (frame - 3) / 4; var step = (frame - 3) % 4;
                    if (step == 0)
                    {
                        var target = view.PageTargets[navigation[index]];
                        if (target.X <= 20 || target.X >= scenario.Width - 20) throw new InvalidOperationException("Navigation cards must fit the window.");
                        io.AddMousePosEvent(target.X, target.Y);
                    }
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                    if (step == 3 && view.Page != navigation[index]) throw new InvalidOperationException($"Direct page navigation failed: {navigation[index]}");
                }
                if (clicks.Length > 0 && frame >= 3)
                {
                    var index = (frame - 3) / 4; var step = (frame - 3) % 4;
                    if (step == 0) { var target = scenario.Name.StartsWith("bocchi-route") || scenario.Name == "carrot-route-switch" ? view.PatrolRouteTargets[clicks[index]] : scenario.Name.StartsWith("carrot") ? view.CarrotTargets[clicks[index]] : scenario.Name.StartsWith("pot-debug") ? view.PotTimeDebugTargets[clicks[index]] : clicks[index] == "pot-time-sync" ? view.PotTimeSyncTarget : clicks[index] == "phantom-overlay" ? view.PhantomOverlayToggleTarget : clicks[index] == "auto-patrol" ? view.AutoPatrolTarget : scenario.Name.StartsWith("loot") ? view.LootTargets[clicks[index]] : scenario.Name.StartsWith("chart-settings") ? view.PatrolSettingsTargets[clicks[index]] : scenario.Name.StartsWith("patrol-overlay-options") ? view.PatrolOverlayTargets[clicks[index]] : scenario.Name.StartsWith("pot-overlay-options") ? view.PotOverlayTargets[clicks[index]] : scenario.Name.StartsWith("ce-") ? view.CeTargets[clicks[index]] : clicks[index] == "auto-chest" ? view.AutoChestToggleTarget : scenario.Name.StartsWith("phantom-jobs") ? view.PhantomTargets[clicks[index] == "job-scroll" ? "copy-1" : clicks[index]] : view.WaymarkTargets[clicks[index]]; io.AddMousePosEvent(target.X, target.Y); }
                    if (step == 1 && clicks[index] == "job-scroll") io.AddMouseWheelEvent(0, -30);
                    if (step == 1 && clicks[index] != "job-scroll") io.AddMouseButtonEvent(0, true);
                    if (step == 2 && clicks[index] != "job-scroll") io.AddMouseButtonEvent(0, false);
                    if (step == 3 && scenario.Name.StartsWith("pot-debug") && index == clicks.Length - 1) io.AddMousePosEvent(-1000, -1000);
                    if (step == 3 && clicks[index] == "json") foreach (char ch in demoWaymark.Export()) io.AddInputCharacter(ch);
                    if (step == 3 && clicks[index] == "search") foreach (char ch in scenario.Name == "loot-filter-empty" ? "48204" : "21057") io.AddInputCharacter(ch);
                }
                if (scenario.Name == "menu-navigation" && frame >= 3)
                {
                    var index = (frame - 3) / 7;
                    var step = (frame - 3) % 7;
                    if (step == 0) io.AddMousePosEvent(view.MenuTargets.Single(t => t.Page == navigation[index]).Center.X, view.MenuTargets.Single(t => t.Page == navigation[index]).Center.Y);
                    if (step == 1) io.AddMouseButtonEvent(0, true);
                    if (step == 2) io.AddMouseButtonEvent(0, false);
                    if (step == 3)
                    {
                        if (!view.MenuItemTargets.TryGetValue(navigation[index], out var destination)) throw new InvalidOperationException($"Menu did not open: {navigation[index]}");
                        io.AddMousePosEvent(destination.X, destination.Y);
                    }
                    if (step == 4) io.AddMouseButtonEvent(0, true);
                    if (step == 5) io.AddMouseButtonEvent(0, false);
                    if (step == 6 && view.Page != navigation[index]) throw new InvalidOperationException($"Submenu click failed: {navigation[index]}");
                }
                if (scenario.Name == "menu-ce-open")
                {
                    if (frame == 3) { var target = view.MenuTargets.Single(t => t.Page == CompassPage.Ce).Center; io.AddMousePosEvent(target.X, target.Y); }
                    if (frame == 4) io.AddMouseButtonEvent(0, true);
                    if (frame == 5) io.AddMouseButtonEvent(0, false);
                }
                if (scenario.Name == "chart-zoomed" || scenario.Name.StartsWith("ce-zoomed"))
                {
                    var mapArea = scenario.Name.StartsWith("ce-zoomed") ? view.CeMapArea : view.MapArea;
                    if (frame == 3) io.MousePos = scenario.Name == "ce-zoomed-max" ? view.CeTargets["boss-33"] : mapArea.Origin + mapArea.Size * new Vector2(0.65f, 0.35f);
                    if (frame == 4) { io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddMouseWheelEvent(0, scenario.Name == "ce-zoomed-max" ? 20 : scenario.Name == "ce-zoomed-out" ? -20 : 3); }
                    if (frame == 5) io.AddKeyEvent(ImGuiKey.ModCtrl, false);
                    if (frame == 6) io.MousePos = new Vector2(-1000);
                    if (scenario.Name == "ce-zoomed")
                    {
                        // Select the enlarged icon outside its original hit radius, then flag that boss.
                        if (frame == 7) { var target = view.CeTargets["boss-42"] + new Vector2(25, 0); io.AddMousePosEvent(target.X, target.Y); }
                        if (frame is 8 or 11) io.AddMouseButtonEvent(0, true);
                        if (frame is 9 or 12) io.AddMouseButtonEvent(0, false);
                        if (frame == 10) { var target = view.CeTargets["boss-flag"]; io.AddMousePosEvent(target.X, target.Y); }
                        if (frame == 14) io.AddMousePosEvent(-1000, -1000);
                    }
                }
                ImGui.NewFrame();
                using (new CompassTheme())
                {
                    ImGui.SetNextWindowPos(new Vector2(20));
                    ImGui.SetNextWindowSize(new Vector2(scenario.Width - 40, scenario.Height - 40));
                    if (ImGui.Begin($"新月島尋寶羅盤 · 示範資料##{scenario.Name}", ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                    {
                        var frameState = scenario.State with { PotOverlay = potOverlayOptions, PatrolOverlay = patrolOverlayOptions, PhantomOverlay = phantomOverlayOptions };
                        if (scenario.Name == "carrot-stale-confirm" && frame >= 7) frameState = frameState with { Route = frameState.Route.Skip(1).ToArray() };
                        if (frameState.Fates is { } potFates) frameState = frameState with { Fates = potFates with { FetchSharedTimeOnEntry = fetchPotTime } };
                        if (potDebugRetries > 0 && frameState.Fates?.Debug is { } potDebug)
                            frameState = frameState with { Fates = frameState.Fates with { Debug = potDebug with { CanRetry = false, RetryDetail = "查詢尚未完成，請等待結果。" } } };
                        if (autoChestSetting is { } autoSetting) frameState = frameState with { AutoOpenNearbyChests = autoSetting };
                        if (selectedPatrolRoute is { } selectedRoute && frameState.Controls is { } controls)
                            frameState = frameState with { Controls = controls with { RouteKind = selectedRoute } };
                        if (autoPatrolSetting is { } patrolSetting && frameState.AutoPatrol is { } patrolState)
                            frameState = frameState with { AutoPatrol = patrolState with { Enabled = patrolSetting } };
                        if (ignoreDistanceSetting is { } setting && frameState.Waymarks is { } markState)
                            frameState = frameState with { Waymarks = markState with { IgnoreDistance = setting } };
                        if (scenario.Name == "waymarks-select-new" && waymarkCalls.Contains("save"))
                            frameState = frameState with { Waymarks = frameState.Waymarks! with {
                                SelectionRequest = frameState.Waymarks.Presets[1].Id, SelectionRevision = 1 } };
                        view.Draw(frameState, actions);
                        if (scenario.Name == "waymarks-select-new" && waymarkCalls.SequenceEqual(new[] { "save", "place" }) && lastPlacedWaymark != frameState.Waymarks!.Presets[1].Id)
                            throw new InvalidOperationException("Save/import selection request must select the newly saved preset before placing.");
                        if (view.MenuTargets.Count != 8) throw new InvalidOperationException("All eight native menu-bar entries must remain visible.");
                        if (view.Page == CompassPage.Ce && view.CeRowsDrawn != 15) throw new InvalidOperationException("CE list must remain accessible before entering the island or receiving observations.");
                        if (frame == 3) scrollBefore = view.PageScroll;
                        if (frame == 6) scrollAfter = view.PageScroll;
                    }
                    ImGui.End();
                }
                ImGui.Render();
            }
            if ((scenario.Name == "menu-navigation" || scenario.Name.StartsWith("cards-navigation")) && actionCount != beforeActions)
                throw new InvalidOperationException("Changing feature pages must not change filters, routing, or tracking settings.");
            if (scenario.Name.StartsWith("loot") && (actionCount != beforeActions || view.LootRows.Any(i => LootCleanup.IsExcluded(i.Id))))
                throw new InvalidOperationException("Loot filters must not change cleanup rules or actions, and excluded items must remain absent.");
            if (scenario.Name == "loot-minions" && (view.LootRows.Count != 44 || view.LootRows.Any(i => i.SearchCategory != 75)))
                throw new InvalidOperationException("Selecting minions must show only the catalog's 44 minion items.");
            if (scenario.Name == "loot-orchestrion" && (view.LootRows.Count != 7 || view.LootRows.Any(i => !i.EnglishName.EndsWith(" Orchestrion Roll"))))
                throw new InvalidOperationException("Orchestrion selection must include all seven rolls, including unavailable future items.");
            if (scenario.Name == "loot-category-reset" && view.LootRows.Count != 289)
                throw new InvalidOperationException("Selecting All must restore the full cleanup catalog.");
            if (scenario.Name == "loot-filter-combined" && !view.LootRows.Select(i => i.Id).SequenceEqual(new uint[] { 21057 }))
                throw new InvalidOperationException("Category, inventory, garbage and ID search filters must work together.");
            if (scenario.Name == "loot-filter-empty" && view.LootRows.Count != 0)
                throw new InvalidOperationException("An orchestrion ID search within minions must show the empty state.");
            if (scenario.Name == "phantom-jobs-actions" && (phantomIconRefreshes != 1 || !phantomCopies.SequenceEqual(new[] { "/crescent job 1" }) || !phantomSwitchCalls.SequenceEqual(new byte[] { 1 })))
                throw new InvalidOperationException("Copy must produce the selected macro; only a deliberate non-current switch may run.");
            if (scenario.Name == "phantom-jobs-off-island" && (!phantomCopies.SequenceEqual(new[] { "/crescent job 1" }) || phantomSwitchCalls.Count != 0))
                throw new InvalidOperationException("Outside the island macros remain copyable but job switching must be disabled.");
            if (scenario.Name == "phantom-jobs-last" && (!phantomCopies.SequenceEqual(new[] { "/crescent job 12" }) || !phantomSwitchCalls.SequenceEqual(new byte[] { 12 })))
                throw new InvalidOperationException("The last job must remain reachable by scrolling in a compact window.");
            if (scenario.Name == "auto-chests-toggle" && (!autoChestCalls.SequenceEqual(new[] { true, false }) || autoChestSetting != false))
                throw new InvalidOperationException("Auto chest checkbox must enable and disable the persisted setting exactly once each.");
            if (scenario.Name == "auto-patrol-toggle" && !autoPatrolCalls.SequenceEqual(new[] { true, false }))
                throw new InvalidOperationException("Auto patrol must start and stop exactly once through the visible control.");
            if (scenario.Name == "bocchi-route-switch" && (!patrolRouteCalls.SequenceEqual(new[] { PatrolRouteKind.Chart, PatrolRouteKind.Bocchi }) || autoPatrolCalls.Count != 0))
                throw new InvalidOperationException("Route selection must dispatch both directions without starting movement.");
            if (scenario.Name == "bocchi-route-invalid" && patrolRouteCalls.Count != 0)
                throw new InvalidOperationException("Unavailable imported data cannot be selected as a route.");
            if (scenario.Name.StartsWith("carrot") && view.CarrotRowsDrawn != 25)
                throw new InvalidOperationException("Every carrot pad, including visited ones, must stay visible in weight records.");
            if (scenario.Name == "carrot-route-switch" && !patrolRouteCalls.SequenceEqual(new[] { PatrolRouteKind.Bocchi, PatrolRouteKind.Carrot }))
                throw new InvalidOperationException("Carrot route must be selectable independently of chest routes.");
            if (scenario.Name == "carrot-actions" && (carrotResets != 1 || !carrotConfirmations.SequenceEqual(new[] { carrotState.Route[0].Spot.Id })))
                throw new InvalidOperationException("Carrot manual confirmation must carry the chosen pad identity, and reset must dispatch once.");
            if (scenario.Name is "carrot-stale-confirm" or "carrot-off-island" && (carrotResets != 0 || carrotConfirmations.Count != 0))
                throw new InvalidOperationException("Off-island or changed-target confirmation must not change carrot weights.");
            if (scenario.Name == "auto-patrol-off-island" && autoPatrolCalls.Count != 0)
                throw new InvalidOperationException("Auto patrol start must remain disabled outside the supported island.");
            if (scenario.Name == "auto-patrol-paused" && !autoPatrolCalls.SequenceEqual(new[] { false }))
                throw new InvalidOperationException("Stopping auto patrol must remain available while the route is paused.");
            if (scenario.Name == "waymarks-button-check" && !waymarkCalls.SequenceEqual(new[] { "save", "place", "delete" }))
                throw new InvalidOperationException("Waymark save/place and confirmed deletion must each dispatch once.");
            if (scenario.Name == "waymarks-select-new" && (!waymarkCalls.SequenceEqual(new[] { "save", "place", "place" }) || lastPlacedWaymark != demoWaymark.Id || view.SelectedWaymark != demoWaymark.Id))
                throw new InvalidOperationException("Save/import selection must be consumed once, preserving subsequent manual selections.");
            if (scenario.Name == "waymarks-distance-toggle" && (!waymarkCalls.SequenceEqual(new[] { "distance-on", "place", "distance-off" }) || ignoreDistanceSetting != false))
                throw new InvalidOperationException("Ignore distance must toggle both directions and permit the selected preset to be placed.");
            if (scenario.Name is "waymarks-off-island" or "waymarks-empty" or "waymarks-combat" && waymarkCalls.Count != 0)
                throw new InvalidOperationException("Off-island native actions and empty import must stay disabled.");
            if (scenario.Name is "waymarks-placement-error" or "waymarks-unchanged" or "waymarks-combat" &&
                (view.WaymarkTargets["placement-status"].Y <= view.WaymarkTargets["place"].Y || view.WaymarkTargets["placement-status"].Y >= scenario.Height - 40))
                throw new InvalidOperationException("Placement feedback must remain visible directly below the action button.");
            if (scenario.Name == "waymarks-cancel-check" && !waymarkCalls.SequenceEqual(new[] { "cancel" }))
                throw new InvalidOperationException("Active placement cancellation must dispatch once.");
            if (scenario.Name.StartsWith("waymarks-preview-") && (view.PreviewMarkersDrawn != 8 || waymarkCalls.Count != 0 || actionCount != beforeActions))
                throw new InvalidOperationException("Tower preview must draw eight markers without executing game actions.");
            if (scenario.Name == "waymarks-import-check" && (waymarkCalls.Count != 1 || waymarkCalls[0] != "import" || WaymarkPreset.Import(importedWaymarks).Name != demoWaymark.Name))
                throw new InvalidOperationException("Typing JSON and clicking import must preserve the complete preset.");
            if (scenario.Name == "menu-ce-open" && !view.MenuItemTargets.ContainsKey(CompassPage.Ce))
                throw new InvalidOperationException("CE dropdown must be visible in the reference preview.");
            if (scenario.Name == "chart-zoomed" && (view.MapZoom < 1.5f || MathF.Abs(scrollAfter - scrollBefore) > 0.1f))
                throw new InvalidOperationException($"Ctrl-wheel must zoom map without scrolling parent: zoom={view.MapZoom}, scroll={scrollBefore}->{scrollAfter}");
            if (scenario.Name == "ce-zoomed" && (view.CeMapZoom < 1.5f || MathF.Abs(scrollAfter - scrollBefore) > 0.1f))
                throw new InvalidOperationException("CE Ctrl-wheel must zoom the map without scrolling its parent.");
            if (scenario.Name == "ce-zoomed" && !ceFlags.SequenceEqual(new (ushort, bool)[] { (42, false) }))
                throw new InvalidOperationException("Clicking the enlarged icon must select and flag its boss: " + string.Join(",", ceFlags));
            if (scenario.Name == "ce-actions" && !ceFlags.SequenceEqual(new (ushort, bool)[] { (33, true), (37, true), (39, true), (41, true), (42, true), (44, true), (34, false) }))
                throw new InvalidOperationException("CE selection/coordinate clicks must flag each selected trigger or boss exactly once: " + string.Join(",", ceFlags));
            if (scenario.Name is "ce-off-island" or "ce-north" or "ce-transit" && ceFlags.Count != 0)
                throw new InvalidOperationException("CE flag actions must be disabled outside South Horn or during loading.");
            if (scenario.Name.StartsWith("pot-overlay-options") && !potOverlayCalls.SequenceEqual(new[] { "visible-True", "visible-False" }))
                throw new InvalidOperationException("Pot overlay options must remain usable on and off the island: " + string.Join(",", potOverlayCalls));
            if (scenario.Name.StartsWith("patrol-overlay-options") && (!patrolOverlayCalls.SequenceEqual(new[] { "visible-True", "visible-False" }) || potOverlayCalls.Count != 0))
                throw new InvalidOperationException("Patrol overlay options must work independently of the pot overlay: " + string.Join(",", patrolOverlayCalls));
            if (scenario.Name.StartsWith("pot-overlay") && view.PotOverlayTargets.Keys.Any(k => k is "locked" or "reset") ||
                scenario.Name.StartsWith("patrol-overlay") && view.PatrolOverlayTargets.Keys.Any(k => k is "locked" or "reset"))
                throw new InvalidOperationException("Overlay settings must not expose obsolete lock/reset controls.");
            if (scenario.Name.StartsWith("general-fates") && view.FateAutoFlagShown != (scenario.State.GeneralFates?.AutoFlag ?? false))
                throw new InvalidOperationException("FATE auto-flag defaults off and respects an explicitly saved setting.");
            if ((scenario.Name.StartsWith("phantom-overlay-options") || scenario.Name == "phantom-jobs-overlay-options") &&
                (!phantomOverlayCalls.SequenceEqual(new[] { true, false }) || potOverlayCalls.Count != 0 || patrolOverlayCalls.Count != 0))
                throw new InvalidOperationException("Phantom overlay toggles work on both settings pages and outside the island without changing other overlays.");
            if (scenario.Name is "pot-time-sync-toggle" or "pot-time-sync-off-island" &&
                (!fetchPotTimeCalls.SequenceEqual(new[] { false, true }) || potOverlayCalls.Count != 0))
                throw new InvalidOperationException("Entry-time query toggle must work independently on and off the island.");
            if (scenario.Name.StartsWith("pot-debug") && (!potDebugCopies.SequenceEqual(new[] { scenario.State.Fates!.Debug!.Report }) ||
                view.PotTimeDebugVisible != (scenario.Name != "pot-debug-collapse") || fetchPotTimeCalls.Count != 0 || actionCount != beforeActions))
                throw new InvalidOperationException("Debug expansion and full report copy must not query, toggle settings or affect game actions.");
            if (scenario.Name.StartsWith("pot-debug") && potDebugRetries !=
                (scenario.Name.StartsWith("pot-debug-retry") && scenario.State.Fates!.Debug!.CanRetry ? 1 : 0))
                throw new InvalidOperationException("Manual retry must dispatch once; disabled or in-flight retry clicks must do nothing.");
            var path = Path.Combine(output, $"{scenario.Name}.png");
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, scenario.Width, scenario.Height, path);
            Console.WriteLine($"Rendered {scenario.Name}: {scenario.Width}x{scenario.Height}, {ImGui.GetDrawData().TotalVtxCount} vertices -> {path}");
        }
    }
    finally { ImGui.DestroyContext(context); }
}
