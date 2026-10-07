using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace CrescentCompass.Core;

/// <summary>Read-only OccultOverlay / Eureka Linker shared backend. No uploads, redirects, cookies, installation ID, or retries.</summary>
public sealed class PotSharedTimeClient(HttpClient http)
{
    public const string Endpoint = "https://infi.ovh/api/OccultTrackerV3";
    // Public anonymous client key published by both upstream clients, not a player/account credential.
    private const string AnonymousKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiJ9.Ur6wgi_rD4dr3uLLvbLoaEvfLCu4QFWdrF-uHRtbl_s";
    public const int MaximumResponseBytes = 65536;

    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    }) { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<PotTimeFetchResult> FetchAsync(PotTimeRequest entry, CancellationToken cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        int? status = null;
        var bytesRead = 0;
        PotTimeFetchResult Result(PotTimeFetchOutcome outcome, string code, string detail, PotTimeResponse? value = null) =>
            new(outcome, value, new(status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, bytesRead, code, detail));
        var keys = entry.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length is < 1 or > 16 || keys.Any(key => key.Length != 64 || !key.All(Uri.IsHexDigit)))
            return Result(PotTimeFetchOutcome.Invalid, "invalid-key", "副本指紋格式或數量無效，未發出請求。");
        if (!SpotCatalog.IsSupported(entry.Territory) || entry.Datacenter is 0 or > int.MaxValue)
            return Result(PotTimeFetchOutcome.Invalid, "invalid-scope", "島嶼或資料中心無效，未發出請求。");
        var callerCancellation = cancellation;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        cancellation = timeout.Token;
        try
        {
            var fingerprints = string.Join(",", keys.Select(key => key.ToUpperInvariant()));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{Endpoint}?last_fate=in.({fingerprints})&territory=eq.{entry.Territory}&datacenter=eq.{entry.Datacenter}" +
                "&select=territory,datacenter,last_fate,pot_history&limit=2");
            request.Headers.Add("apikey", AnonymousKey);
            request.Headers.Authorization = new("Bearer", AnonymousKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return Result(PotTimeFetchOutcome.Failed, "http-status", $"HTTP {status}：服務未成功回應，不自動重試。");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                return Result(PotTimeFetchOutcome.Invalid, "body-too-large", "回應宣告超過 65536 bytes，未讀取內容。");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[1024];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                bytesRead += count;
                if (body.Length + count > MaximumResponseBytes)
                    return Result(PotTimeFetchOutcome.Invalid, "body-too-large", "回應串流超過 65536 bytes，已停止讀取。");
                body.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return Result(PotTimeFetchOutcome.Invalid, "invalid-envelope", "OccultTrackerV3 回應不是陣列。");
            if (root.GetArrayLength() == 0)
                return Result(PotTimeFetchOutcome.NotFound, "not-found", "OccultTrackerV3 回傳空陣列：目前副本指紋沒有共享紀錄；不是連線失敗。");
            if (root.GetArrayLength() != 1)
                return Result(PotTimeFetchOutcome.Invalid, "ambiguous-instance", "副本指紋對應多筆追蹤紀錄，無法唯一確認場次，不匯入時間。");
            var row = root[0];
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("last_fate", out var key) || key.ValueKind != JsonValueKind.String ||
                !row.TryGetProperty("territory", out var territory) || !territory.TryGetUInt16(out var territoryId) ||
                !row.TryGetProperty("datacenter", out var dc) || !dc.TryGetUInt32(out var datacenterId))
                return Result(PotTimeFetchOutcome.Invalid, "invalid-fields", "追蹤紀錄必要欄位缺少或型別／數值範圍不符。");
            if (!entry.Matches(key.GetString()!) || territoryId != entry.Territory || datacenterId != entry.Datacenter)
                return Result(PotTimeFetchOutcome.Invalid, "instance-mismatch", "回應副本指紋、島嶼或資料中心不符，不匯入時間。");
            if (!row.TryGetProperty("pot_history", out var history) || history.ValueKind == JsonValueKind.Null ||
                history.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(history.GetString()))
                return Result(PotTimeFetchOutcome.NotFound, "no-pot-history", "已找到副本，但尚無魔法罐觀測紀錄。");
            using var nested = history.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(history.GetString()!, new JsonDocumentOptions { MaxDepth = 8 }) : null;
            var pots = nested?.RootElement ?? history;
            if (pots.ValueKind != JsonValueKind.Array || pots.GetArrayLength() > 64)
                return Result(PotTimeFetchOutcome.Invalid, "invalid-history", "魔法罐紀錄不是陣列或筆數異常。");
            PotTimeResponse? latest = null;
            var ambiguous = false;
            foreach (var pot in pots.EnumerateArray())
            {
                if (pot.ValueKind != JsonValueKind.Object || !pot.TryGetProperty("fate_id", out var fate) ||
                    !fate.TryGetUInt16(out var fateId) || !pot.TryGetProperty("spawn_time", out var spawn) || !spawn.TryGetInt64(out var spawnUnix))
                    return Result(PotTimeFetchOutcome.Invalid, "invalid-pot", "魔法罐 ID 或開始時間缺少或無效。");
                if (!(territoryId == 1252 ? fateId is 1976 or 1977 : fateId is 2072 or 2073) || spawnUnix <= 0) continue;
                if (latest is null || spawnUnix > latest.SpawnUnix)
                {
                    latest = new(key.GetString()!, territoryId, datacenterId, fateId, spawnUnix);
                    ambiguous = false;
                }
                else if (spawnUnix == latest.SpawnUnix && fateId != latest.FateId) ambiguous = true;
            }
            if (ambiguous)
                return Result(PotTimeFetchOutcome.Invalid, "ambiguous-pot", "兩個魔法罐的最新開始時間相同，無法確認下次交替位置。");
            return latest is null ? Result(PotTimeFetchOutcome.NotFound, "no-pot-spawn", "已找到副本，但沒有本島魔法罐的有效開始時間。") :
                Result(PotTimeFetchOutcome.Found, "found", "已找到副本及最近魔法罐時間，待檢查時間是否仍有效。", latest);
        }
        catch (OperationCanceledException)
        { return Result(PotTimeFetchOutcome.Failed, callerCancellation.IsCancellationRequested ? "cancelled" : "timeout",
            callerCancellation.IsCancellationRequested ? "本機已取消請求。" : "請求超過 10 秒，已停止等待；不自動重試。"); }
        catch (HttpRequestException error)
        { return Result(PotTimeFetchOutcome.Failed, $"transport-{error.HttpRequestError}", $"網路連線失敗：{error.HttpRequestError}；不自動重試。"); }
        catch (IOException) { return Result(PotTimeFetchOutcome.Failed, "body-read", "回應串流讀取失敗；不自動重試。"); }
        catch (JsonException) { return Result(PotTimeFetchOutcome.Invalid, "invalid-json", "回應不是有效 JSON，或超過容許的巢狀深度。"); }
        catch (InvalidOperationException) { return Result(PotTimeFetchOutcome.Invalid, "invalid-fields", "JSON 欄位型別不符或回應內容不可讀。"); }
    }
}
