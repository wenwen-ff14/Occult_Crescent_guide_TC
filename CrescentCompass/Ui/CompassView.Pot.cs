using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Bindings.ImGui;
using static CrescentCompass.Ui.CompassTheme;

namespace CrescentCompass.Ui;

internal sealed partial class CompassView
{
    internal Dictionary<string, Vector2> PotOverlayTargets { get; } = [];

    private void DrawPotOverlayControls(CompassViewState state, CompassActions actions)
    {
        PotOverlayTargets.Clear();
        var enabled = state.PotOverlay?.Enabled == true;
        if (ImGui.Checkbox("在畫面顯示魔法罐倒數", ref enabled)) actions.PotOverlay?.SetVisible(enabled);
        PotOverlayTargets["visible"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        HoverHint("關閉主介面後仍顯示倒數。僅在新月島顯示；離島、傳送、過場及隱藏遊戲介面時自動隱藏。\n沿用本場 FATE 紀錄推估，未知時間不補算；與出現通知、場景位置提示分開設定。");
        if (!enabled) return;
        var locked = state.PotOverlay?.Locked ?? true;
        if (ImGui.Button(locked ? "調整位置" : "完成調整"))
        {
            locked = !locked;
            actions.PotOverlay?.SetLocked(locked);
        }
        PotOverlayTargets["locked"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.SameLine();
        if (ImGui.SmallButton("重設倒數位置")) actions.PotOverlay?.ResetPosition();
        PotOverlayTargets["reset"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ImGui.TextWrapped(locked ? "位置已鎖定，滑鼠可穿透；按「調整位置」後拖曳浮窗。" :
            "拖曳倒數浮窗的任意位置移動，再按「完成調整」鎖定。放開滑鼠即保存位置。");
        ImGui.Spacing();
    }

    private void DrawFates(CompassViewState state, CompassActions actions)
    {
        if (state.Fates is not { } fates) return;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Lerp(Surface, Gold, 0.08f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(U(14), U(12)));
        var locations = fates.Locations ?? [];
        if (ImGui.BeginChild("pot-fates", new Vector2(0, U(190 + fates.Active.Count * 108 + (locations.Count > 0 ? 68 + locations.Count * 36 : 0))), true))
        {
            ImGui.TextColored(Gold, "魔法罐 FATE"); ImGui.SameLine();
            var notify = fates.Notify;
            if (ImGui.Checkbox("出現時通知", ref notify)) actions.SetPotFateNotify?.Invoke(notify);
            HoverHint("偵測到本場魔法罐 FATE 時，顯示一次 Dalamud 彈出通知與僅自己可見的聊天提示。\n關閉通知仍會追蹤倒數；插件視窗關閉時也會偵測。\n倒數是約 30 分鐘南北交替的推估，不是伺服器保證。換區、分流或重載後重新建立本場紀錄。");
            var notifySoon = fates.NotifySoon;
            if (ImGui.Checkbox("預估剩 5 分鐘時通知", ref notifySoon)) actions.SetPotFateSoonNotify?.Invoke(notifySoon);
            HoverHint("下一場預估倒數進入 5 分鐘內時，顯示彈出通知與個人聊天提示，每輪一次。\n主介面或倒數浮窗關閉時仍會提醒；未知或逾時不提醒。關閉後不補發該輪已略過的提醒。");
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

}
