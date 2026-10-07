using System.Net;
using System.Text;
using System.Text.Json;
using CrescentCompass.Core;

internal static class PotEntryTimeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var now = start.AddSeconds(3);
        var sync = new PotEntryTimeSync();
        var tracker = new PotFateTracker();
        PotFingerprintFate[] fates = [new(1964, start.ToUnixTimeSeconds() + 1), new(1963, start.ToUnixTimeSeconds())];
        void Enter(ushort territory = 1252, uint instance = 1, bool enabled = true)
        {
            tracker.Reset(); tracker.Update(territory, instance, [], start, true);
            sync.Enter(territory, enabled, start);
        }
        PotTimeRequest Request(uint instance = 1) => sync.TryStart(3, instance, fates, tracker.HasLocalAnchor, now)
            ?? throw new InvalidOperationException("Expected one entry request.");
        PotTimeFetchResult Hit(PotTimeRequest request, ushort id = 1976, long? epoch = null) =>
            new(PotTimeFetchOutcome.Found, new(request.InstanceKey, request.Territory, request.Datacenter, id, epoch ?? start.AddMinutes(-20).ToUnixTimeSeconds()));
        PotFateObservation Local(ushort id = 1976, long? epoch = null) =>
            new(id, 1252, PotFatePhase.Running, epoch ?? now.ToUnixTimeSeconds(), 900, 0, new(200, 111, -215));

        check(PotEntryTimeSync.Fingerprint(3, new(1963, 1_800_000_000)) == "ECF35A90AB9416B3D9C115C93D9893F60CCDA19D8D877CC26B8B3FC3D97FD356",
            "Eureka Linker fingerprint matches its three little-endian Int32 values and uppercase SHA256");
        Enter();
        check(sync.TryStart(3, 1, fates, false, start) is null, "Wait for the entry FATE table to populate before the single query");
        check(sync.TryStart(0, 1, fates, false, now) is null && sync.Status == PotEntryTimeStatus.Waiting, "Missing datacenter does not send a query");
        check(sync.LastRequest is null && sync.DiagnosticReason.Contains("資料中心"), "Diagnostic distinguishes missing datacenter before a request");
        check(sync.TryStart(3, 1, [], false, now) is null, "No FATE fingerprint means no network request");
        check(sync.DiagnosticReason.Contains("FATE"), "Diagnostic distinguishes an unavailable fingerprint");
        check(sync.TryStart(3, 1, [new(0, 1), new(1, 0), new(2, long.MaxValue), new(3, now.ToUnixTimeSeconds() + 1)], false, now) is null,
            "Invalid, missing and future FATE epochs cannot identify an instance");
        var request = Request();
        check(sync.LastRequest == request && sync.QueryFate == fates[1] && sync.EnteredAt == start && sync.RequestedAt == now,
            "Diagnostic freezes the source FATE, fingerprint and send time actually used");
        check(request.InstanceKey == PotEntryTimeSync.Fingerprint(3, fates[1]) && request.Territory == 1252 && request.Instance == 1,
            "Keep the oldest FATE as primary and retain local instance identity without sending it");
        check(sync.TryStart(3, 1, fates, false, now.AddSeconds(1)) is null, "In-flight query consumes the only attempt");
        check(sync.Complete(request, Hit(request), tracker, now), "A matching fresh response seeds this island's timer");
        check(sync.LastResult == Hit(request) && sync.CompletedAt == now && sync.DiagnosticReason.Contains("通過"), "Accepted response retains its outcome and application reason");
        var snapshot = tracker.Snapshot(now);
        check(snapshot.IsSharedEstimate && snapshot.Next?.Id == 1977 && snapshot.ExpectedAt == start.AddMinutes(10) && snapshot.Active.Count == 0,
            "Shared seed provides remaining time and next flag without inventing a live pot");
        check(!tracker.HasLocalAnchor && tracker.Update(1252, 1, [], now, true).Count == 0, "Import is not a local observation or spawn notification");
        check(!sync.Complete(request, Hit(request), tracker, now), "A completed response is not applied twice");
        check(!sync.Complete(request, new(PotTimeFetchOutcome.Failed), tracker, now.AddSeconds(10)) && sync.LastResult == Hit(request) && sync.CompletedAt == now,
            "Late duplicate responses cannot overwrite the displayed diagnostic");
        foreach (var minute in new[] { 1, 5, 30, 120 })
        {
            check(sync.TryStart(3, 1, [new(1980, start.AddMinutes(minute).ToUnixTimeSeconds())], false, start.AddMinutes(minute)) is null,
                "FATE roster churn and elapsed cycles do not trigger another network query");
            check(tracker.Snapshot(start.AddMinutes(minute)).ExpectedAt == start.AddMinutes(10), "Local clock retains the original deadline instead of fabricating later spawns");
        }
        tracker.Update(1252, 1, [], start.AddMinutes(5), true);
        check(tracker.TakeUpcomingReminder(start.AddMinutes(5), true)?.Definition.Id == 1977, "Imported remaining time supports the existing five-minute reminder");
        check(tracker.TakeUpcomingReminder(start.AddMinutes(5), true) is null, "Shared seed reminder is emitted once");
        check(tracker.Update(1252, 1, [Local(1977, start.AddMinutes(10).ToUnixTimeSeconds())], start.AddMinutes(10), true).Count == 1,
            "An actual subsequent local spawn still notifies normally");
        check(tracker.Snapshot(start.AddMinutes(10)) is { IsSharedEstimate: false, Next.Id: 1976 } &&
            tracker.Snapshot(start.AddMinutes(10)).ExpectedAt == start.AddMinutes(40), "Later local observation takes over all future timing");
        check(sync.TryStart(3, 1, fates, true, start.AddMinutes(10)) is null, "Local correction never re-queries shared time");

        Enter(); request = Request();
        var sharedEpoch = start.AddMinutes(-25).ToUnixTimeSeconds();
        sync.Complete(request, Hit(request, epoch: sharedEpoch), tracker, now);
        tracker.Update(1252, 1, [], now, true);
        check(tracker.TakeUpcomingReminder(now, true) is not null, "Entry inside the reminder window can remind once");
        tracker.Update(1252, 1, [Local(epoch: sharedEpoch)], now, true);
        check(!tracker.Snapshot(now).IsSharedEstimate && tracker.TakeUpcomingReminder(now, true) is null,
            "Confirming the same shared occurrence locally does not duplicate its reminder");

        foreach (var outcome in new[] { PotTimeFetchOutcome.NotFound, PotTimeFetchOutcome.Failed, PotTimeFetchOutcome.Invalid })
        {
            Enter(); request = Request();
            check(!sync.Complete(request, new(outcome), tracker, now) && tracker.Snapshot(now).ExpectedAt is null,
                "Missing, failed or invalid query leaves an unknown timer");
            check(sync.TryStart(3, 1, fates, false, now.AddSeconds(20)) is null, "Failed queries never retry even after the server's usual backoff period");
        }
        foreach (var mutate in new Func<PotTimeResponse, PotTimeResponse>[]
        {
            r => r with { InstanceKey = new string('A', 64) }, r => r with { Territory = 1346 }, r => r with { Datacenter = 4 },
            r => r with { FateId = 2072 }, r => r with { FateId = 0 }, r => r with { FateId = 1963 },
            r => r with { SpawnUnix = 0 }, r => r with { SpawnUnix = long.MaxValue }, r => r with { SpawnUnix = long.MinValue },
            r => r with { SpawnUnix = now.ToUnixTimeSeconds() + 1 }, r => r with { SpawnUnix = now.AddMinutes(-30).ToUnixTimeSeconds() },
            r => r with { SpawnUnix = now.AddMinutes(-45).ToUnixTimeSeconds() },
        })
        {
            Enter(); request = Request();
            check(!sync.Complete(request, new(PotTimeFetchOutcome.Found, mutate(Hit(request).Value!)), tracker, now) &&
                tracker.Snapshot(now).ExpectedAt is null && sync.Status == PotEntryTimeStatus.Invalid, "Reject mismatched, non-pot, future, corrupt or overdue remote data");
            check(sync.TryStart(3, 1, fates, false, now) is null, "Rejected data cannot replenish the one-shot attempt");
        }
        Enter(); request = Request();
        tracker.Update(1252, 1, [Local()], now, true);
        check(!sync.Complete(request, Hit(request), tracker, now) && sync.Status == PotEntryTimeStatus.Local && !tracker.Snapshot(now).IsSharedEstimate,
            "A local spawn arriving while HTTP is pending wins over the shared response");
        Enter(); tracker.Update(1252, 1, [Local()], now, true);
        check(sync.TryStart(3, 1, fates, true, now) is null && sync.Status == PotEntryTimeStatus.Local, "Already known local time skips the network entirely");
        Enter(); request = Request(); Enter(instance: 2);
        var second = Request(2);
        check(!sync.Complete(request, Hit(request), tracker, now), "A previous instance's pending response cannot seed the new instance");
        check(sync.Complete(second, Hit(second), tracker, now), "New instance gets its own single query");
        Enter(); request = Request(); Enter();
        check(!sync.Complete(request, Hit(request), tracker, now), "Re-entry with recycled territory and instance IDs rejects the old generation");
        check(sync.LastRequest is null && sync.LastResult is null && sync.QueryFate is null && sync.CompletedAt is null,
            "A new entry clears diagnostics and stale completions cannot repopulate them");
        Enter(); request = Request(); tracker.Update(1252, 2, [], now, true);
        check(!sync.Complete(request, Hit(request), tracker, now), "Tracker also validates instance identity at application time");
        Enter(); request = Request(); sync.Disable();
        check(!sync.Complete(request, Hit(request), tracker, now) && sync.TryStart(3, 1, fates, false, now) is null,
            "Disabling cancels eligibility and ignores an in-flight result without resetting the attempt");
        Enter(enabled: false);
        check(sync.TryStart(3, 1, fates, false, now) is null && sync.Status == PotEntryTimeStatus.Disabled, "Disabled entries make no request");
        Enter(territory: 1);
        check(sync.TryStart(3, 1, fates, false, now) is null && sync.Status == PotEntryTimeStatus.Outside, "Off-island data cannot initiate a query");
        Enter();
        sync.TryStart(0, 1, fates, false, now);
        check(sync.TryStart(3, 1, fates, false, start.AddMinutes(1)) is null && sync.Status == PotEntryTimeStatus.Unavailable,
            "Missing entry data does not cause a surprise query much later in the session");
        check(sync.DiagnosticReason.Contains("60 秒") && sync.DiagnosticReason.Contains("資料中心"), "Entry deadline retains the last missing prerequisite");
        Enter();
        check(sync.TryStart(3, 1, fates, false, start.AddSeconds(-1)) is null, "Clock rollback cannot query against an invalid entry window");
        Enter(1346); request = Request();
        check(sync.Complete(request, Hit(request, 2072), tracker, now) && tracker.Snapshot(now).Next?.Id == 2073, "North Horn uses its own magic pots");
        tracker.Reset(); check(!tracker.Snapshot(now).IsSharedEstimate && tracker.Snapshot(now).Next is null, "Logout clears shared and local anchors");

        var lifecycle = new PatrolContext();
        lifecycle.Update(true, false, true, 1252, 1, 0);
        Enter(); request = Request(); sync.Complete(request, Hit(request), tracker, now);
        lifecycle.Update(true, true, false, 1252, 1, 1);
        lifecycle.Update(true, false, true, 1252, 1, 2);
        check(lifecycle.Update(true, false, true, 1252, 1, 1002) == PatrolContextChange.Resumed &&
            sync.TryStart(3, 1, fates, false, now) is null, "Same-island teleport resumes without resetting the consumed query");

        foreach (var (change, reason) in new (Func<PotTimeResponse, PotTimeResponse>, string)[]
        {
            (r => r with { InstanceKey = "wrong" }, "雜湊"), (r => r with { Territory = 1346 }, "島嶼不符"),
            (r => r with { Datacenter = 999 }, "資料中心不符"), (r => r with { FateId = 1963 }, "不是本島"),
            (r => r with { SpawnUnix = 0 }, "大於零"), (r => r with { SpawnUnix = long.MaxValue }, "未來"),
            (r => r with { SpawnUnix = now.AddMinutes(-30).ToUnixTimeSeconds() }, "30 分鐘"),
        })
        {
            Enter(); request = Request();
            sync.Complete(request, new(PotTimeFetchOutcome.Found, change(Hit(request).Value!)), tracker, now);
            check(sync.Status == PotEntryTimeStatus.Invalid && sync.DiagnosticReason.Contains(reason), "Data rejection identifies the precise reason: " + reason);
        }
        Enter(); request = Request();
        var missing = new PotTimeFetchResult(PotTimeFetchOutcome.NotFound, Diagnostic: new(200, 85, 2, "not-found", "empty array"));
        sync.Complete(request, missing, tracker, now);
        check(sync.LastResult == missing && sync.DiagnosticReason == "empty array", "Coordinator preserves HTTP details without replacing no-data with a network error");
        sync.Disable();
        check(sync.LastResult == missing && sync.LastRequest == request, "Disabling retains the completed diagnostic without rearming the query");

        Enter(); request = Request();
        check(request.Keys.Count() == 2 && request.Matches(PotEntryTimeSync.Fingerprint(3, fates[0])),
            "All valid active FATE fingerprints share the single immutable entry request");
        var additional = Hit(request).Value! with { InstanceKey = request.AdditionalKeys[0].ToLowerInvariant() };
        check(sync.Complete(request, new(PotTimeFetchOutcome.Found, additional), tracker, now),
            "A response matching another frozen active FATE is accepted");
        Enter();
        request = sync.TryStart(3, 1, Enumerable.Range(1, 30).Select(i => new PotFingerprintFate((ushort)i, start.ToUnixTimeSeconds())), false, now)!;
        check(request.Keys.Count() == 16, "Fingerprint count and URI length are bounded");
        Enter(); request = Request();
        await CheckHttp(check, request, Hit(request));
        await CheckManualRetry(check);
    }

    private static async Task CheckManualRetry(Action<bool, string> check)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var sent = start.AddSeconds(3);
        var retryAt = sent + PotEntryTimeSync.RetryCooldown;
        var sync = new PotEntryTimeSync();
        var tracker = new PotFateTracker();
        PotFingerprintFate[] initial = [new(1963, start.ToUnixTimeSeconds())];
        PotFingerprintFate[] fresh = [new(1964, retryAt.ToUnixTimeSeconds())];
        void Enter(bool enabled = true, ushort territory = 1252)
        {
            tracker.Reset(); tracker.Update(territory, 1, [], start, false);
            sync.Enter(territory, enabled, start);
        }
        PotTimeRequest Start(DateTimeOffset now, PotFingerprintFate[] fates) => sync.TryStart(3, 1, fates, tracker.HasLocalAnchor, now)!;
        foreach (var outcome in new[] { PotTimeFetchOutcome.NotFound, PotTimeFetchOutcome.Failed, PotTimeFetchOutcome.Invalid })
        {
            Enter();
            check(!sync.TryRetry(false, sent), "Manual retry cannot duplicate the pending entry query");
            var first = Start(sent, initial);
            check(sync.RequestCount == 1 && sync.ManualRequestCount == 0 && !sync.TryRetry(false, retryAt), "In-flight entry GET blocks manual retry even after cooldown");
            sync.Complete(first, new(outcome), tracker, sent);
            check(!sync.TryRetry(false, sent) && !sync.TryRetry(false, retryAt.AddTicks(-1)) && !sync.TryRetry(false, start),
                "Manual retry respects cooldown boundary and clock rollback");
            check(sync.RetryBlockedReason(false, retryAt.AddSeconds(-1))?.Contains("1 秒") == true, "Retry diagnostic shows remaining cooldown");
            check(sync.TryStart(3, 1, fresh, false, retryAt) is null, "Failed attempt never retries automatically");
            check(sync.TryRetry(false, retryAt) && sync.EnteredAt == start && sync.RequestCount == 1 && sync.ManualRequestCount == 0,
                "Explicit retry rearms one attempt without resetting real entry time or counting an unsent request");
            check(sync.LastRequest is null && sync.LastResult is null && sync.RequestedAt is null && sync.CompletedAt is null && sync.QueryFate is null,
                "Retry clears previous HTTP traces before fresh request details appear");
            check(!sync.TryRetry(false, retryAt), "Double-click cannot queue another manual retry");
            var second = Start(retryAt, fresh);
            check(second is not null && second.Generation != first.Generation && second.InstanceKey == PotEntryTimeSync.Fingerprint(3, fresh[0]) &&
                second.InstanceKey != first.InstanceKey && sync.RequestCount == 2 && sync.ManualRequestCount == 1,
                "Manual request freezes fresh FATE fingerprints and increments actual GET counters");
            check(!sync.Complete(first, new(PotTimeFetchOutcome.Failed), tracker, retryAt) && sync.LastResult is null,
                "Late result from previous attempt cannot complete manual query");
            check(!sync.TryRetry(false, retryAt.AddMinutes(1)), "In-flight manual query cannot be retried");
            sync.Complete(second!, new(outcome), tracker, retryAt);
            check(Start(retryAt.AddMinutes(1), fresh) is null && sync.RequestCount == 2, "Manual failure also remains terminal without another click");
        }
        Enter();
        check(Start(start.AddMinutes(1), initial) is null && sync.Status == PotEntryTimeStatus.Unavailable, "Initial fingerprint window expires");
        var late = start.AddMinutes(5);
        check(sync.TryRetry(false, late), "Explicit retry can reopen an expired initial data window");
        check(sync.TryStart(0, 1, [], false, late) is null && sync.RequestCount == 0, "Manual retry waits for data without inventing a request");
        check(sync.TryStart(0, 1, [], false, late.AddSeconds(60)) is null && sync.Status == PotEntryTimeStatus.Unavailable && sync.DiagnosticReason.Contains("手動重試後"),
            "Manual data wait has its own bounded 60-second deadline");
        check(sync.TryRetry(false, late.AddMinutes(1)) && Start(late.AddMinutes(1), fresh) is not null && sync.RequestCount == 1 && sync.ManualRequestCount == 1,
            "A later explicit attempt can use ready data without counting an automatic request");
        sync.Disable();
        check(!sync.TryRetry(false, late.AddMinutes(2)) && sync.RequestCount == 1, "Disabled query cannot be rearmed by retry");
        Enter(false); check(!sync.TryRetry(false, retryAt), "Disabled entry blocks retry");
        Enter(territory: 1); check(!sync.TryRetry(false, retryAt), "Off-island retry cannot send data");
        Enter();
        var request = Start(sent, initial);
        sync.Complete(request, new(PotTimeFetchOutcome.NotFound), tracker, sent);
        check(!sync.TryRetry(true, retryAt) && sync.Status == PotEntryTimeStatus.NotFound, "Local observation acquired after failure blocks retry without clearing diagnostics");

        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        using var http = new HttpClient(handler);
        var client = new PotSharedTimeClient(http);
        Enter(); request = Start(sent, initial);
        sync.Complete(request, await client.FetchAsync(request, default), tracker, sent);
        check(sync.TryRetry(false, retryAt), "Manual retry follows an actual mock HTTP miss");
        request = Start(retryAt, fresh);
        sync.Complete(request, await client.FetchAsync(request, default), tracker, retryAt);
        check(handler.Calls == 2 && handler.Method == HttpMethod.Get && handler.Content is null && handler.Uri!.Query.Contains(request.InstanceKey),
            "One automatic plus one manual request are exactly two read-only GETs using fresh fingerprints");
        check(sync.TryRetry(false, retryAt.AddSeconds(10)), "Each later retry requires another explicit click after cooldown");
        request = Start(retryAt.AddSeconds(10), fresh);
        var hit = new PotTimeFetchResult(PotTimeFetchOutcome.Found,
            new(request.InstanceKey, 1252, 3, 1976, start.AddMinutes(-20).ToUnixTimeSeconds()));
        check(sync.Complete(request, hit, tracker, retryAt.AddSeconds(10)) && sync.RequestCount == 3 && sync.ManualRequestCount == 2,
            "Manual success seeds the same validated local countdown");
        var expected = tracker.Snapshot(retryAt).ExpectedAt;
        check(!sync.TryRetry(false, retryAt.AddMinutes(1)) && tracker.Snapshot(retryAt).ExpectedAt == expected, "Imported countdown is preserved instead of being reset to force another query");
        Enter(); request = Start(sent, initial); sync.Complete(request, new(PotTimeFetchOutcome.NotFound), tracker, sent);
        sync.TryRetry(false, retryAt); request = Start(retryAt, fresh);
        tracker.Update(1252, 1, [new(1976, 1252, PotFatePhase.Running, retryAt.ToUnixTimeSeconds(), 900, 0, new(200, 111, -215))], retryAt, false);
        check(!sync.Complete(request, hit with { Value = hit.Value! with { InstanceKey = request.InstanceKey } }, tracker, retryAt) && sync.Status == PotEntryTimeStatus.Local,
            "Local observation during manual HTTP wins over shared time");
        Enter(); request = Start(sent, initial); sync.Complete(request, new(PotTimeFetchOutcome.NotFound), tracker, sent);
        sync.TryRetry(false, retryAt); request = Start(retryAt, fresh);
        sync.Disable();
        check(!sync.Complete(request, hit, tracker, retryAt) && sync.Status == PotEntryTimeStatus.Disabled, "Disabling pending manual retry rejects late response");
        Enter();
        check(sync.RequestCount == 0 && sync.ManualRequestCount == 0 && !sync.Complete(request, hit, tracker, retryAt), "Re-entry resets counts and rejects previous manual response");
    }

    private static async Task CheckHttp(Action<bool, string> check, PotTimeRequest request, PotTimeFetchResult hit)
    {
        string History(PotTimeResponse r) => JsonSerializer.Serialize(new[] { new { fate_id = r.FateId, spawn_time = r.SpawnUnix } });
        string Json(PotTimeResponse r, bool encoded = true, object? history = null) => JsonSerializer.Serialize(new[] {
            new { last_fate = r.InstanceKey, territory = r.Territory, datacenter = r.Datacenter,
                pot_history = history ?? (encoded ? (object)History(r) : JsonSerializer.Deserialize<JsonElement>(History(r))) } });
        var valid = Json(hit.Value!);
        foreach (var (body, outcome) in new[]
        {
            (valid, PotTimeFetchOutcome.Found), (Json(hit.Value!, false), PotTimeFetchOutcome.Found),
            ("[]", PotTimeFetchOutcome.NotFound), ("{}", PotTimeFetchOutcome.Invalid),
            ("null", PotTimeFetchOutcome.Invalid), ("<html>error</html>", PotTimeFetchOutcome.Invalid),
            ("[{\"found\":false}]", PotTimeFetchOutcome.Invalid),
            (valid.Replace("\"territory\":1252", "\"territory\":\"1252\""), PotTimeFetchOutcome.Invalid),
            (valid.Replace("\"datacenter\":3", "\"datacenter\":-1"), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value! with { Datacenter = 4 }), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value! with { Territory = 1346 }), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value! with { InstanceKey = new string('A', 64) }), PotTimeFetchOutcome.Invalid),
            ("[" + valid[1..^1] + "," + valid[1..^1] + "]", PotTimeFetchOutcome.Invalid),
            (Json(hit.Value!, history: "[]"), PotTimeFetchOutcome.NotFound),
            (Json(hit.Value!, history: ""), PotTimeFetchOutcome.NotFound),
            (Json(hit.Value!, history: "{}"), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value!, history: "[{}]"), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value!, history: "<html>"), PotTimeFetchOutcome.Invalid),
            (Json(hit.Value! with { FateId = 2072 }), PotTimeFetchOutcome.NotFound),
            (Json(hit.Value! with { SpawnUnix = -1 }), PotTimeFetchOutcome.NotFound),
            (new string('x', PotSharedTimeClient.MaximumResponseBytes + 1), PotTimeFetchOutcome.Invalid),
        })
        {
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            using var http = new HttpClient(handler);
            var result = await new PotSharedTimeClient(http).FetchAsync(request, default);
            check(result.Outcome == outcome, "Strict, bounded OccultTrackerV3 response: " + outcome);
            check(result.Diagnostic is { StatusCode: 200, ElapsedMilliseconds: >= 0 } trace && trace.Code.Length > 0 && trace.Detail.Length > 0,
                "HTTP diagnostic preserves status, duration and safe reason");
            check(result.Diagnostic!.BytesRead == (Encoding.UTF8.GetByteCount(body) > PotSharedTimeClient.MaximumResponseBytes ? 0 : Encoding.UTF8.GetByteCount(body)),
                "Diagnostic distinguishes unread oversized response from actual bytes read");
            if (outcome == PotTimeFetchOutcome.NotFound)
                check(result.Diagnostic.Code is "not-found" or "no-pot-history" or "no-pot-spawn", "No instance and no pot history have distinct diagnostic codes");
            check(handler.Calls == 1 && handler.Method == HttpMethod.Get && handler.Content is null &&
                handler.Uri?.GetLeftPart(UriPartial.Path) == PotSharedTimeClient.Endpoint && handler.HeaderCount == 2 &&
                handler.Uri.Query.Contains("territory=eq.1252") && handler.Uri.Query.Contains("datacenter=eq.3") &&
                handler.Uri.Query.Contains("limit=2") && request.Keys.All(key => handler.Uri.Query.Contains(key)) &&
                !handler.Uri.Query.Contains("instance") && handler.AnonymousHeadersMatch,
                "One GET contains scoped fingerprints and public anonymous headers, with no uploads or personal identity");
            if (outcome == PotTimeFetchOutcome.Found) check(result.Value == hit.Value, "Response preserves the full validation identity");
        }
        foreach (var entries in new[]
        {
            new[] { new { fate_id = 1977, spawn_time = hit.Value!.SpawnUnix - 1800 }, new { fate_id = 1976, spawn_time = hit.Value.SpawnUnix } },
            new[] { new { fate_id = 1976, spawn_time = hit.Value!.SpawnUnix }, new { fate_id = 1977, spawn_time = hit.Value.SpawnUnix - 1800 } },
        })
        {
            var body = Json(hit.Value!, history: JsonSerializer.Serialize(entries));
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            using var http = new HttpClient(handler);
            check((await new PotSharedTimeClient(http).FetchAsync(request, default)).Value == hit.Value, "Latest pot selection is independent of history order");
        }
        foreach (var (history, outcome) in new[]
        {
            (JsonSerializer.Serialize(new[] { new { fate_id = 1976, spawn_time = hit.Value!.SpawnUnix },
                new { fate_id = 1977, spawn_time = hit.Value.SpawnUnix } }), PotTimeFetchOutcome.Invalid),
            (JsonSerializer.Serialize(new[] { new { fate_id = 1976, spawn_time = hit.Value!.SpawnUnix - 60 },
                new { fate_id = 1977, spawn_time = hit.Value.SpawnUnix - 60 },
                new { fate_id = 1976, spawn_time = hit.Value.SpawnUnix } }), PotTimeFetchOutcome.Found),
            (JsonSerializer.Serialize(Enumerable.Range(0, 65).Select(_ => new { fate_id = 1976, spawn_time = -1 })), PotTimeFetchOutcome.Invalid),
            ("[{\"fate_id\":1976,\"spawn_time\":\"123\"}]", PotTimeFetchOutcome.Invalid),
            (new string('[', 10) + new string(']', 10), PotTimeFetchOutcome.Invalid),
        })
        {
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(hit.Value!, history: history)) }));
            using var http = new HttpClient(handler);
            check((await new PotSharedTimeClient(http).FetchAsync(request, default)).Outcome == outcome,
                "History guards handle ambiguous latest pots, older ties, count, types and nesting");
        }
        {
            var north = hit.Value! with { Territory = 1346, FateId = 2072 };
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(north)) }));
            using var http = new HttpClient(handler);
            check((await new PotSharedTimeClient(http).FetchAsync(request with { Territory = 1346 }, default)).Value == north,
                "HTTP parser handles North Horn's separate pot IDs");
        }
        foreach (var (entry, expected) in new[] { (request with { Territory = (ushort)1 }, "invalid-scope"),
            (request with { Datacenter = 0u }, "invalid-scope"), (request with { InstanceKey = "invalid&character=123" }, "invalid-key"),
            (request with { AdditionalKeys = Enumerable.Range(0, 17).Select(i => i.ToString("X64")).ToArray() }, "invalid-key") })
        {
            var handler = new Handler((_, _) => throw new Exception("Must not send"));
            using var http = new HttpClient(handler);
            check((await new PotSharedTimeClient(http).FetchAsync(entry, default)).Diagnostic?.Code == expected && handler.Calls == 0,
                "Invalid scope or fingerprints are rejected before network access");
        }
        foreach (var status in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.Unauthorized, HttpStatusCode.Redirect })
        {
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
            using var http = new HttpClient(handler);
            var result = await new PotSharedTimeClient(http).FetchAsync(request, default);
            check(result.Outcome == PotTimeFetchOutcome.Failed && handler.Calls == 1, "HTTP failure or redirect is terminal without retries");
            check(result.Diagnostic is { Code: "http-status", BytesRead: 0 } && result.Diagnostic.StatusCode == (int)status,
                "HTTP error preserves status without displaying arbitrary server content");
        }
        {
            var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); });
            using var http = new HttpClient(handler);
            using var cancellation = new CancellationTokenSource();
            var task = new PotSharedTimeClient(http).FetchAsync(request, cancellation.Token);
            cancellation.Cancel();
            var result = await task;
            check(result.Outcome == PotTimeFetchOutcome.Failed && handler.Calls == 1 && result.Diagnostic?.Code == "cancelled",
                "Island-exit cancellation stops the pending read without a retry or timeout mislabel");
        }
        foreach (var (exception, code) in new (Exception, string)[]
        {
            (new TaskCanceledException(), "timeout"),
            (new HttpRequestException(HttpRequestError.NameResolutionError, "private.example/character=secret"), "transport-NameResolutionError"),
            (new HttpRequestException(HttpRequestError.SecureConnectionError, "secret"), "transport-SecureConnectionError"),
            (new HttpRequestException("offline"), "transport-Unknown"),
            (new IOException("secret"), "body-read"),
        })
        {
            var handler = new Handler((_, _) => throw exception);
            using var http = new HttpClient(handler);
            var result = await new PotSharedTimeClient(http).FetchAsync(request, default);
            check(result.Diagnostic?.Code == code && handler.Calls == 1 && result.Outcome == PotTimeFetchOutcome.Failed, "Network failure is classified without retries: " + code);
            check(!result.Diagnostic!.Detail.Contains("secret"), "Exception text and private host details never appear in diagnostics");
        }
        {
            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(new string('x', PotSharedTimeClient.MaximumResponseBytes + 1)))) }));
            using var http = new HttpClient(handler);
            check((await new PotSharedTimeClient(http).FetchAsync(request, default)).Outcome == PotTimeFetchOutcome.Invalid,
                "Oversized unknown-length streaming responses are rejected");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public HttpContent? Content { get; private set; }
        public int HeaderCount { get; private set; }
        public bool AnonymousHeadersMatch { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Method = request.Method; Uri = request.RequestUri; Content = request.Content; HeaderCount = request.Headers.Count();
            AnonymousHeadersMatch = request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.GetValues("apikey").Single() == request.Headers.Authorization.Parameter;
            return respond(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
