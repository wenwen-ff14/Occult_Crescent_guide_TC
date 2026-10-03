using CrescentCompass.Core;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private long lastMacroIconScan;
    private bool macroIconFaulted;
    private string macroIconDetail = "儲存單行職業巨集並關閉編輯視窗後，會自動更新快捷列圖示。";
    internal string PhantomMacroIconDetail => macroIconDetail;

    private void UpdatePhantomMacroIcons()
    {
        var now = Environment.TickCount64;
        if (macroIconFaulted || now - lastMacroIconScan < 2000) return;
        lastMacroIconScan = now;
        RefreshPhantomMacroIcons(false);
    }

    internal void RefreshPhantomMacroIcons() => _ = Framework.RunOnFrameworkThread(() =>
    {
        if (disposed) return;
        macroIconFaulted = false;
        lastMacroIconScan = Environment.TickCount64;
        RefreshPhantomMacroIcons(true);
    });

    private unsafe void RefreshPhantomMacroIcons(bool announce)
    {
        if (disposed) return;
        var changed = 0;
        try
        {
            if (!Client.IsLoggedIn || !PlayerState.IsLoaded || PlayerState.ContentId == 0 || Objects.LocalPlayer is null || IsLoading || Client.IsGPosing)
            { macroIconDetail = "登入且角色資料就緒後，自動更新職業巨集圖示。"; return; }
            if (Conditions[ConditionFlag.InCombat] || IsOccupied)
            { macroIconDetail = "戰鬥或互動結束後更新職業巨集圖示。"; return; }
            var editor = AgentMacro.Instance();
            if (editor == null || editor->IsAgentActive())
            { macroIconDetail = "請先關閉遊戲的巨集編輯視窗，再更新圖示。"; return; }
            var macros = RaptureMacroModule.Instance();
            var hotbars = RaptureHotbarModule.Instance();
            if (macros == null || macros->CharacterContentId != PlayerState.ContentId || hotbars == null || !hotbars->DatFileLoadedSuccessfully)
            { macroIconDetail = "等待目前角色的巨集與快捷列資料載入。"; return; }

            // Both pages are bounded by the SDK's 100 macros per page. Pointers never escape this framework tick.
            try
            {
                for (byte set = 0; set < 2; set++)
                for (byte index = 0; index < 100; index++)
                {
                    var macro = macros->GetMacro(set, index);
                    if (macro == null) continue;
                    var lines = new string[15];
                    for (var line = 0; line < lines.Length; line++) lines[line] = macro->Lines[line].ToString();
                    if (PhantomMacroIcons.Replacement(macro->IconId, lines) is not { } icon) continue;
                    macro->SetIcon(icon);
                    macros->SetSavePendingFlag(true, set);
                    changed++;
                    if (macro->IconId != icon) throw new InvalidOperationException("Game macro icon did not match the requested icon.");
                    hotbars->ReloadMacroSlots(set, index);
                }
            }
            finally
            {
                // Use the game's save API, including a partial batch if a later update failed.
                if (changed > 0) macros->SaveFile(true);
            }
            macroIconDetail = changed > 0 ? $"已更新 {changed} 個職業巨集圖示，快捷列已重新載入。" :
                "職業巨集圖示已檢查；新增巨集後關閉編輯視窗便會自動套用。";
        }
        catch (Exception error)
        {
            macroIconFaulted = true;
            macroIconDetail = $"圖示更新發生錯誤（本次已處理 {changed} 個），已停止自動更新；請查看 Dalamud 記錄。";
            Log.Error(error, "Phantom macro icon update failed");
        }
        finally { if (announce) Chat.Print($"[新月島羅盤] {macroIconDetail}"); }
    }
}
