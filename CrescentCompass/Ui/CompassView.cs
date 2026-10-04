using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private float scale;
    private int selectedTab;
    private int manualDirection;
    private readonly MapViewport mapViewport = new();
    private (int Revision, string Region, bool Chart)? mapContext;
    internal (Vector2 Origin, Vector2 Size) MapArea { get; private set; }
    internal float MapZoom => mapViewport.Zoom;
    internal float PageScroll { get; private set; }
    private static int Completed(CompassViewState state) => state.CompletedStops ?? Math.Max(0, state.TotalStops - state.Route.Count - state.SkippedStops);
    private float U(float value) => value * scale;
    internal static string KindName(SpotKind kind) => kind switch
    {
        SpotKind.Carrot => "蘿蔔", SpotKind.Silver => "銀箱", SpotKind.Bronze => "銅箱",
        SpotKind.PotGold => "罐子金箱", SpotKind.PotSilver => "罐子銀箱", SpotKind.PotBronze => "罐子銅箱",
        SpotKind.RabbitGold => "兔子金箱", SpotKind.Gold => "金箱", SpotKind.Tower => "塔內／獎勵", SpotKind.Exploration => "探索筆記", _ => "其他寶箱",
    };
    internal static Vector4 KindColor(SpotKind kind) => kind switch
    {
        SpotKind.Carrot => Carrot, SpotKind.Silver or SpotKind.PotSilver => Silver,
        SpotKind.Bronze or SpotKind.PotBronze => Bronze,
        SpotKind.Exploration => new(0.71f, 0.65f, 1f, 1),
        SpotKind.PotGold or SpotKind.RabbitGold or SpotKind.Gold => new(1f, 0.79f, 0.35f, 1), _ => Mint,
    };
    private static string StatusName(SpotStatus status) => status switch
    {
        SpotStatus.Visible => "目前可選取", SpotStatus.LastSeen => "曾看見 · 待確認",
        SpotStatus.Visited => "已巡查", SpotStatus.Location => "探索狀態未知", SpotStatus.Skipped => "已略過 · 無可用箱",
        SpotStatus.Unexplored => "未探索", SpotStatus.Explored => "遊戲內已完成", _ => "候選點 · 待確認",
    };

    internal void Draw(CompassViewState state, CompassActions actions)
    {
        scale = ImGui.GetFontSize() / 17f;
        DrawNavigation(state, actions);
        DrawHeader(state);
        DrawPageNavigation();
        var accent = PageAccent;
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(Surface, accent, 0.24f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(Surface, accent, 0.40f));
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Lerp(Surface, accent, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Lerp(Surface, accent, 0.32f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, accent);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Vector4.Lerp(Surface, accent, 0.16f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(14)));
        // Independent scroll positions keep the menu visible; changing pages never changes tracking or routes.
        if (ImGui.BeginChild($"feature-page-{Page}", Vector2.Zero, true))
        {
            var destination = Destinations.Single(d => d.Page == Page);
            DrawSection(destination.Title, destination.Detail, accent);
            switch (Page)
            {
                case CompassPage.Loot: DrawLoot(state, actions); break;
                case CompassPage.Patrol:
                    DrawStats(state);
                    if (state.Active) DrawJourney(state, actions);
                    if (state.Active) DrawSurvey(state);
                    if (state.Controls is not null) DrawSection("路線控制", "規劃與巡查進度", Sky);
                    DrawRouteControls(state, actions);
                    DrawSection("目標與自動化", "篩選與執行選項", Violet);
                    DrawFilters(state, actions);
                    if (state.Pot?.Active == true) ImGui.TextColored(Carrot, "魔法罐尋寶中 · 詳情與旗標請見「魔法罐」選單");
                    if (state.Active) DrawLists(state, actions);
                    else DrawWaiting(state);
                    DrawFooter(state, actions);
                    break;
                case CompassPage.Pot:
                    if (state.Active)
                    {
                        DrawFates(state, actions); DrawPot(state, actions);
                        if (state.Message.Contains("FATE", StringComparison.Ordinal) || state.Message.StartsWith("魔法罐插旗", StringComparison.Ordinal) ||
                            state.Message.StartsWith("魔法罐旗標", StringComparison.Ordinal)) ImGui.TextWrapped(state.Message);
                    }
                    else DrawWaiting(state);
                    break;
                case CompassPage.Ce: DrawCeCooldowns(state, actions); break;
                case CompassPage.Fate: DrawGeneralFates(state, actions); break;
                case CompassPage.Waymarks: DrawWaymarks(state, actions); break;
                case CompassPage.Exploration: DrawExplorationPage(state, actions); break;
                case CompassPage.Settings: DrawSettings(state, actions); break;
            }
            PageScroll = ImGui.GetScrollY();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(6);
    }

    private void DrawHeader(CompassViewState state)
    {
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, U(91)), Pack(Raised), U(12));
        draw.AddCircleFilled(origin + new Vector2(width - U(50), U(45)), U(36), Pack(Alpha(Violet, 0.12f)));
        draw.AddCircleFilled(origin + new Vector2(width - U(87), U(60)), U(22), Pack(Alpha(Sky, 0.10f)));
        DrawCompass(draw, origin + new Vector2(U(34), U(39)), U(22), Gold);
        Label(draw, origin + new Vector2(U(72), U(11)), $"CRESCENT COMPASS / {state.PluginVersion}", Sky, 15);
        Label(draw, origin + new Vector2(U(71), U(31)), "新月島尋寶羅盤", Text, 28);
        Label(draw, origin + new Vector2(U(72), U(68)), "巡查路線 / 島嶼事件 / 探索收藏", Muted, 15);
        var status = state.Active ? $"{state.Region}  ·  偵測中" : state.Transit
            ? state.Route.Count > 0 || state.Planning ? "傳送中 · 路線已保留" : "傳送中 · 無進行中路線"
            : "等待進入新月島";
        var badgeWidth = ImGui.CalcTextSize(status).X * 16 / 17 + U(27);
        Badge(draw, origin + new Vector2(width - badgeWidth - U(15), U(37)), status, state.Active ? Mint : Gold, true, 16);
        ImGui.Dummy(new Vector2(width, U(91)));
    }

    private void DrawStats(CompassViewState state)
    {
        if (state.Controls?.ChartMode == true)
        {
            ImGui.TextColored(Mint, state.TotalStops > 0
                ? $"巡查 {Completed(state) + state.SkippedStops:00} / {state.TotalStops:00} · 待巡 {state.Route.Count:00} · 略過 {state.SkippedStops}"
                : "圖表巡查 · 68 處固定候選位置");
            return;
        }
        if (!ImGui.BeginTable("overview", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings)) return;
        var carrotCount = state.Points.Count(p => p.Spot.Kind == SpotKind.Carrot && p.Status == SpotStatus.Visible);
        var completed = Completed(state) + state.SkippedStops;
        ImGui.TableNextColumn(); StatCard("附近蘿蔔", state.Active ? carrotCount.ToString("00") : "—", "已載入範圍", Carrot);
        ImGui.TableNextColumn(); StatCard("全島探查 · 銀／銅箱", state.Active && state.TreasureSurvey is { } survey ? $"{survey.Silver:00} / {survey.Bronze:00}" : "— / —", "最近探查快照", Silver);
        ImGui.TableNextColumn(); StatCard("巡查進度", state.TotalStops > 0 ? $"{completed:00} / {state.TotalStops:00}" : "尚未開始", $"略過 {state.SkippedStops} 處", Mint, state.TotalStops > 0 ? completed / (float)state.TotalStops : 0);
        ImGui.EndTable();
    }

    private void DrawSurvey(CompassViewState state)
    {
        if (state.Controls?.ChartMode == true && !ImGui.CollapsingHeader(state.TreasureSurvey is { } count
            ? $"個人全島探查 · 銀 {count.Silver}／銅 {count.Bronze}###chart-survey" : "個人全島探查 · 尚無數量快照###chart-survey")) return;
        ImGui.TextColored(Mint, state.TreasureSurvey is { } survey ? $"個人全島探查 · {survey.CapturedAt.ToLocalTime():HH:mm:ss} 的數量快照" : "個人全島探查 · 尚未收到數量");
        ImGui.TextWrapped(state.TreasureSurvey is null ? "請在遊戲內使用寶箱探查能力，收到提示後會更新銀箱、銅箱數量。未知數量不當作 0。" : "數量為探查當時的結果，開箱後請再次探查；提示不含寶箱座標。");
        ImGui.TextWrapped("全島已知位置會保留離開視野的目標；遠處是否仍存在、是否可由你開啟，尚無法確認。");
    }

    private void StatCard(string title, string value, string caption, Vector4 color, float progress = -1)
    {
        var origin = ImGui.GetCursorScreenPos(); var size = new Vector2(ImGui.GetContentRegionAvail().X, U(81));
        ImGui.InvisibleButton(title, size); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, Pack(Vector4.Lerp(Surface, color, 0.13f)), U(9));
        draw.AddRect(origin, origin + size, Pack(Alpha(color, 0.5f)), U(9));
        draw.AddRectFilled(origin + new Vector2(U(13), U(15)), origin + new Vector2(U(16), U(29)), Pack(color), U(2));
        Label(draw, origin + new Vector2(U(24), U(10)), title, Muted, 16);
        Label(draw, origin + new Vector2(U(15), U(33)), value, color, value == "尚未開始" ? 21 : 28);
        if (size.X > U(225) && progress < 0) Label(draw, origin + new Vector2(size.X - ImGui.CalcTextSize(caption).X * 15 / 17 - U(15), U(52)), caption, Muted, 15);
        if (progress >= 0)
        {
            if (size.X > U(230)) Label(draw, origin + new Vector2(size.X - ImGui.CalcTextSize(caption).X * 15 / 17 - U(15), U(48)), caption, Muted, 15);
            var bar = origin + new Vector2(U(15), size.Y - U(10));
            draw.AddRectFilled(bar, bar + new Vector2(size.X - U(30), U(3)), Pack(Border), U(2));
            if (progress > 0) draw.AddRectFilled(bar, bar + new Vector2((size.X - U(30)) * progress, U(3)), Pack(Mint), U(2));
        }
    }

    private void DrawFilters(CompassViewState state, CompassActions actions)
    {
        var filters = state.Filters;
        if (state.Controls?.ChartMode != true || ImGui.CollapsingHeader("其他地點與顯示篩選"))
        {
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(Muted, "巡查目標"); ImGui.SameLine();
        if (Chip("蘿蔔", filters.Carrots, Carrot)) filters = filters with { Carrots = !filters.Carrots };
        ImGui.SameLine(); if (Chip("銀箱", filters.Silver, Silver)) filters = filters with { Silver = !filters.Silver };
        ImGui.SameLine(); if (Chip("銅箱", filters.Bronze, Bronze)) filters = filters with { Bronze = !filters.Bronze };
        if (Chip("罐子／兔子／其他寶箱", filters.Special, Carrot)) filters = filters with { Special = !filters.Special };
        HoverHint("顯示已載入的事件寶箱與其他模型寶箱；不代表所有隱藏寶箱已被揭露。");
        ImGui.SameLine(); if (Chip("塔內／獎勵", filters.Tower, Mint)) filters = filters with { Tower = !filters.Tower };
        HoverHint("包含南區 14 個 BA_treasure 場景候選點。塔內有樓層與機關限制，需自行確認可達性。");
        if (filters.Exploration) ImGui.TextColored(KindColor(SpotKind.Exploration), "已納入島上探索筆記 · 可至「探索筆記」選單調整");
        var mode = (int)filters.DisplayMode;
        ImGui.SetNextItemWidth(U(200));
        if (ImGui.Combo("物件顯示範圍", ref mode, "全島已知位置\0目前可選取\0全島候選巡查\0")) filters = filters with { DisplayMode = (PointDisplayMode)mode };
        HoverHint("全島已知位置：本場曾偵測為可選取且未記為已巡查的物件，離開視野仍保留。\n目前可選取：限已載入目標。\n全島候選巡查：另加入固定候選位置，並非你目前可開的寶箱。\n探索筆記地點使用獨立開關，不受此範圍限制。");
        if (filters.Exploration && state.Active && state.Region.Contains("北")) ImGui.TextWrapped("北部探索地點尚未由本機繁中資料核對，本版不提供推測位置。");
        if (filters != state.Filters) actions.SetFilters(filters);
        }
        DrawAutoChestControl(state, actions);
        var autoAdvance = state.AutoAdvanceChests;
        if (ImGui.Checkbox("開箱或近距離空點時，自動標記下一站", ref autoAdvance)) actions.SetAutoAdvance?.Invoke(autoAdvance);
        HoverHint("確認開箱後插下一旗；水平與步行路程都在判定範圍內、高差不超過 8 公尺，連續 3 秒沒有可用箱則略過。導航未就緒或仍需繞路時等待。\n探索筆記啟用未探索篩選時，遊戲確認完成目前站也會換旗；蘿蔔需手動巡查。互動、讀條、過場與魔法罐尋寶時暫停換旗。");
        if (state.Controls?.ChartMode != true || ImGui.CollapsingHeader("空點距離與地形狀態"))
        {
        ImGui.BeginDisabled(!autoAdvance);
        var radius = state.EmptyCheckRadius;
        ImGui.SetNextItemWidth(U(210));
        if (ImGui.SliderFloat("空點判定距離", ref radius, 20, 100, "%.0f m")) actions.SetEmptyCheckRadius?.Invoke(radius);
        ImGui.EndDisabled();
        ImGui.TextWrapped($"自動巡查：{state.AutomationDetail}");
        ImGui.TextWrapped($"步行路線：{state.NavigationDetail}");
        }
        if (state.Controls?.ChartMode == true && state.Planning && state.Controls.Paused == false) ImGui.TextWrapped(state.NavigationDetail);
        if (state.Planning && state.Controls?.Paused != true && ImGui.Button("取消計算，保留原路線")) actions.CancelPlanning?.Invoke();
        ImGui.BeginDisabled(!state.Active || state.Planning);
        ImGui.BeginDisabled(state.Controls?.ChartMode == true && !state.Region.Contains("南"));
        if (PrimaryButton(state.Controls?.ChartMode == true ? $"從 #{state.Controls.StartNumber:00} 開始" : state.TotalStops > 0 ? "重新規劃路線" : "規劃巡查路線", new Vector2(U(158), U(34)))) actions.Plan();
        ImGui.SameLine(); if (ImGui.Button("重新巡查", new Vector2(U(106), U(34)))) actions.Restart();
        HoverHint("保留本場已巡查與略過記錄。圖表模式從上輪未完成的首點接續，維持編號順序；最短模式優先未巡查點。若要全部重跑，使用「設定」選單中的清除紀錄。");
        ImGui.EndDisabled(); ImGui.EndDisabled(); ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextColored(Muted, state.Controls?.ChartMode == true ? "保留已巡查紀錄" : "從目前位置出發");
    }

    private void DrawLists(CompassViewState state, CompassActions actions)
    {
        var points = state.Points.Where(p => p.Spot.Kind switch { SpotKind.Carrot => state.Filters.Carrots, SpotKind.Silver => state.Filters.Silver,
            SpotKind.Bronze => state.Filters.Bronze, SpotKind.Tower => state.Filters.Tower, SpotKind.Exploration => state.Filters.Exploration, _ => state.Filters.Special }).ToArray();
        if (Chip($"巡查路線  {state.Route.Count:00}", selectedTab == 0, Mint)) selectedTab = 0;
        ImGui.SameLine(); if (Chip($"地點清單  {points.Length:00}", selectedTab == 1, Mint)) selectedTab = 1;
        var rows = selectedTab == 0 ? state.Route : points.OrderBy(p => p.Status == SpotStatus.Visible ? 0 : 1).ThenBy(p => p.Distance).ToArray();
        if (rows.Count == 0) { ImGui.TextWrapped(selectedTab == 0 ? "規劃路線後，這裡會列出每一站。" : "尚無符合篩選的已知地點。探索筆記可獨立啟用；未發現的寶箱座標仍未知。"); return; }
        var height = Math.Clamp(ImGui.GetContentRegionAvail().Y - U(80), U(150), U(300));
        if (!ImGui.BeginTable("destinations", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings, new Vector2(0, height))) return;
        ImGui.TableSetupColumn("目標與地圖座標", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("狀態", ImGuiTableColumnFlags.WidthFixed, U(138));
        ImGui.TableSetupColumn(selectedTab == 0 ? "路段距離" : "直線距離", ImGuiTableColumnFlags.WidthFixed, U(85));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, U(55));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; ImGui.PushID(row.Spot.Id); ImGui.TableNextRow(ImGuiTableRowFlags.None, U(43));
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            ImGui.TextColored(KindColor(row.Spot.Kind), $"{(row.ChartNumber is { } number ? $"#{number:00}  " : selectedTab == 0 ? $"{i + 1:00}  " : "")}{row.Spot.Name ?? KindName(row.Spot.Kind)}");
            HoverHint(row.Spot.Name ?? KindName(row.Spot.Kind));
            ImGui.TextColored(Muted, row.Coordinates);
            ImGui.TableNextColumn(); DrawStatus(row.Status, row.Spot.RequiresTower);
            if (row.LastSeen is { } lastSeen) HoverHint($"最後看見：{lastSeen.ToLocalTime():HH:mm:ss}");
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Muted, selectedTab == 0 ? row.WalkingDistance is { } walking ? $"{walking:F0} m" : "--" : $"{row.Distance:F0} m");
            if (selectedTab == 0) HoverHint(row.LivePath ? "最近一次從角色位置尋路的步行距離，會隨行走重新計算。" : "規劃當時從上一站前往這站的步行路程。");
            ImGui.TableNextColumn(); if (ImGui.Button("插旗")) actions.Flag(row.Spot); ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawWaiting(CompassViewState state)
    {
        var origin = ImGui.GetCursorScreenPos(); var size = new Vector2(ImGui.GetContentRegionAvail().X, U(215));
        ImGui.InvisibleButton("waiting", size); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, Pack(Surface), U(10));
        var center = origin + new Vector2(size.X / 2, U(66));
        draw.AddCircle(center, U(42), Pack(Alpha(Mint, 0.1f)), 64, U(1)); DrawCompass(draw, center, U(27), Mint);
        var hasRoute = state.Route.Count > 0 || state.Planning;
        CenterLabel(draw, origin + new Vector2(size.X / 2, U(124)), state.Transit ? hasRoute ? "傳送中，巡查路線已保留" : "傳送讀取中" : "探索從這裡開始", Text, 23);
        CenterLabel(draw, origin + new Vector2(size.X / 2, U(163)), state.Transit
            ? state.Controls?.Paused == true ? "暫停狀態保留，落地後可手動繼續。" : hasRoute ? "返回同島同分流後，從落點接續規劃。" : "目前沒有進行中的巡查，落地後可重新開始。"
            : "進入新月島後，會自動偵測附近蘿蔔與寶箱。", Muted, 15);
    }

    private void DrawFooter(CompassViewState state, CompassActions actions)
    {
        if (state.Active)
        {
            ImGui.TextWrapped(state.Message);
            if (state.RouteChanged && state.Route.Count > 0) ImGui.TextColored(Mint, "點位狀態已更新，可重新規劃路線。");
            if (state.Unreachable is { Count: > 0 } blocked && ImGui.CollapsingHeader($"未找到接續路徑 · {blocked.Count} 點"))
                foreach (var point in blocked)
                {
                    ImGui.PushID("blocked-" + point.Id);
                    if (ImGui.SmallButton("插旗查看")) actions.Flag(point);
                    ImGui.SameLine(); ImGui.TextUnformatted(point.Name ?? $"{KindName(point.Kind)} · {point.Id}");
                    ImGui.PopID();
                }
        }
    }

}
