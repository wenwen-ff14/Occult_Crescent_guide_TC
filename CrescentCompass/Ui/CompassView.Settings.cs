using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    private void DrawSettings(CompassViewState state, CompassActions actions)
    {
        if (ShowPhantomJobs) { DrawPhantomJobs(state, actions); return; }
        if (ToneButton("幻影職業／巨集 →", new Vector2(0, U(40)), Violet)) ShowPhantomJobs = true;
        ImGui.Spacing();
        ImGui.TextColored(Mint, "寶箱互動");
        DrawAutoChestControl(state, actions);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
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

    internal Vector2 AutoChestToggleTarget { get; private set; }
    private void DrawAutoChestControl(CompassViewState state, CompassActions actions)
    {
        var enabled = state.AutoOpenNearbyChests;
        if (ImGui.Checkbox("自動開啟附近寶箱（2 公尺）", ref enabled)) actions.SetAutoOpenNearbyChests?.Invoke(enabled);
        AutoChestToggleTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        HoverHint("只在新月島互動已載入、未開啟且可選取的寶箱；包含魔法罐／兔子寶箱。\n不受地圖顯示篩選影響，無需規劃路線；視窗關閉後仍有效。\n請自行靠近，支援地面騎乘；飛行、乘客、上下坐騎、戰鬥、讀條、傳送、倒地與巡查暫停時停止。\n靠近便嘗試，不用停穩；每秒最多一次，無次數上限，直到開啟、離開或狀態不允許。\n保留遊戲的距離與視線檢查。");
        if (enabled) ImGui.TextWrapped($"自動開箱：{state.AutoChestDetail}");
    }

}
