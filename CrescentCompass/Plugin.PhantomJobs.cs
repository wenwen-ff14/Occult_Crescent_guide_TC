using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly PhantomJobSwitcher phantomSwitcher = new();
    private PhantomJobContext phantomContext;
    private long lastPhantomScan;
    private bool phantomFaulted;

    internal CompassPhantomJobs PhantomJobState => new(phantomContext.CurrentJob,
        !phantomFaulted && phantomContext.BlockReason.Length == 0 && !phantomSwitcher.Busy,
        phantomFaulted ? "職業介面讀取失敗，請查看 Dalamud 記錄。" :
        phantomContext.BlockReason is { Length: > 0 } reason ? reason : phantomSwitcher.Detail, PhantomMacroIconDetail);

    private unsafe PhantomJobContext ReadPhantomContext()
    {
        var chest = AutoChestContext;
        var ready = chest.Ready && !IsLoading && !disposed;
        byte? current = null;
        if (ready)
        {
            var content = PublicContentOccultCrescent.GetInstance();
            ready = content != null && content->StateLoaded;
            if (ready && PhantomJobs.Find(content->State.CurrentSupportJob) is not null) current = content->State.CurrentSupportJob;
        }
        return new(ready, Client.TerritoryType, Client.Instance, PlayerState.ContentId, current,
            chest.InCombat, chest.Occupied || chest.InFlight || chest.RidingPillion, chest.Dead);
    }

    private void UpdatePhantomJobs()
    {
        var now = Environment.TickCount64;
        if (phantomFaulted || now - lastPhantomScan < 250) return;
        lastPhantomScan = now;
        try
        {
            phantomContext = ReadPhantomContext();
            if (phantomSwitcher.Update(phantomContext, now) is { } result) Chat.Print($"[新月島羅盤] {result}");
        }
        catch (Exception error) { phantomFaulted = true; Log.Error(error, "Phantom job state read failed"); }
    }

    internal void SwitchPhantomJob(byte id)
    {
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            try
            {
                if (phantomFaulted) { Chat.Print("[新月島羅盤] 職業介面讀取失敗，請重新載入插件後再試。"); return; }
                phantomContext = ReadPhantomContext();
                var result = phantomSwitcher.Request(id, phantomContext, Environment.TickCount64, ChangePhantomJob);
                Chat.Print($"[新月島羅盤] {result}");
            }
            catch (Exception error)
            {
                phantomFaulted = true;
                Log.Error(error, "Phantom job switch failed");
                Chat.Print("[新月島羅盤] 職業切換失敗，請查看 Dalamud 記錄。");
            }
        });
    }

    private unsafe bool ChangePhantomJob(byte id)
    {
        // Re-read on the framework thread; never retain native pointers or write support-job state.
        if (ReadPhantomContext().BlockReason.Length != 0 || PhantomJobs.Find(id) is not { } job) return false;
        if (Data.GetExcelSheet<MKDSupportJob>()?.GetRowOrDefault(id) is not { } row || row.Unknown4.ToString() != job.EnglishName)
            return false;
        var agent = AgentMKDSupportJobList.Instance();
        if (agent == null) return false;
        agent->ChangeSupportJob(id);
        return true;
    }

    internal static bool DrawPhantomJobIcon(uint icon, System.Numerics.Vector2 size)
    {
        var texture = Textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault();
        if (texture is null) return false;
        ImGui.Image(texture.Handle, size);
        return true;
    }

    private bool HandlePhantomCommand(string args)
    {
        var parts = args.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (parts[0].Equals("jobicons", StringComparison.OrdinalIgnoreCase)) { RefreshPhantomMacroIcons(); return true; }
        if (parts[0].Equals("jobs", StringComparison.OrdinalIgnoreCase) ||
            parts.Length == 1 && parts[0].Equals("job", StringComparison.OrdinalIgnoreCase))
        { window.OpenPhantomJobs(); return true; }
        if (!parts[0].Equals("job", StringComparison.OrdinalIgnoreCase)) return false;
        if (PhantomJobs.Resolve(parts[1]) is { } job) SwitchPhantomJob(job.Id);
        else Chat.Print("[新月島羅盤] 未知職業。用法：/crescent job 騎士 或 /crescent job 1；/crescent jobs 查看圖標與全部指令。");
        return true;
    }
}
