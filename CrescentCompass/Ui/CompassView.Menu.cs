using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal enum CompassPage { Patrol, Pot, Ce, Exploration, Settings, Fate, Waymarks, Loot }

internal sealed partial class CompassView
{
    internal CompassPage Page { get; set; }
    private bool ceRecordedOnly;
    internal IReadOnlyList<(CompassPage Page, Vector2 Center)> MenuTargets => menuTargets;
    private readonly List<(CompassPage Page, Vector2 Center)> menuTargets = [];
    internal Dictionary<CompassPage, Vector2> MenuItemTargets { get; } = [];
    internal int CeRowsDrawn { get; private set; }

    private void DrawNavigation(CompassViewState state, CompassActions actions)
    {
        string[] labels = ["巡查", "魔法罐", "FATE", "CE 冷卻", "探索筆記", "標點", "背包整理", "設定"];
        string[] destinations = ["路線與地點", "尋寶與 FATE", "事件與自動標點", "查看冷卻與觸發條件", "查看島上地點", "儲存、匯入與還原", "背包整理", "顯示與紀錄設定"];
        CompassPage[] pages = [CompassPage.Patrol, CompassPage.Pot, CompassPage.Fate, CompassPage.Ce, CompassPage.Exploration, CompassPage.Waymarks, CompassPage.Loot, CompassPage.Settings];
        menuTargets.Clear();
        MenuItemTargets.Clear();
        if (!ImGui.BeginMenuBar()) return;
        for (var i = 0; i < labels.Length; i++)
        {
            var page = pages[i];
            var selected = Page == page;
            var menuDraw = ImGui.GetWindowDrawList();
            var menuAccent = Destinations.Single(d => d.Page == page).Color;
            ImGui.PushStyleColor(ImGuiCol.Text, menuAccent);
            var open = ImGui.BeginMenu(labels[i]);
            ImGui.PopStyleColor();
            if (selected)
            {
                var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
                menuDraw.AddLine(new Vector2(min.X + U(5), max.Y - U(2)),
                    new Vector2(max.X - U(5), max.Y - U(2)), Pack(menuAccent), U(3));
            }
            menuTargets.Add((page, (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2));
            if (!open) continue;
            if (ImGui.MenuItem(destinations[i], "", Page == page)) Page = page;
            MenuItemTargets[page] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.Separator();
            switch (page)
            {
                case CompassPage.Patrol:
                    var hasRoute = (state.Active || state.Transit) && (state.Route.Count > 0 || state.Planning);
                    if (ImGui.MenuItem("暫停巡查", "", false, hasRoute && state.Controls?.Paused != true)) actions.Pause?.Invoke();
                    if (ImGui.MenuItem("繼續巡查", "", false, hasRoute && state.Active && state.Controls?.Paused == true)) actions.Resume?.Invoke();
                    if (ImGui.MenuItem("終止巡查", "", false, hasRoute)) actions.Stop?.Invoke();
                    break;
                case CompassPage.Pot:
                    if (ImGui.MenuItem("自動追蹤位置與旗標", "", state.Pot?.AutoFlag == true)) actions.SetPotAutoFlag?.Invoke(state.Pot?.AutoFlag != true);
                    if (ImGui.MenuItem("FATE 出現時通知", "", state.Fates?.Notify == true)) actions.SetPotFateNotify?.Invoke(state.Fates?.Notify != true);
                    break;
                case CompassPage.Ce:
                    if (ImGui.MenuItem("自動記錄 CE 冷卻", "", state.Ce?.Enabled == true)) actions.SetCeTracking?.Invoke(state.Ce?.Enabled != true);
                    break;
                case CompassPage.Fate:
                    if (ImGui.MenuItem("出現時自動標點", "", state.GeneralFates?.AutoFlag == true)) actions.SetFateAutoFlag?.Invoke(state.GeneralFates?.AutoFlag != true);
                    if (ImGui.MenuItem("解除本次標點並接續巡查", "", false, state.GeneralFates?.Holding == true)) actions.ReleaseFateNavigation?.Invoke();
                    break;
                case CompassPage.Exploration:
                    if (ImGui.MenuItem("納入巡查與場景提示", "", state.Filters.Exploration)) actions.SetFilters(state.Filters with { Exploration = !state.Filters.Exploration });
                    if (ImGui.MenuItem("只顯示未探索地點", "", state.Filters.OnlyUnexplored)) actions.SetFilters(state.Filters with { OnlyUnexplored = !state.Filters.OnlyUnexplored });
                    break;
                case CompassPage.Settings:
                    if (ImGui.MenuItem("幻影職業／巨集", "/crescent jobs")) { Page = CompassPage.Settings; ShowPhantomJobs = true; }
                    if (ImGui.MenuItem("場景位置提示", "", state.WorldHints)) actions.SetWorldHints(!state.WorldHints);
                    if (ImGui.MenuItem("隱藏其他玩家（保留倒地者）", "", state.HideOtherPlayers)) actions.SetHideOtherPlayers?.Invoke(!state.HideOtherPlayers);
                    break;
                case CompassPage.Waymarks:
                    if (ImGui.MenuItem("取消放置標點", "", false, state.Waymarks?.Busy == true)) actions.Waymarks?.Cancel();
                    break;
            }
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();
    }

    private void DrawCeCooldowns(CompassViewState state, CompassActions actions)
    {
        CeRowsDrawn = 0;
        ImGui.TextColored(Sky, "危命任務 · 本場冷卻紀錄");
        var ce = state.Ce;
        var enabled = ce?.Enabled ?? true;
        if (ImGui.Checkbox("自動記錄 CE 冷卻", ref enabled)) actions.SetCeTracking?.Invoke(enabled);
        HoverHint("關閉視窗或切換選單仍會追蹤。關閉此選項會保留已知結束時間，停止觀測新事件；未觀測的結束時間不補算。");
        ImGui.SameLine();
        if (ImGui.Button("清除本場 CE 紀錄")) actions.ClearCeCooldowns?.Invoke();
        ImGui.TextWrapped("自動出現約 120 分鐘；打怪觸發約 60 分鐘。從觀測到戰鬥結束時計算，屬社群預估，非伺服器倒數；到期仍需等待事件或觸發條件。");
        ImGui.TextWrapped("觸發怪物與位置供參考；擊殺數未確認。冷卻結束後仍需符合觸發條件。");
        var available = (state.Active || state.Transit) && state.Region.Contains("南");
        if (!available)
            ImGui.TextWrapped(state.Active ? "目前僅提供已核對的南部 15 個 CE；北部尚未支援。以下為南部清單。" : "進島前也可查看南部 15 個 CE；進入新月島南部後開始記錄，上島前的結束時間未知。");
        ImGui.TextColored(available && (ce?.Enabled != true || ce.Snapshot.ScanFresh != true) ? Carrot : Muted,
            !available ? "南部清單預覽 · 尚無本場事件資料" : state.Transit ? "傳送中 · 已知冷卻保留，暫停事件觀測" : ce?.Enabled != true ? "記錄已關閉 · 已知冷卻繼續計時" :
            ce.Snapshot.ScanFresh ? "事件資料已更新 · 同島傳送保留紀錄，離島／換分流／重載清除" : "等待有效事件資料 · 不將讀取中斷當成戰鬥結束");
        ImGui.Checkbox("只顯示已觀測的 CE", ref ceRecordedOnly);
        var now = ce?.Now ?? DateTimeOffset.UtcNow;
        var snapshot = available && ce is not null ? ce.Snapshot : new CeCooldownTracker().Snapshot(now);
        var rows = snapshot.Entries.Where(e => !ceRecordedOnly || e.LastSeen is not null)
            .Select(e => (!state.Active || !enabled || !snapshot.ScanFresh) &&
                e.Status is CeStatus.Register or CeStatus.Warmup or CeStatus.Battle or CeStatus.ConfirmingEnd
                ? e with { Status = CeStatus.EndUnobserved } : e).ToArray();
        if (rows.Length == 0) { ImGui.TextColored(Muted, "本場尚無已觀測的 CE。"); return; }
        var height = Math.Max(U(220), ImGui.GetContentRegionAvail().Y - U(30));
        if (!ImGui.BeginTable("ce-cooldowns", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings, new Vector2(0, height))) return;
        ImGui.TableSetupColumn("危命任務／觸發方式", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("目前狀態", ImGuiTableColumnFlags.WidthFixed, U(133));
        ImGui.TableSetupColumn("上次觀測結束", ImGuiTableColumnFlags.WidthFixed, U(122));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        foreach (var row in rows.OrderBy(e => CeOrder(e.Status)).ThenBy(e => e.EligibleAt).ThenBy(e => e.Definition.Id))
        {
            CeRowsDrawn++;
            ImGui.PushID(row.Definition.Id);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, U(61)); ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Definition.Name);
            ImGui.TextColored(Muted, $"{(row.Definition.MobTriggered ? "打怪觸發" : "自動出現")} · 約 {row.Definition.Cooldown.TotalMinutes:0} 分鐘");
            ImGui.PushStyleColor(ImGuiCol.Text, Mint);
            ImGui.TextWrapped(row.Definition.TriggerCondition);
            ImGui.PopStyleColor();
            ImGui.TableNextColumn();
            ImGui.TextColored(row.Status is CeStatus.Battle or CeStatus.Register or CeStatus.Warmup ? Mint : row.Status == CeStatus.Cooldown ? Carrot : Muted, CeLabel(row, now));
            if (row.Status == CeStatus.Cooldown && row.EligibleAt is { } until) ImGui.TextColored(Muted, $"預估至 {until.ToLocalTime():HH:mm:ss}");
            if (row.Status == CeStatus.Eligible) ImGui.TextColored(Muted, "等待實際觸發");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.EndedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—");
            if (row.EndedAt is null && row.LastSeen is { } seen)
            { ImGui.TextColored(Muted, $"看見 {seen.ToLocalTime():HH:mm:ss}"); }
            if (row.Status == CeStatus.EndUnobserved) HoverHint("曾看見事件，但中斷或資料缺漏期間未觀測到戰鬥結束，無法建立可信的冷卻起算時間。");
            ImGui.PopID();
        }
        ImGui.EndTable();
        ImGui.TextColored(Muted, "首次上島不回推未知冷卻；預估到期不代表已刷新。");
    }

    private static int CeOrder(CeStatus status) => status switch
    { CeStatus.Register or CeStatus.Warmup or CeStatus.Battle => 0, CeStatus.ConfirmingEnd => 1, CeStatus.Cooldown => 2, CeStatus.Eligible => 3, _ => 4 };

    internal static string CeLabel(CeCooldownEntry row, DateTimeOffset now) => row.Status switch
    {
        CeStatus.Register => "報名中", CeStatus.Warmup => "準備中", CeStatus.Battle => $"戰鬥中 · {row.Progress}%",
        CeStatus.ConfirmingEnd => "確認結束中", CeStatus.EndUnobserved => "結束未觀測",
        CeStatus.Eligible => "預估冷卻已到期",
        CeStatus.Cooldown when row.EligibleAt is { } at => $"預估 {Countdown(at - now)}",
        _ => "未觀測 · 未知",
    };

    private static string Countdown(TimeSpan value)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(value.TotalSeconds));
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    private void DrawExplorationPage(CompassViewState state, CompassActions actions)
    {
        ImGui.TextColored(KindColor(SpotKind.Exploration), "島上探索筆記 · 南部 12 處");
        ImGui.TextWrapped("只包含島上的探索地點，排除塔內避世書庫。可直接插旗；勾選納入後，可至巡查路線使用地形最短順序規劃。");
        var filters = state.Filters;
        var include = filters.Exploration;
        if (ImGui.Checkbox("納入巡查目標與場景提示", ref include)) filters = filters with { Exploration = include };
        var only = filters.OnlyUnexplored;
        if (ImGui.Checkbox("只顯示未探索的筆記地點", ref only)) filters = filters with { OnlyUnexplored = only };
        if (filters != state.Filters) actions.SetFilters(filters);
        ImGui.TextWrapped(state.Transit ? "傳送中，探索紀錄保留；落地後自動更新。" : state.ExplorationDetail);
        if (state.Message.StartsWith("旗標：", StringComparison.Ordinal) || state.Message.StartsWith("地圖旗標", StringComparison.Ordinal))
            ImGui.TextWrapped(state.Message);
        if (!state.Active) { DrawWaiting(state); return; }
        var rows = state.Points.Where(p => p.Spot.Kind == SpotKind.Exploration).ToArray();
        if (rows.Length == 0) { ImGui.TextColored(Muted, "目前沒有符合篩選的島上筆記地點。"); return; }
        if (!ImGui.BeginTable("exploration-points", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings,
            new Vector2(0, Math.Max(U(200), ImGui.GetContentRegionAvail().Y - U(12))))) return;
        ImGui.TableSetupColumn("筆記／座標", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("探索狀態", ImGuiTableColumnFlags.WidthFixed, U(140));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, U(65));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        foreach (var row in rows)
        {
            ImGui.PushID(row.Spot.Id); ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Spot.Name ?? "探索地點"); ImGui.TextColored(Muted, row.Coordinates);
            ImGui.TableNextColumn(); DrawStatus(row.Status);
            ImGui.TableNextColumn(); if (ImGui.Button("插旗")) actions.Flag(row.Spot);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
}
