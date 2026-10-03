using System.Globalization;

namespace CrescentCompass.Core;

public sealed record PhantomJob(byte Id, string Name, string EnglishName, uint StatusId, uint IconId)
{
    public string ShortName => Name[2..];
    public string Macro => $"/crescent job {Id}";
}

/// <summary>TC MKDSupportJob rows and corresponding Status icons, audited against the local client.</summary>
public static class PhantomJobs
{
    public static IReadOnlyList<PhantomJob> All { get; } = Array.AsReadOnly<PhantomJob>([
        new(0, "輔助自由人", "Phantom Freelancer", 4242, 216871),
        new(1, "輔助騎士", "Phantom Knight", 4358, 216872),
        new(2, "輔助狂戰士", "Phantom Berserker", 4359, 216873),
        new(3, "輔助武僧", "Phantom Monk", 4360, 216874),
        new(4, "輔助獵人", "Phantom Ranger", 4361, 216875),
        new(5, "輔助武士", "Phantom Samurai", 4362, 216876),
        new(6, "輔助吟遊詩人", "Phantom Bard", 4363, 216877),
        new(7, "輔助風水士", "Phantom Geomancer", 4364, 216878),
        new(8, "輔助時魔道士", "Phantom Time Mage", 4365, 216879),
        new(9, "輔助砲擊士", "Phantom Cannoneer", 4366, 216880),
        new(10, "輔助藥劑師", "Phantom Chemist", 4367, 216881),
        new(11, "輔助預言士", "Phantom Oracle", 4368, 216882),
        new(12, "輔助盜賊", "Phantom Thief", 4369, 216883),
    ]);

    public static PhantomJob? Find(byte id) => All.FirstOrDefault(j => j.Id == id);

    public static PhantomJob? Resolve(string input)
    {
        var value = input.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1].Trim();
        if (byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return Find(id);
        return All.FirstOrDefault(j => new[] { j.Name, j.ShortName, "幻影" + j.ShortName, j.EnglishName, j.EnglishName[8..] }
            .Any(alias => string.Equals(value, alias, StringComparison.OrdinalIgnoreCase)));
    }
}

public readonly record struct PhantomJobContext(bool Ready, ushort Territory, uint Instance, ulong Character,
    byte? CurrentJob, bool InCombat = false, bool Occupied = false, bool Dead = false)
{
    public string BlockReason => !Ready || Character == 0 || !SpotCatalog.IsSupported(Territory) || CurrentJob is null
        ? "請進入新月島，等待職業資料載入。" : Dead ? "倒地時無法切換幻影職業。" :
        InCombat ? "戰鬥中無法切換幻影職業。" : Occupied ? "互動、讀條或移動過場中，請稍後切換。" : "";
}

/// <summary>A single user request, never automatically retried. Success requires observed game state.</summary>
public sealed class PhantomJobSwitcher
{
    public const int MinimumIntervalMs = 1000;
    public const int ConfirmationTimeoutMs = 3000;
    private (ushort Territory, uint Instance, ulong Character)? session;
    private long? lastRequest;
    private long lastTick;
    private byte? pending;
    public bool Busy => pending is not null;
    public string Detail { get; private set; } = "選擇職業切換，或複製指令放入遊戲巨集。";

    public string Request(byte id, PhantomJobContext context, long now, Func<byte, bool> change)
    {
        Update(context, now);
        if (PhantomJobs.Find(id) is not { } job) return "未知的幻影職業；請使用 0～12 或完整名稱。";
        if (context.BlockReason is { Length: > 0 } reason) return reason;
        if (Busy) return "上一個切換請求仍在等待遊戲回覆。";
        if (context.CurrentJob == id) return Detail = $"目前已是{job.Name}。";
        if (lastRequest is { } last && now - last < MinimumIntervalMs) return "切換過於頻繁，請稍後再試。";
        lastRequest = now; // Reserve before native code, including rejection or exception.
        if (!change(id)) return Detail = "職業切換介面尚未就緒或遊戲未接受，請稍後再試。";
        pending = id;
        return Detail = $"已送出切換{job.Name}的請求，等待遊戲確認。";
    }

    public string? Update(PhantomJobContext context, long now)
    {
        var identity = (context.Territory, context.Instance, context.Character);
        if (session != identity || now < lastTick || !context.Ready)
        {
            var cancelled = Busy;
            pending = null; lastRequest = null; session = identity; lastTick = now;
            Detail = "選擇職業切換，或複製指令放入遊戲巨集。";
            return cancelled ? Detail = "區域或角色狀態已變更，取消等待職業切換結果。" : null;
        }
        lastTick = now;
        if (pending is not { } id) return null;
        if (context.CurrentJob == id)
        {
            pending = null;
            return Detail = $"已切換為{PhantomJobs.Find(id)!.Name}。";
        }
        if (now - lastRequest >= ConfirmationTimeoutMs)
        {
            pending = null;
            return Detail = "尚未確認職業切換；請檢查職業是否已解鎖、切換冷卻及遊戲提示後再試。";
        }
        return null;
    }
}
