using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CrescentCompass.Core;

public readonly record struct PotFingerprintFate(ushort Id, long StartUnix);
public sealed record PotTimeRequest(long Generation, ushort Territory, uint Instance, uint Datacenter, string InstanceKey)
{
    public IReadOnlyList<string> AdditionalKeys { get; init; } = [];
    public IEnumerable<string> Keys => new[] { InstanceKey }.Concat(AdditionalKeys);
    public bool Matches(string key) => Keys.Contains(key, StringComparer.OrdinalIgnoreCase);
}
public sealed record PotTimeResponse(string InstanceKey, ushort Territory, uint Datacenter, ushort FateId, long SpawnUnix);
public enum PotTimeFetchOutcome { Found, NotFound, Failed, Invalid }
public sealed record PotTimeHttpDiagnostic(int? StatusCode, long ElapsedMilliseconds, int BytesRead, string Code, string Detail);
public sealed record PotTimeFetchResult(PotTimeFetchOutcome Outcome, PotTimeResponse? Value = null, PotTimeHttpDiagnostic? Diagnostic = null);
public enum PotEntryTimeStatus { Outside, Waiting, Fetching, Imported, Local, NotFound, Failed, Invalid, Unavailable, Disabled }

/// <summary>One automatic attempt per entry; only an explicit manual retry can replenish it.</summary>
public sealed class PotEntryTimeSync
{
    public static readonly TimeSpan EntryWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(10);
    private long generation;
    private ushort territory;
    private DateTimeOffset attemptStartedAt;
    private bool manualAttempt;
    public int RequestCount { get; private set; }
    public int ManualRequestCount { get; private set; }
    public DateTimeOffset? EnteredAt { get; private set; }
    public DateTimeOffset? RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public PotTimeRequest? LastRequest { get; private set; }
    public PotFingerprintFate? QueryFate { get; private set; }
    public PotTimeFetchResult? LastResult { get; private set; }
    public string DiagnosticReason { get; private set; } = "尚未進入新月島。";
    public PotEntryTimeStatus Status { get; private set; }
    public string Detail => Status switch
    {
        PotEntryTimeStatus.Outside => "進島後查詢一次共享時間。",
        PotEntryTimeStatus.Waiting => "等待副本資料，準備一次性查詢。",
        PotEntryTimeStatus.Fetching => "正在取得共享時間。",
        PotEntryTimeStatus.Imported => "已取得共享時間，後續由本機計時。",
        PotEntryTimeStatus.Local => "已有本機觀測時間，無需共享資料。",
        PotEntryTimeStatus.NotFound => "查無共享時間，等待本機觀測；不自動重試。",
        PotEntryTimeStatus.Failed => "共享時間查詢失敗，等待本機觀測；不自動重試。",
        PotEntryTimeStatus.Invalid => "共享時間無效或已過期，等待本機觀測。",
        PotEntryTimeStatus.Unavailable => "未取得副本識別資料，改由本機觀測。",
        _ => "進島共享時間查詢已關閉。",
    };

    public void Enter(ushort currentTerritory, bool enabled, DateTimeOffset now)
    {
        generation++;
        territory = currentTerritory;
        attemptStartedAt = now;
        manualAttempt = false;
        RequestCount = ManualRequestCount = 0;
        EnteredAt = now;
        RequestedAt = CompletedAt = null;
        LastRequest = null; QueryFate = null; LastResult = null;
        Status = !SpotCatalog.IsSupported(territory) ? PotEntryTimeStatus.Outside :
            enabled ? PotEntryTimeStatus.Waiting : PotEntryTimeStatus.Disabled;
        DiagnosticReason = Status == PotEntryTimeStatus.Waiting ? "等待初始 FATE 表載入。" :
            Status == PotEntryTimeStatus.Outside ? "目前不在新月島。" : "進島時查詢開關已關閉，本場不發出請求。";
    }

    public void Disable()
    {
        generation++;
        Status = PotEntryTimeStatus.Disabled;
        DiagnosticReason = LastRequest is null ? "查詢已關閉；重新啟用於下次進島生效。" :
            "查詢已關閉；未完成的請求已取消，不套用晚到回應，已匯入的倒數保留。";
    }

    public string? RetryBlockedReason(bool hasLocalAnchor, DateTimeOffset now)
    {
        if (Status == PotEntryTimeStatus.Outside) return "目前不在新月島。";
        if (Status == PotEntryTimeStatus.Disabled) return "本場共享查詢已關閉；重新啟用於下次進島生效。";
        if (hasLocalAnchor || Status == PotEntryTimeStatus.Local) return "已有本機觀測時間，不以共享資料覆蓋。";
        if (Status == PotEntryTimeStatus.Imported) return "已取得共享時間，繼續由本機計時。";
        if (Status is PotEntryTimeStatus.Waiting or PotEntryTimeStatus.Fetching) return "查詢尚未完成，請等待結果。";
        if (RequestedAt is { } requested && now - requested < RetryCooldown)
            return $"請等待 {Math.Ceiling((RetryCooldown - (now - requested)).TotalSeconds):0} 秒後重試。";
        return null;
    }

