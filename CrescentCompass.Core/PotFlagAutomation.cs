namespace CrescentCompass.Core;

/// <summary>One automatic flag per search update, independent of the general chest filter.</summary>
public sealed class PotFlagAutomation
{
    private int revision = -1;
    private bool wasEnabled;
    private bool pending;
    private long? lastAttempt;
    private int attempts;
    public bool OwnsNavigation { get; private set; }
    public string Detail { get; private set; } = "取得魔法罐後自動標示搜尋點。";

    public void Reset()
    {
        revision = -1; wasEnabled = pending = OwnsNavigation = false; lastAttempt = null; attempts = 0;
        Detail = "取得魔法罐後自動標示搜尋點。";
    }

    public void Update(PotSession pot, bool enabled, bool canFlag, long now, Func<bool> flag)
    {
        OwnsNavigation = enabled && pot.Searching;
        if (!OwnsNavigation)
        {
            Reset();
            Detail = enabled ? "等待下一次取得魔法罐。" : "自動標點已關閉；提示辨識仍會更新。";
            return;
        }
        if (revision != pot.Revision || !wasEnabled)
        {
            revision = pot.Revision; pending = pot.Target is { } target && Coordinates.IsFinite(target); attempts = 0;
        }
        wasEnabled = true;
        if (pot.Target is null) { pending = false; Detail = "等待有效提示或現身寶箱，再自動更新旗標。"; return; }
        if (!pending) { Detail = "旗標已同步；收到新提示時自動更新。"; return; }
        if (!canFlag) { Detail = "等待互動或讀條結束後自動插旗。"; return; }
        var delay = attempts >= 3 ? 10_000 : 1500;
        if (lastAttempt is { } previous && now - previous < delay) return;

        // Record even an exception so a temporary API failure cannot trigger a retry on every scan.
        lastAttempt = now;
        attempts = Math.Min(attempts + 1, 3);
        Detail = "插旗暫未成功，會自動重試。";
        if (!flag()) return;
        pending = false;
        Detail = pot.Revealed ? "已自動標示現身寶箱；等待下一次提示。" : "已自動標示搜尋候選點；使用聖靈藥後繼續更新。";
    }
}
