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
        // Independent scroll positions keep the menu visible; changing pages never changes tracking or routes.
        if (ImGui.BeginChild($"feature-page-{Page}", Vector2.Zero, false))
        {
            switch (Page)
            {
                case CompassPage.Patrol:
                    DrawStats(state);
                    if (state.Active) DrawSurvey(state);
                    DrawRouteControls(state, actions);
                    DrawFilters(state, actions);
                    if (state.Pot?.Active == true) ImGui.TextColored(Carrot, "魔法罐尋寶中 · 詳情與旗標請見「魔法罐」選單");
                    if (state.Active) { DrawJourney(state, actions); DrawLists(state, actions); }
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
                case CompassPage.Exploration: DrawExplorationPage(state, actions); break;
                case CompassPage.Settings: DrawSettings(state, actions); break;
            }
            PageScroll = ImGui.GetScrollY();
        }
        ImGui.EndChild();
    }

    private void DrawHeader(CompassViewState state)
    {
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        DrawCompass(draw, origin + new Vector2(U(22), U(24)), U(19), Mint);
        Label(draw, origin + new Vector2(U(56), 0), $"C R E S C E N T   C O M P A S S   ·   {state.PluginVersion}", Muted, 11);
        Label(draw, origin + new Vector2(U(55), U(19)), "新月島尋寶羅盤", Text, 25);
        var status = state.Active ? $"{state.Region}  ·  偵測中" : state.Transit
            ? state.Route.Count > 0 || state.Planning ? "傳送中 · 路線已保留" : "傳送中 · 無進行中路線"
            : "等待進入新月島";
        var badgeWidth = ImGui.CalcTextSize(status).X * 13 / 17 + U(27);
        Badge(draw, origin + new Vector2(width - badgeWidth, U(16)), status, state.Active ? Mint : Muted, true);
        ImGui.Dummy(new Vector2(width, U(53)));
    }

    private void DrawFates(CompassViewState state, CompassActions actions)
    {
        if (state.Fates is not { } fates) return;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Alpha(Mint, 0.06f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(12)));
        var locations = fates.Locations ?? [];
        if (ImGui.BeginChild("pot-fates", new Vector2(0, U(154 + fates.Active.Count * 108 + (locations.Count > 0 ? 68 + locations.Count * 36 : 0))), true))
        {
            ImGui.TextColored(Mint, "魔法罐 FATE"); ImGui.SameLine();
            var notify = fates.Notify;
            if (ImGui.Checkbox("出現時通知", ref notify)) actions.SetPotFateNotify?.Invoke(notify);
            HoverHint("偵測到本場魔法罐 FATE 時，顯示一次 Dalamud 彈出通知與僅自己可見的聊天提示。\n關閉通知仍會追蹤倒數；插件視窗關閉時也會偵測。\n倒數是約 30 分鐘南北交替的推估，不是伺服器保證。換區、分流或重載後重新建立本場紀錄。");
            if (locations.Count > 0)
            {
                ImGui.TextColored(Muted, "北罐／南罐座標 · 固定地點");
                HoverHint("事件尚未出現時也能查看與插旗；固定地點不代表目前正在進行。\n南部座標已與繁中場景資料核對；北部位置取自公開資料，尚未經本機核對。");
                if (ImGui.BeginTable("pot-fate-locations", 3, ImGuiTableFlags.NoSavedSettings))
                {
                    ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("coordinates", ImGuiTableColumnFlags.WidthFixed, U(152));
                    ImGui.TableSetupColumn("flag", ImGuiTableColumnFlags.WidthFixed, U(64));
                    foreach (var location in locations)
                    {
                        ImGui.PushID($"fixed-{location.Id}");
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextWrapped(location.Name);
                        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextColored(Mint, location.Coordinates);
                        ImGui.TableNextColumn(); ImGui.BeginDisabled(!location.CanFlag);
                        if (ImGui.Button("插旗", new Vector2(U(60), U(28)))) actions.FlagPotFateLocation?.Invoke(location.Id);
                        ImGui.EndDisabled(); ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
                ImGui.Spacing();
            }
            foreach (var fate in fates.Active)
            {
                ImGui.PushID(fate.Id);
                ImGui.TextColored(Carrot, $"已出現 · {fate.Name}");
                ImGui.TextWrapped($"{fate.Coordinates} · {fate.Status}");
                ImGui.BeginDisabled(!fate.CanFlag);
                if (ImGui.Button("標記 FATE 地點", new Vector2(U(145), U(28)))) actions.FlagPotFate?.Invoke(fate.Id);
                ImGui.EndDisabled(); ImGui.PopID();
                ImGui.Spacing();
            }
            ImGui.TextColored(Mint, $"預估下次 · {fates.Countdown}");
            ImGui.TextWrapped(fates.NextName);
            ImGui.TextWrapped(fates.Detail);
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor();
        ImGui.Spacing();
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
        draw.AddRectFilled(origin, origin + size, Pack(Surface), U(9));
        draw.AddRect(origin, origin + size, Pack(Alpha(Border, 0.6f)), U(9));
        draw.AddRectFilled(origin + new Vector2(U(13), U(15)), origin + new Vector2(U(16), U(29)), Pack(color), U(2));
        Label(draw, origin + new Vector2(U(24), U(13)), title, Muted, 13);
        Label(draw, origin + new Vector2(U(15), U(33)), value, color, value == "尚未開始" ? 21 : 28);
        if (size.X > U(225) && progress < 0) Label(draw, origin + new Vector2(size.X - ImGui.CalcTextSize(caption).X * 12 / 17 - U(15), U(52)), caption, Muted, 12);
        if (progress >= 0)
        {
            Label(draw, origin + new Vector2(size.X - ImGui.CalcTextSize(caption).X * 11 / 17 - U(15), U(15)), caption, Muted, 11);
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

    private void DrawPot(CompassViewState state, CompassActions actions)
    {
        if (state.Pot is not { } pot) return;
        ImGui.Spacing();
        if (!pot.Active)
        {
            var enabled = pot.AutoFlag;
            if (ImGui.Checkbox("魔法罐自動追蹤位置與旗標", ref enabled)) actions.SetPotAutoFlag?.Invoke(enabled);
            HoverHint("取得「指引財寶」後自動標示候選搜尋點；收到聖靈藥方向、第二處財寶或現身提示時自動更新。\n獨立於一般寶箱顯示範圍。尋寶期間暫停一般路線自動換旗；仍需自行使用遊戲內聖靈藥。");
            return;
        }
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Alpha(Carrot, 0.065f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(12)));
        if (ImGui.BeginChild("pot-search", new Vector2(0, U(178)), true))
        {
            ImGui.TextColored(Carrot, "魔法罐尋寶"); ImGui.SameLine();
            ImGui.TextColored(pot.Revealed ? Mint : Muted, pot.Revealed ? "附近寶箱已現身" : $"{pot.Candidates} 個候選 · 尚未確認");
            ImGui.TextWrapped(pot.Detail);
            ImGui.TextColored(pot.Revealed ? Mint : Carrot, pot.Coordinates is { } coords ? $"{(pot.Revealed ? "寶箱座標" : "搜尋候選點")}  {coords}" : "等待有效提示或可互動寶箱");
            var enabled = pot.AutoFlag;
            if (ImGui.Checkbox("自動追蹤位置與旗標", ref enabled)) actions.SetPotAutoFlag?.Invoke(enabled);
            ImGui.TextWrapped(pot.AutomationDetail);
        }
        ImGui.EndChild(); ImGui.PopStyleVar(); ImGui.PopStyleColor();
        if (!ImGui.CollapsingHeader("魔法罐手動備援")) return;
        ImGui.BeginDisabled(pot.Coordinates is null);
        if (ImGui.Button("重新插旗", new Vector2(U(120), U(30)))) actions.FlagPot?.Invoke();
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("重設候選", new Vector2(U(102), U(30)))) actions.RestartPot?.Invoke();
        ImGui.TextWrapped("自動提示未出現時使用。站在剛才使用聖靈藥的位置，再選擇遊戲提示的方向。");
        ImGui.SetNextItemWidth(U(120));
        ImGui.Combo("##pot-direction", ref manualDirection, "北\0東北\0東\0東南\0南\0西南\0西\0西北\0");
        ImGui.SameLine(); if (ImGui.Button("套用方向")) actions.PotHint?.Invoke(manualDirection + 1);
    }

    private void DrawJourney(CompassViewState state, CompassActions actions)
    {
        if (ImGui.GetContentRegionAvail().X >= U(760))
        {
            if (!ImGui.BeginTable("journey", 2, ImGuiTableFlags.NoSavedSettings)) return;
            ImGui.TableSetupColumn("map", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("next", ImGuiTableColumnFlags.WidthFixed, U(237));
            ImGui.TableNextColumn(); DrawMapPanel(state, actions);
            ImGui.TableNextColumn(); DrawNextPanel(state, actions); ImGui.EndTable();
        }
        else { DrawNextPanel(state, actions, true); DrawMapPanel(state, actions); }
    }

    private void DrawMapPanel(CompassViewState state, CompassActions actions)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(12)));
        if (ImGui.BeginChild("map-panel", new Vector2(0, U(340)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextUnformatted("巡查路線"); ImGui.SameLine(); ImGui.TextColored(Muted, state.Route.Count > 0 ? $"/ {state.Route.Count} 站待巡查" : "/ 等待規劃");
            DrawRouteMap(state, actions, new Vector2(ImGui.GetContentRegionAvail().X, U(242)));
            if (ImGui.SmallButton("全圖")) mapContext = null;
            ImGui.SameLine(); ImGui.TextColored(Muted, $"{mapViewport.Zoom:P0} · Ctrl＋滾輪縮放 · 右鍵拖曳");
            ImGui.TextColored(Muted, state.Controls?.ChartMode == true ? "點選插旗 · Ctrl＋點選設定起點" : "點選節點插旗 · 雙擊空白處顯示全圖");
            HoverHint("Ctrl＋滾輪以滑鼠所在位置縮放，右鍵／中鍵拖曳平移；全圖可還原。線條只畫已取得的地形路徑。圖表模式固定 1～68 順序；最短模式另計站點順序。機關與解鎖需自行確認。");
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawNextPanel(CompassViewState state, CompassActions actions, bool compact = false)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(16), U(14)));
        if (ImGui.BeginChild("next-panel", new Vector2(0, U(compact ? 264 : 340)), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Mint, state.Controls?.Paused == true ? "已暫停 · 保留目前站" : "下一站");
            if (state.Route.FirstOrDefault() is { } next)
            {
                var color = KindColor(next.Spot.Kind); var origin = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
                draw.AddCircleFilled(origin + new Vector2(U(19), U(20)), U(19), Pack(Alpha(color, 0.13f)));
                DrawPin(draw, origin + new Vector2(U(19), U(20)), U(7), color);
                Label(draw, origin + new Vector2(U(48), U(1)), $"{(next.ChartNumber is { } number ? $"#{number:00} " : "")}{KindName(next.Spot.Kind)}", Text, 24);
                Label(draw, origin + new Vector2(U(48), U(29)), next.Coordinates, Muted, 13);
                ImGui.Dummy(new Vector2(0, U(48)));
                if (next.Spot.Name is { } name)
                {
                    Label(draw, ImGui.GetCursorScreenPos(), name, Text, 13);
                    ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, U(18))); HoverHint(name);
                }
                DrawStatus(next.Status, next.Spot.RequiresTower);
                ImGui.TextColored(Text, next.WalkingDistance is { } distance ? $"{(next.LivePath ? "前往約" : "規劃此段")} {distance:F0} m" : "路段距離待計算");
                if (!compact) { ImGui.Spacing(); ImGui.TextColored(Muted, "旗標會開啟遊戲地圖"); }
                var width = ImGui.GetContentRegionAvail().X;
                if (compact) width = (width - U(10)) / 2;
                if (PrimaryButton("在地圖插旗", new Vector2(width, U(36)))) actions.Flag(next.Spot);
                if (compact) ImGui.SameLine();
                ImGui.BeginDisabled(state.Controls?.Paused == true);
                if (ImGui.Button("已巡查 → 下一站", new Vector2(width, U(36)))) actions.Next();
                if (next.ChartNumber is not null && actions.ConfirmOpened is not null)
                {
                    if (ImGui.SmallButton("手動確認此箱已開")) actions.ConfirmOpened();
                    HoverHint("自動紀錄未捕捉到時使用；這會記錄目前編號為已開箱並切到下一站。空點或只經過請用「已巡查」。");
                }
                ImGui.EndDisabled();
            }
            else
            {
                var finished = state.TotalStops > 0 && Completed(state) + state.SkippedStops >= state.TotalStops;
                ImGui.Spacing(); ImGui.TextUnformatted(finished ? "本輪巡查結束" : state.TotalStops > 0 ? "目前沒有可用站點" : "準備開始探索");
                ImGui.TextColored(Muted, finished ? $"已巡查 {Completed(state)} · 略過 {state.SkippedStops}" : state.TotalStops > 0 ? "靠近目標後可重新規劃。" : "選擇目標，再規劃路線。");
                ImGui.BeginDisabled(state.Planning || state.Controls?.ChartMode == true && !state.Region.Contains("南"));
                ImGui.Spacing(); if (PrimaryButton("規劃巡查路線", new Vector2(-1, U(36)))) actions.Plan();
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private void DrawRouteMap(CompassViewState state, CompassActions actions, Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        MapArea = (origin, size);
        ImGui.InvisibleButton("route-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        // Claim the wheel while the map is hovered so Ctrl+wheel cannot scroll the parent window.
        if (hovered) ImGuiP.SetItemUsingMouseWheel();
        var clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var chart = state.Controls is { ChartMode: true };
        var points = chart ? state.Controls!.ChartPoints : state.Route;
        var ground = state.GroundLegs ?? [];
        var playerWorld = new Vector2(state.Position.X, state.Position.Z);
        var bounds = points.Select(p => new Vector2(p.Spot.Position.X, p.Spot.Position.Z))
            .Concat(ground.SelectMany(l => l.Path).Select(p => new Vector2(p.X, p.Z))).Append(playerWorld);
        var context = (state.Controls?.MapRevision ?? state.TotalStops, state.Region, chart);
        if (mapContext != context) { mapViewport.Fit(bounds); mapContext = context; }
        if (hovered && io.KeyCtrl && io.MouseWheel != 0) mapViewport.ZoomAt(io.MouseWheel, ImGui.GetMousePos() - origin, size);
        if (ImGui.IsItemActive() && (ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 0)))
            mapViewport.Pan(io.MouseDelta, size);
        Vector2 Project(Vector3 p) => origin + mapViewport.Project(new(p.X, p.Z), size);
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
        draw.AddRectFilled(origin, origin + size, Pack(Background), U(8));
        var grid = U(32) * MathF.Sqrt(mapViewport.Zoom);
        var gridOrigin = mapViewport.Project(Vector2.Zero, size);
        for (var x = (gridOrigin.X % grid + grid) % grid; x < size.X; x += grid)
            draw.AddLine(origin + new Vector2(x, 0), origin + new Vector2(x, size.Y), Pack(Alpha(Border, 0.24f)));
        for (var y = (gridOrigin.Y % grid + grid) % grid; y < size.Y; y += grid)
            draw.AddLine(origin + new Vector2(0, y), origin + new Vector2(size.X, y), Pack(Alpha(Border, 0.24f)));
        Label(draw, origin + new Vector2(size.X - U(24), U(10)), "N", Muted, 11);
        var north = origin + new Vector2(size.X - U(20), U(34));
        draw.AddTriangleFilled(north + new Vector2(0, -U(9)), north + new Vector2(-U(4), U(2)), north + new Vector2(U(4), U(2)), Pack(Muted));
        if (points.Count == 0)
        {
            DrawCompass(draw, origin + size / 2 - new Vector2(0, U(12)), U(28), Border);
            CenterLabel(draw, origin + size / 2 + new Vector2(0, U(34)), "規劃後顯示巡查順序", Muted, 17);
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) mapContext = null;
            draw.PopClipRect(); return;
        }
        var projected = points.Select(p => Project(p.Spot.Position)).ToArray();
        var hoveredIndex = -1; var hoverDistance = U(12);
        if (hovered)
            for (var i = 0; i < projected.Length; i++)
            {
                var distance = Vector2.Distance(ImGui.GetMousePos(), projected[i]);
                if (distance < hoverDistance) { hoveredIndex = i; hoverDistance = distance; }
            }
        var headId = state.Route.FirstOrDefault()?.Spot.Id;
        foreach (var leg in ground)
        {
            var first = leg.DestinationId == headId;
            for (var j = 1; j < leg.Path.Count; j++)
                draw.AddLine(Project(leg.Path[j - 1]), Project(leg.Path[j]), Pack(Alpha(Mint, first ? 0.85f : 0.30f)), U(first ? 2 : 1.4f));
        }
        var labelBounds = new List<(Vector2 Min, Vector2 Max)>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i]; var p = projected[i]; var head = point.Spot.Id == headId;
            if (p.X < origin.X - U(16) || p.Y < origin.Y - U(16) || p.X > origin.X + size.X + U(16) || p.Y > origin.Y + size.Y + U(16)) continue;
            var complete = point.Status is SpotStatus.Visited or SpotStatus.Skipped;
            var color = complete ? Muted : KindColor(point.Spot.Kind);
            var selected = chart && point.ChartNumber == state.Controls!.StartNumber;
            if (head || i == hoveredIndex) draw.AddCircleFilled(p, U(12), Pack(Alpha(color, 0.15f)));
            draw.AddCircleFilled(p, U(head ? 5 : 3.5f), Pack(Alpha(color, complete ? 0.3f : point.Status == SpotStatus.Visible ? 1 : 0.75f)));
            if (head || selected) draw.AddCircle(p, U(selected ? 10 : 8), Pack(selected ? Mint : color), 20, U(1));
            if (chart || i < 8 || i == hoveredIndex)
            {
                var text = point.ChartNumber is { } n ? n.ToString("00") : (i + 1).ToString("00");
                var textPosition = p + new Vector2(U(9), -U(11)); var textEnd = textPosition + ImGui.CalcTextSize(text) * 13 / 17;
                if (head || selected || i == hoveredIndex || labelBounds.All(r => textEnd.X < r.Min.X || textPosition.X > r.Max.X || textEnd.Y < r.Min.Y || textPosition.Y > r.Max.Y))
                {
                    draw.AddRectFilled(textPosition - new Vector2(U(2)), textEnd + new Vector2(U(2)), Pack(Alpha(Background, 0.9f)), U(2));
                    Label(draw, textPosition, text, head || selected ? Text : color, 13); labelBounds.Add((textPosition, textEnd));
                }
            }
        }
        var player = Project(state.Position); draw.AddCircleFilled(player, U(11), Pack(Alpha(Mint, 0.15f)));
        draw.AddQuadFilled(player - new Vector2(0, U(6)), player + new Vector2(U(5), 0), player + new Vector2(0, U(6)), player - new Vector2(U(5), 0), Pack(Mint));
        Label(draw, player + new Vector2(U(9), U(7)), "你", Mint, 13); draw.PopClipRect();
        if (hoveredIndex >= 0)
        {
            var point = points[hoveredIndex];
            var title = point.ChartNumber is { } number ? $"圖表 #{number:00}" : $"第 {hoveredIndex + 1} 站";
            ImGui.SetTooltip($"{title} · {point.Spot.Name ?? KindName(point.Spot.Kind)}\n{point.Coordinates}\n{StatusName(point.Status)}\n點擊插旗" +
                (chart ? "\nCtrl＋點擊設定起點，再按「從此編號開始」" : ""));
            if (clicked)
            {
                if (chart && io.KeyCtrl && point.ChartNumber is { } startNumber) actions.SetChartStart?.Invoke(startNumber);
                else actions.Flag(point.Spot);
            }
        }
        else if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) mapContext = null;
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

    private void DrawSettings(CompassViewState state, CompassActions actions)
    {
        ImGui.TextColored(Mint, "顯示設定");
        var hideOthers = state.HideOtherPlayers;
        if (ImGui.Checkbox("隱藏非小隊／好友玩家（倒地仍顯示）", ref hideOthers)) actions.SetHideOtherPlayers?.Invoke(hideOthers);
        HoverHint("僅在新月島隱藏其他玩家模型，保留自己、小隊成員、好友與倒地玩家。復活後若仍不屬於小隊／好友，會重新隱藏。\n關閉、離島、傳送、過場、合照或卸載時，撤回本功能的隱藏設定。");
        ImGui.TextWrapped(state.PlayerVisibilityDetail);
        var hints = state.WorldHints;
        if (ImGui.Checkbox("場景位置提示與下一站標示", ref hints)) actions.SetWorldHints(hints);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Mint, "巡查紀錄");
        ImGui.BeginDisabled(!state.Active);
        if (ImGui.Button("清除巡查紀錄並重排")) actions.ClearSurvey?.Invoke();
        HoverHint("清除手動完成與自動略過紀錄；已確認開啟且未重新出現的箱子仍排除。");
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("資料範圍與路線說明")) return;
        ImGui.TextWrapped("全島已知位置只包含本場曾偵測到的物件，不是個人可開寶箱完整清單。候選點與曾看見的位置都需到場確認；數量快照不代表座標已知。");
        ImGui.TextWrapped("探索筆記限島上 12 處，排除塔內避世書庫。未探索篩選依目前角色的遊戲完成狀態；「已巡查」另記本輪手動進度，清除巡查不會重設遊戲紀錄。");
        ImGui.TextWrapped("自動略過代表範圍內連續沒有可用寶箱，不當作已開箱。重新巡查保留紀錄並優先續巡；清除紀錄才會重跑。略過位置重新出現可再規劃。");
        ImGui.TextWrapped(state.Region.Contains("北") ? "北區點位取自公開資料；本機繁中客戶端無此區域，尚未完成遊戲資料核對。" : "南區野外 68／68 點已與繁中場景核對；塔內另有 14 點。魔法罐候選資料未經伺服器完整性驗證。");
        ImGui.TextWrapped("圖表模式保留南部 1～68 編號順序；vnavmesh 只計算站間走法。地形最短模式則在 12 站內求此模型與優先條件下的最短順序，更多站點近似最佳化。同島同分流傳送保留路線，使用者暫停也會保留；落地後從目前位置接續。未計入敵人、機關、解鎖與傳送時間。");
        if (state.TotalStops > 0) ImGui.TextColored(Muted, $"已取得路段合計 {state.PlannedDistance:F0} m · {(state.Controls?.ChartMode == true ? "圖表固定順序" : state.Exact ? "此距離模型下最短順序" : "近似最佳化順序")}");
    }

    private bool Chip(string text, bool selected, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Alpha(accent, 0.15f) : Surface);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? accent : Muted);
        var result = ImGui.Button(text, new Vector2(0, U(32))); ImGui.PopStyleColor(2); return result;
    }
    private static bool PrimaryButton(string text, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Mint); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.54f, 0.94f, 0.82f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.73f, 0.62f, 1)); ImGui.PushStyleColor(ImGuiCol.Text, Background);
        var result = ImGui.Button(text, size); ImGui.PopStyleColor(4); return result;
    }
    private void DrawStatus(SpotStatus status, bool tower = false)
    {
        var color = status switch { SpotStatus.Visible or SpotStatus.Explored => Mint, SpotStatus.Unexplored => KindColor(SpotKind.Exploration), SpotStatus.LastSeen => Carrot, _ => Muted };
        var width = Badge(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos() + new Vector2(0, U(4)), tower ? "塔內 · 需解鎖" : StatusName(status), color, status == SpotStatus.Visible);
        ImGui.InvisibleButton("status", new Vector2(width, U(30)));
    }
    private float Badge(ImDrawListPtr draw, Vector2 origin, string text, Vector4 color, bool dot)
    {
        var width = ImGui.CalcTextSize(text).X * 13 / 17 + U(dot ? 27 : 17);
        draw.AddRectFilled(origin, origin + new Vector2(width, U(24)), Pack(Alpha(color, 0.12f)), U(5));
        if (dot) draw.AddCircleFilled(origin + new Vector2(U(10), U(12)), U(2.5f), Pack(color));
        Label(draw, origin + new Vector2(U(dot ? 18 : 8), U(5)), text, color, 13); return width;
    }
    private void Label(ImDrawListPtr draw, Vector2 position, string text, Vector4 color, float fontSize) => draw.AddText(ImGui.GetFont(), U(fontSize), position, Pack(color), text, 0);
    private void CenterLabel(ImDrawListPtr draw, Vector2 position, string text, Vector4 color, float fontSize) => Label(draw, position - new Vector2(ImGui.CalcTextSize(text).X * fontSize / 17 / 2, 0), text, color, fontSize);
    private static void HoverHint(string message) { if (ImGui.IsItemHovered()) ImGui.SetTooltip(message); }
    internal static void DrawPin(ImDrawListPtr draw, Vector2 center, float radius, Vector4 color)
    {
        draw.AddCircle(center - new Vector2(0, radius * 0.4f), radius * 0.65f, Pack(color), 16, 1.5f);
        draw.AddLine(center + new Vector2(-radius * 0.55f, 0), center + new Vector2(0, radius), Pack(color), 1.5f);
        draw.AddLine(center + new Vector2(radius * 0.55f, 0), center + new Vector2(0, radius), Pack(color), 1.5f);
    }
    private static void DrawCompass(ImDrawListPtr draw, Vector2 center, float radius, Vector4 color)
    {
        draw.AddCircle(center, radius, Pack(Alpha(color, 0.45f)), 48, 1.3f);
        var n = center - new Vector2(0, radius * 0.78f); var s = center + new Vector2(0, radius * 0.78f);
        var w = center - new Vector2(radius * 0.27f, 0); var e = center + new Vector2(radius * 0.27f, 0);
        draw.AddTriangleFilled(n, w, e, Pack(color)); draw.AddTriangleFilled(s, w, e, Pack(Alpha(color, 0.25f)));
        draw.AddCircleFilled(center, radius * 0.09f, Pack(Background));
    }
}