    public bool TryRetry(bool hasLocalAnchor, DateTimeOffset now)
    {
        if (RetryBlockedReason(hasLocalAnchor, now) is not null) return false;
        generation++;
        attemptStartedAt = now;
        manualAttempt = true;
        RequestedAt = CompletedAt = null;
        LastRequest = null; QueryFate = null; LastResult = null;
        Status = PotEntryTimeStatus.Waiting;
        DiagnosticReason = "已手動重試，等待最新副本資料；只查詢一次、不上傳。";
        return true;
    }

    public PotTimeRequest? TryStart(uint datacenter, uint instance, IEnumerable<PotFingerprintFate> fates,
        bool hasLocalAnchor, DateTimeOffset now)
    {
        if (Status != PotEntryTimeStatus.Waiting) return null;
        if (hasLocalAnchor) { Status = PotEntryTimeStatus.Local; DiagnosticReason = "已有本機魔法罐紀錄，免除網路查詢。"; return null; }
        var elapsed = now - attemptStartedAt;
        if (elapsed < TimeSpan.Zero || elapsed >= EntryWindow)
        {
            Status = PotEntryTimeStatus.Unavailable;
            DiagnosticReason = elapsed < TimeSpan.Zero ? "系統時間倒退，停止本次查詢。" : $"{(manualAttempt ? "手動重試後" : "進島")} 60 秒內未能開始查詢。最後原因：{DiagnosticReason}";
            return null;
        }
        // Each explicit attempt freezes fresh fingerprints in one GET; roster changes never rearm it.
        if (!manualAttempt && elapsed < TimeSpan.FromSeconds(2)) { DiagnosticReason = "進島未滿 2 秒，等待初始 FATE 表載入。"; return null; }
        if (datacenter is 0 or > int.MaxValue) { DiagnosticReason = "目前世界的資料中心 ID 未就緒或無效。"; return null; }
        var candidates = fates.Where(f => f.Id != 0 && f.StartUnix is > 0 and <= int.MaxValue && f.StartUnix <= now.ToUnixTimeSeconds())
            .Distinct().OrderBy(f => f.StartUnix).ThenBy(f => f.Id).Take(16).ToArray();
        var oldest = candidates.FirstOrDefault();
        if (oldest.Id == 0) { DiagnosticReason = "沒有可用於副本指紋的 FATE：需有效 ID、位置及開始時間。"; return null; }
        Status = PotEntryTimeStatus.Fetching;
        RequestCount++;
        if (manualAttempt) ManualRequestCount++;
        DiagnosticReason = $"{(manualAttempt ? "手動" : "進島")}查詢已送出，等待 HTTP 結果；不自動重試。";
        QueryFate = oldest;
        RequestedAt = now;
        return LastRequest = new(generation, territory, instance, datacenter, Fingerprint(datacenter, oldest))
        {
            AdditionalKeys = Array.AsReadOnly(candidates.Skip(1).Select(f => Fingerprint(datacenter, f)).ToArray()),
        };
    }

    public bool Complete(PotTimeRequest request, PotTimeFetchResult result, PotFateTracker tracker, DateTimeOffset now)
    {
        if (request.Generation != generation || request.Territory != territory || Status != PotEntryTimeStatus.Fetching) return false;
        LastResult = result; CompletedAt = now;
        if (tracker.HasLocalAnchor) { Status = PotEntryTimeStatus.Local; DiagnosticReason = "等待 HTTP 期間取得本機紀錄，忽略共享資料。"; return false; }
        if (result.Outcome != PotTimeFetchOutcome.Found)
        {
            Status = result.Outcome switch
            {
                PotTimeFetchOutcome.NotFound => PotEntryTimeStatus.NotFound,
                PotTimeFetchOutcome.Invalid => PotEntryTimeStatus.Invalid,
                _ => PotEntryTimeStatus.Failed,
            };
            DiagnosticReason = result.Diagnostic?.Detail ?? Detail;
            return false;
        }
        var value = result.Value;
        var rejection = value is null ? "回應缺少魔法罐時間。" :
            !request.Matches(value.InstanceKey) ? "回應副本雜湊與本次請求不符。" :
            value.Territory != request.Territory ? $"回應島嶼不符：{value.Territory}，本次 {request.Territory}。" :
            value.Datacenter != request.Datacenter ? $"回應資料中心不符：{value.Datacenter}，本次 {request.Datacenter}。" :
            tracker.SharedTimeRejection(request.Territory, request.Instance, value.FateId, value.SpawnUnix, now);
        if (rejection is not null) { Status = PotEntryTimeStatus.Invalid; DiagnosticReason = rejection; return false; }
        if (!tracker.TrySeedSharedTime(request.Territory, request.Instance, value!.FateId, value.SpawnUnix, now))
        { Status = PotEntryTimeStatus.Invalid; DiagnosticReason = "本場計時狀態已改變，未匯入共享時間。"; return false; }
        Status = PotEntryTimeStatus.Imported;
        DiagnosticReason = "回應通過副本、島嶼、資料中心與時間檢查；已改由本機倒數。";
        return true;
    }

    public static string Fingerprint(uint datacenter, PotFingerprintFate fate)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, datacenter);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], fate.Id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], checked((int)fate.StartUnix));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
