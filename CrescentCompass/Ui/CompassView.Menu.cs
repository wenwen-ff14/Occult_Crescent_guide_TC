using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal enum CompassPage { Patrol, Pot, Ce, Exploration, Settings, Fate, Waymarks, Loot }

internal sealed partial class CompassView
{
    internal CompassPage Page { get; set; }
    private bool ceUnseenOnly;
    internal bool CeUnseenOnly => ceUnseenOnly;
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
                    if (ImGui.MenuItem("在畫面顯示巡查進度", "", state.PatrolOverlay?.Enabled == true)) actions.PatrolOverlay?.SetVisible(state.PatrolOverlay?.Enabled != true);
                    break;
                case CompassPage.Pot:
                    if (ImGui.MenuItem("自動追蹤位置與旗標", "", state.Pot?.AutoFlag == true)) actions.SetPotAutoFlag?.Invoke(state.Pot?.AutoFlag != true);
                    if (ImGui.MenuItem("FATE 出現時通知", "", state.Fates?.Notify == true)) actions.SetPotFateNotify?.Invoke(state.Fates?.Notify != true);
                    if (ImGui.MenuItem("預估剩 5 分鐘時通知", "", state.Fates?.NotifySoon == true)) actions.SetPotFateSoonNotify?.Invoke(state.Fates?.NotifySoon != true);
                    if (ImGui.MenuItem("在畫面顯示魔法罐倒數", "", state.PotOverlay?.Enabled == true)) actions.PotOverlay?.SetVisible(state.PotOverlay?.Enabled != true);
                    break;
                case CompassPage.Ce:
                    if (ImGui.MenuItem("自動記錄 CE 冷卻", "", state.Ce?.Enabled == true)) actions.SetCeTracking?.Invoke(state.Ce?.Enabled != true);
                    break;
                case CompassPage.Fate:
                    if (ImGui.MenuItem("出現時自動標點", "", state.GeneralFates?.AutoFlag == true)) actions.SetFateAutoFlag?.Invoke(state.GeneralFates?.AutoFlag != true);
                    if (ImGui.MenuItem("解除本次標點並接續巡查", "", false, state.GeneralFates?.Holding == true)) actions.ReleaseFateNavigation?.Invoke();
                    break;
                case CompassPage.Exploration:
                    if (ImGui.MenuItem("顯示探索筆記與場景提示", "", state.Filters.Exploration)) actions.SetFilters(state.Filters with { Exploration = !state.Filters.Exploration });
                    if (ImGui.MenuItem("只顯示未探索地點", "", state.Filters.OnlyUnexplored)) actions.SetFilters(state.Filters with { OnlyUnexplored = !state.Filters.OnlyUnexplored });
                    break;
                case CompassPage.Settings:
                    if (ImGui.MenuItem("幻影職業／巨集", "/crescent jobs")) { Page = CompassPage.Settings; ShowPhantomJobs = true; }
                    if (ImGui.MenuItem("在畫面顯示幻影職業", "", state.PhantomOverlay?.Enabled == true, actions.PhantomOverlay is not null))
                        actions.PhantomOverlay?.SetVisible(state.PhantomOverlay?.Enabled != true);
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
        CeTargets.Clear(); CeRowsDrawn = 0;
        var ce = state.Ce;
        var enabled = ce?.Enabled ?? true;
        if (ImGui.Checkbox("自動記錄 CE 冷卻", ref enabled)) actions.SetCeTracking?.Invoke(enabled);
        ImGui.SameLine();
        if (ImGui.Button("清除本場 CE 紀錄")) actions.ClearCeCooldowns?.Invoke();
        var available = (state.Active || state.Transit) && state.Region.Contains("南");
        ImGui.TextWrapped(!available ? "南部地圖預覽 · 進入南部後開始記錄，上島前冷卻未知。" :
            state.Transit ? "傳送中 · 已知冷卻保留，暫停觀測" : !enabled ? "記錄已關閉 · 已知冷卻繼續計時" :
            ce?.Snapshot.ScanFresh == true ? "本場紀錄 · 冷卻為預估，到期仍需符合觸發條件。" : "等待事件資料 · 未觀測的冷卻保持未知");
        HoverHint("從觀測到戰鬥結束時計算：打怪觸發約 60 分鐘、自動出現約 120 分鐘。不是伺服器倒數；離島、換分流或重載清除本場紀錄。");
        var now = ce?.Now ?? DateTimeOffset.UtcNow;
        var snapshot = available && ce is not null ? ce.Snapshot : new CeCooldownTracker().Snapshot(now);
        var rows = snapshot.Entries.Select(e => (!state.Active || !enabled || !snapshot.ScanFresh) &&
            e.Status is CeStatus.Register or CeStatus.Warmup or CeStatus.Battle or CeStatus.ConfirmingEnd
            ? e with { Status = CeStatus.EndUnobserved } : e).ToArray();
        ImGui.Checkbox("只顯示尚未出現的 CE", ref ceUnseenOnly);
        CeTargets["unseen"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        HoverHint("依本次分流的觀測紀錄，僅保留尚未看過出現的 CE；未觀測不代表伺服器從未刷新。\n已觀測的 CE 即使冷卻到期也不列入；換分流或清除紀錄後重新累計。");
        ImGui.SameLine();
        if (ImGui.SmallButton("全圖")) ceMapReady = false;
        HoverHint("回到全圖。Ctrl＋滾輪縮放，滑鼠左／右／中鍵拖曳地圖。點選 BOSS 查看觸發條件，點座標插旗。");
        var shown = rows.Where(e => !ceUnseenOnly || e.LastSeen is null).ToArray();
        if (!shown.Any(e => e.Definition.Id == selectedCe)) selectedCe = shown.FirstOrDefault()?.Definition.Id ?? 0;
        DrawCeSelection(state, actions, shown, now);
        if (shown.Length == 0) ImGui.TextColored(Muted, "本次分流的 CE 均已有出現紀錄。");
        DrawCeMap(state, actions, shown, now);
        if (state.Message.StartsWith("CE 旗標：", StringComparison.Ordinal)) ImGui.TextWrapped(state.Message);
    }
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
        ImGui.TextWrapped("只包含島上的探索地點，排除塔內避世書庫。可直接插旗；勾選後顯示於地點清單與場景提示，南部 68 點巡查順序不變。");
        var filters = state.Filters;
        var include = filters.Exploration;
        if (ImGui.Checkbox("顯示於地點清單與場景提示", ref include)) filters = filters with { Exploration = include };
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
