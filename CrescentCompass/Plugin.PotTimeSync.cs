using System.Net.Http;
using CrescentCompass.Core;
using CrescentCompass.Ui;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly PotEntryTimeSync potEntryTimeSync = new();
    private readonly HttpClient potTimeHttp = PotSharedTimeClient.CreateHttpClient();
    private Task<PotTimeFetchResult>? potTimeTask;
    private PotTimeRequest? potTimeRequest;
    private CancellationTokenSource? potTimeCancellation;
    private DateTimeOffset? potTimeLastScan;
    private int potTimeTableRows, potTimeValidRows, potTimePositionRows, potTimeEpochRows;
    private uint potTimeDatacenter;
    private string potTimeScanIssue = "尚未掃描 FATE 表。";
    private string potTimeWorldIssue = "尚未讀取目前世界資料。";
    internal string PotTimeSyncDetail => !Config.FetchPotTimeOnEntry ? "進島共享時間查詢已關閉。" :
        potEntryTimeSync.Status == PotEntryTimeStatus.Disabled ? "下次進島時查詢，本次維持本機計時。" : potEntryTimeSync.Detail;

    private void ResetPotEntryTimeSync(ushort territory)
    {
        CancelPotTimeQuery();
        potEntryTimeSync.Enter(territory, Config.FetchPotTimeOnEntry, DateTimeOffset.UtcNow);
        potTimeLastScan = null;
        potTimeTableRows = potTimeValidRows = potTimePositionRows = potTimeEpochRows = 0;
        potTimeDatacenter = 0;
        potTimeScanIssue = "尚未掃描 FATE 表。";
        potTimeWorldIssue = "尚未讀取目前世界資料。";
    }

    private void CancelPotTimeQuery()
    {
        potTimeCancellation?.Cancel();
        potTimeCancellation?.Dispose();
        potTimeCancellation = null;
        potTimeTask = null;
        potTimeRequest = null;
    }

    private void UpdatePotEntryTimeSync(IReadOnlyList<PotFingerprintFate> fates, int tableRows, int validRows, DateTimeOffset now)
    {
        potTimeLastScan = now; potTimeScanIssue = "掃描成功";
        potTimeTableRows = tableRows; potTimeValidRows = validRows; potTimePositionRows = fates.Count;
        potTimeEpochRows = fates.Count(f => f.Id != 0 && f.StartUnix is > 0 and <= int.MaxValue && f.StartUnix <= now.ToUnixTimeSeconds());
        if (potTimeTask is { IsCompleted: true } completed && potTimeRequest is { } request)
        {
            var result = completed.IsCompletedSuccessfully ? completed.Result : new(PotTimeFetchOutcome.Failed, Diagnostic:
                new(null, 0, 0, "unexpected-task", $"查詢工作非正常結束：{completed.Exception?.GetBaseException().GetType().Name ?? "Cancelled"}；不自動重試。"));
            CancelPotTimeQuery();
            potEntryTimeSync.Complete(request, result, PotFates, now);
            LogPotTimeResult();
        }
        if (!Config.FetchPotTimeOnEntry || !PlayerState.IsLoaded || PlayerState.ContentId == 0) return;
        uint datacenter = 0;
        try
        {
            datacenter = Objects.LocalPlayer?.CurrentWorld.Value.DataCenter.RowId ?? 0;
            potTimeWorldIssue = datacenter == 0 ? "目前世界沒有有效的資料中心 ID。" : "目前世界資料已就緒";
        }
        catch (Exception error) { potTimeWorldIssue = $"目前世界資料讀取失敗：{error.GetType().Name}"; }
        potTimeDatacenter = datacenter;
        var previous = potEntryTimeSync.Status;
        if (potEntryTimeSync.TryStart(datacenter, Client.Instance, fates, PotFates.HasLocalAnchor, now) is not { } next)
        {
            if (previous != potEntryTimeSync.Status) LogPotTimeResult();
            return;
        }
        potTimeRequest = next;
        potTimeCancellation = new();
        Log.Information($"[PotTime] GET; request={potEntryTimeSync.RequestCount}; manual={potEntryTimeSync.ManualRequestCount}; territory={next.Territory}; dc={next.Datacenter}; fate={potEntryTimeSync.QueryFate?.Id}; start={potEntryTimeSync.QueryFate?.StartUnix}; uploads=disabled");
        potTimeTask = new PotSharedTimeClient(potTimeHttp).FetchAsync(next, potTimeCancellation.Token);
    }

    private void LogPotTimeResult()
    {
        var trace = potEntryTimeSync.LastResult?.Diagnostic;
        Log.Information($"[PotTime] state={potEntryTimeSync.Status}; http={trace?.StatusCode}; code={trace?.Code}; elapsed-ms={trace?.ElapsedMilliseconds}; reason={potEntryTimeSync.DiagnosticReason}");
    }

    private string? PotTimeRetryBlockedReason(DateTimeOffset now) =>
        !Config.FetchPotTimeOnEntry ? "查詢開關已關閉。" : !Client.IsLoggedIn ? "尚未登入。" : IsLoading ? "傳送／讀取中。" :
        !Active ? "不在有效島嶼狀態或場次暫停。" : !PlayerState.IsLoaded || PlayerState.ContentId == 0 ? "角色資料尚未就緒。" :
        potEntryTimeSync.RetryBlockedReason(PotFates.HasLocalAnchor, now);

    internal void RetryPotTime() => _ = Framework.RunOnFrameworkThread(() =>
    {
        var now = DateTimeOffset.UtcNow;
        if (PotTimeRetryBlockedReason(now) is not null || !potEntryTimeSync.TryRetry(PotFates.HasLocalAnchor, now)) return;
        // The next successful FATE scan supplies fresh fingerprints; never reuse the failed request.
        LogPotTimeResult();
    });

    internal CompassPotTimeDebug PotTimeDebugState()
    {
        var now = DateTimeOffset.UtcNow;
        var sync = potEntryTimeSync;
        var request = sync.LastRequest;
        var result = sync.LastResult;
        var trace = result?.Diagnostic;
        static string Time(DateTimeOffset? value) => value is { } at ? $"{at:yyyy-MM-dd HH:mm:ss} UTC" : "--";
        var gate = !Config.FetchPotTimeOnEntry ? "查詢開關關閉" : !Client.IsLoggedIn ? "尚未登入" : IsLoading ? "傳送／讀取中" :
            !Active ? "不在有效島嶼狀態或場次暫停" : !PlayerState.IsLoaded || PlayerState.ContentId == 0 ? "角色資料尚未就緒" :
            potTimeScanIssue != "掃描成功" ? potTimeScanIssue : "無；僅進島自動查詢或手動重試";
        var retryBlocked = PotTimeRetryBlockedReason(now);
        var retryDetail = retryBlocked ?? "手動查詢一次最新共享時間；每次請求間隔至少 10 秒，不上傳本機資料。";
        var timer = PotFates.Snapshot(now);
        List<CompassPotTimeDebugRow> rows =
        [
            new("診斷版本", $"{typeof(Plugin).Assembly.GetName().Version} / pot-debug-3"),
            new("共享來源", "OccultOverlay / Eureka Linker"),
            new("查詢狀態", $"{sync.Status} · {PotTimeSyncDetail}"),
            new("目前阻擋條件", gate),
            new("HTTP / 結果", trace is null ? request is null ? "未送出請求" : "請求已送出，尚無已處理回應" :
                $"{trace.StatusCode?.ToString() ?? "無狀態碼"} / {result!.Outcome} / {trace.Code}"),
            new("耗時 / 讀取量", trace is null ? "--" : $"{trace.ElapsedMilliseconds} ms / {trace.BytesRead} bytes"),
            new("本場請求次數", $"{sync.RequestCount}（自動 {sync.RequestCount - sync.ManualRequestCount} / 手動 {sync.ManualRequestCount}）；不上傳、不自動重試"),
            new("手動重試", retryDetail),
            new("場次建立 (UTC)", Time(sync.EnteredAt)),
            new("送出 / 完成 (UTC)", $"{Time(sync.RequestedAt)}\n{Time(sync.CompletedAt)}"),
            new("目前區域 / 分流", $"{Client.TerritoryType} / {Client.Instance}"),
            new("目前資料中心", $"{potTimeDatacenter} · {potTimeWorldIssue}"),
            new("FATE 掃描 (UTC)", $"{Time(potTimeLastScan)} · {potTimeScanIssue}"),
            new("FATE 篩選數", $"表內 {potTimeTableRows} → 有效同區 {potTimeValidRows} → 進行／準備且位置有效 {potTimePositionRows} → 時間戳有效 {potTimeEpochRows}"),
            new("請求使用的場次", request is null ? "--" : $"區域 {request.Territory} / 分流 {request.Instance} / DC {request.Datacenter}"),
            new("請求來源 FATE", sync.QueryFate is not { } fate ? "--" : $"ID {fate.Id} / StartUnix {fate.StartUnix}"),
            new("單次比對指紋數", request is null ? "--" : $"{request.Keys.Count()} 個有效 FATE；共用一個 GET"),
            new("請求指紋 SHA256", request is null ? "--" : string.Join("\n", request.Keys.Select(key => key[..32] + "\n" + key[32..]))),
            new("回應計時資料", result?.Value is not { } value ? "--" :
                $"區域 {value.Territory} / DC {value.Datacenter} / FATE {value.FateId}\nStartUnix {value.SpawnUnix}\n指紋符合：{request?.Matches(value.InstanceKey) == true}"),
            new("目前倒數來源", PotFates.HasLocalAnchor ? "本機觀測" : timer.IsSharedEstimate ? "進島共享時間" : "未知"),
            new("預估下次 (UTC)", Time(timer.ExpectedAt)),
            new("服務", PotSharedTimeClient.Endpoint),
        ];
        return new(sync.DiagnosticReason, rows, retryBlocked is null, retryDetail);
    }

    internal void SetFetchPotTimeOnEntry(bool enabled)
    {
        Config.FetchPotTimeOnEntry = enabled;
        PluginInterface.SavePluginConfig(Config);
        if (enabled) return; // Enabling takes effect on the next entry, never starts a second query here.
        CancelPotTimeQuery();
        potEntryTimeSync.Disable();
    }
}
