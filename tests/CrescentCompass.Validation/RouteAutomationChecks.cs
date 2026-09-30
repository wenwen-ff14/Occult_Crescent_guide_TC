using System.Numerics;
using CrescentCompass.Core;

internal static class RouteAutomationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var head = new Spot("head", 1252, SpotKind.Silver, 1, new(10, 0, 0));
        var next = new Spot("next", 1252, SpotKind.Bronze, 2, new(80, 0, 0));
        var session = new SurveySession();
        var automation = new RouteAutomation();
        List<Spot> route = [];
        var timestamp = DateTimeOffset.UtcNow;
        void Start(params Spot[] spots) { session.Reset(1252, spots); route = [.. spots]; automation.Reset(); }
        RouteUpdate Scan(long ms, Vector3? player = null, Observation[]? objects = null, bool enabled = true, bool checkAbsence = true, PointDisplayMode mode = PointDisplayMode.Candidates, float radius = RouteAutomation.CheckRadius)
        {
            session.Observe(objects ?? [], timestamp.AddMilliseconds(ms));
            return automation.Update(session, route, objects ?? [], player ?? Vector3.Zero, ms, mode, enabled, checkAbsence, radius);
        }
        Observation Present(Spot spot, bool available = true, bool targetable = true) => new((ulong)spot.DataId, spot.DataId, spot.Kind, spot.Position, available, targetable);
        void MissingUntil(long end, Vector3? player = null, bool enabled = true, bool checkAbsence = true)
        { for (long t = 0; t <= end; t += 500) Scan(t, player, enabled: enabled, checkAbsence: checkAbsence); }

        Start(head, next);
        Scan(0, objects: [Present(head), Present(next)]);
        var update = Scan(500, objects: [Present(head, available: false), Present(next)]);
        check(update.Reason == RouteAdvanceReason.Opened && route.SequenceEqual([next]), "Opened route head removed before scheduling the next flag");
        check(automation.CanAttemptFlag(next.Id, 500), "Opened chest schedules actual next head");
        automation.RecordFlagAttempt(500, true);
        Scan(1000, objects: [Present(head, available: false), Present(next)]);
        check(!automation.CanAttemptFlag(next.Id, 1000), "Repeated opened observations do not spam successful flags");

        Start(head, next);
        MissingUntil(2500);
        check(route.Count == 2, "Empty candidate retained through near-pad grace period");
        update = Scan(3000);
        check(update.Reason == RouteAdvanceReason.Empty && route.SequenceEqual([next]) && session.Get(head.Id)!.Status == SpotStatus.Skipped, "Sustained near-pad absence skips without declaring collected");
        check(automation.CanAttemptFlag(next.Id, 3000), "Empty pad schedules next flag");
        check(!session.CanDisplay(head.Id, PointDisplayMode.Candidates), "Skipped candidate excluded from subsequent planning");
        Scan(3500, objects: [Present(head)]);
        check(session.CanDisplay(head.Id, PointDisplayMode.Candidates) && session.Get(head.Id)!.Status == SpotStatus.Visible, "Skipped target reappearing becomes eligible for replanning");

        Start(head, next);
        MissingUntil(5000, new(-100, 0, 0));
        check(route.Count == 2 && session.Get(head.Id)!.Status == SpotStatus.Candidate, "Far unloaded points are never auto-skipped");
        Start(head, next);
        MissingUntil(5000, new(10, 10, 0));
        check(route.Count == 2, "Same map coordinates on another floor do not trigger absence");
        Start(head, next);
        MissingUntil(5000, new(float.NaN, 0, 0));
        check(route.Count == 2, "Invalid player position cannot auto-skip");

        foreach (var kind in new[] { SpotKind.Silver, SpotKind.PotGold, SpotKind.Other })
        {
            Start(head, next);
            for (long t = 0; t <= 5000; t += 500) Scan(t, objects: [Present(head, targetable: false) with { Kind = kind, Opening = true }]);
            check(route.First() == head, "Explicit opening state blocks empty-pad detection even while untargetable");
            Start(head, next);
            for (long t = 0; t <= 3000; t += 500) Scan(t, objects: [Present(head, targetable: false) with { Kind = kind }]);
            check(route.SequenceEqual([next]) && session.Get(head.Id)!.Status == SpotStatus.Skipped, "Untargetable placeholder no longer blocks next stop indefinitely");
        }

        Start(head, next);
        for (long t = 0; t <= 3000; t += 500) Scan(t, new(-35, 0, 0));
        check(route.SequenceEqual([next]), "45m visible-area report now enters default empty-point confirmation");
        Start(head, next);
        for (long t = 0; t <= 3000; t += 500) Scan(t, new(-35, 0, 0), radius: 20);
        check(route.Count == 2 && automation.Detail.Contains("45 m／判定 20 m"), "Custom conservative radius explains why confirmation has not started");
        Start(head, next);
        Scan(0, objects: [Present(head, targetable: false)]);
        Scan(1000, objects: [Present(head, targetable: false)]);
        check(automation.Detail.Contains("不可選取") && automation.Detail.Contains("1.0 / 3.0"), "Untargetable countdown is visible to diagnose a stalled route");
        Scan(1500, objects: [Present(head, targetable: false) with { Opening = true }]);
        check(automation.Detail.Contains("正在開啟"), "Opening state explains the wait and resets empty countdown");
        Scan(2000, objects: [Present(head)]);
        check(automation.Detail.Contains("可選取"), "Usable chest explains why auto-next waits");
        Start(head, next);
        Scan(0); Scan(2000);
        check(Scan(4000).Reason == RouteAdvanceReason.Empty, "Regular slow scans can confirm absence instead of restarting forever at 1.5s");
        check(RouteAutomation.NormalizeRadius(float.NaN) == 60 && RouteAutomation.NormalizeRadius(1000) == 100 && RouteAutomation.NormalizeRadius(-1) == 20, "Bad or extreme radius values are normalized");
        Start(head, next);
        MissingUntil(2500);
        Scan(3000, objects: [Present(head)]);
        for (long t = 3500; t < 6500; t += 500) Scan(t);
        check(route.First() == head, "A returning chest resets the complete grace period");
        check(Scan(6500).Reason == RouteAdvanceReason.Empty, "A fresh sustained absence can eventually skip");

        Start(head, next);
        MissingUntil(1000);
        check(Scan(6000).Reason == RouteAdvanceReason.None && route.Count == 2, "A scan stall does not count as observed absence");
        Start(head, next);
        MissingUntil(2500);
        automation.ResetInspection();
        check(Scan(3000).Reason == RouteAdvanceReason.None, "A failed scan restarts absence timing");
        Start(head, next);
        MissingUntil(5000, checkAbsence: false);
        check(Scan(5500).Reason == RouteAdvanceReason.None && route.Count == 2, "Event/cutscene pause does not accumulate absence time");
        Start(head, next);
        MissingUntil(5000, enabled: false);
        check(route.Count == 2 && !automation.CanAttemptFlag(next.Id, 5000), "Disabled automation does not skip or flag");
        Scan(5500, objects: [Present(head, available: false)], enabled: false);
        check(route.SequenceEqual([next]) && !automation.CanAttemptFlag(next.Id, 5500), "Disabled automation still removes spent chest but never changes map flag");

        foreach (var kind in new[] { SpotKind.Carrot, SpotKind.Exploration })
        {
            Start(head with { Kind = kind }, next);
            MissingUntil(5000);
            check(route.Count == 2, "Carrot and exploration stops require manual progress");
        }
        Start(head, next);
        Scan(0, objects: [Present(head), Present(next)], mode: PointDisplayMode.Visible);
        for (long t = 500; t < 3500; t += 500) Scan(t, objects: [Present(next)], mode: PointDisplayMode.Visible);
        check(route.First() == head, "Visible-only current nearby stop survives grace period");
        update = Scan(3500, objects: [Present(next)], mode: PointDisplayMode.Visible);
        check(update.Reason == RouteAdvanceReason.Empty && route.SequenceEqual([next]) && automation.CanAttemptFlag(next.Id, 3500), "Visible-only empty head still advances to a loaded next stop");
        Start(head, next);
        Scan(0, objects: [Present(head), Present(next)], mode: PointDisplayMode.Visible);
        Scan(500, new(-100, 0, 0), [Present(next)], mode: PointDisplayMode.Visible);
        check(route.SequenceEqual([next]) && !automation.CanAttemptFlag(next.Id, 500) && session.Get(head.Id)!.Status == SpotStatus.LastSeen, "Distant render unloading alone never auto-flags or completes a stop");

        Start(head, next);
        MissingUntil(2500);
        route.RemoveAt(0);
        check(Scan(3000, new(70, 0, 0)).Reason == RouteAdvanceReason.None, "New route head never inherits the previous absence timer");
        Start(head);
        MissingUntil(3000);
        check(route.Count == 0 && !automation.CanAttemptFlag(null, 3000), "Final empty stop has no invalid next flag");
        session.RestartSurvey();
        check(session.Get(head.Id)!.Status == SpotStatus.Candidate, "Restart clears skipped locations");

        automation.ScheduleFlag(next.Id);
        check(automation.CanAttemptFlag(next.Id, 0), "Automatic flag starts immediately");
        automation.RecordFlagAttempt(0, false);
        check(!automation.CanAttemptFlag(next.Id, 1499) && automation.CanAttemptFlag(next.Id, 1500), "Failed flag retry is throttled");
        automation.RecordFlagAttempt(1500, false);
        automation.RecordFlagAttempt(3000, false);
        check(!automation.CanAttemptFlag(next.Id, 10000), "Failed automatic flag attempts are bounded");
        automation.ScheduleFlag(next.Id);
        check(!automation.CanAttemptFlag(head.Id, 11000), "Head change cancels stale automatic flag");
        automation.ScheduleFlag(next.Id);
        automation.CancelFlag();
        check(!automation.CanAttemptFlag(next.Id, 12000), "Manual or pot flag cancels queued route flag");
        automation.ScheduleFlag(next.Id);
        automation.Reset();
        check(!automation.CanAttemptFlag(next.Id, 13000), "Zone/config/route reset disarms queued flags");

        var nearDone = head with { Id = "done", Position = new(1, 0, 0) };
        var oldPending = head with { Id = "old-pending", Position = new(200, 0, 0) };
        var newPending = next with { Id = "new-pending", Position = new(2, 0, 0) };
        var empty = next with { Id = "empty", Position = new(3, 0, 0) };
        Start(nearDone, oldPending, newPending, empty);
        session.Visit(nearDone.Id); session.Skip(empty.Id);
        var candidates = session.Snapshot().Where(s => session.CanDisplay(s.Spot.Id, PointDisplayMode.Candidates)).Select(s => s.Spot);
        var resumed = RoutePlanner.PlanPrioritized(Vector3.Zero, candidates, new HashSet<string> { nearDone.Id, oldPending.Id });
        check(resumed.Stops.Select(s => s.Id).SequenceEqual(new[] { oldPending.Id, newPending.Id }), "Resume visits previous unfinished stop before nearer new stop and excludes visited/skipped points");
        check(session.Get(nearDone.Id)!.Status == SpotStatus.Visited && session.Get(empty.Id)!.Status == SpotStatus.Skipped, "Continuation preserves existing patrol history");
        check(!resumed.Exact && Math.Abs(resumed.Length - 398) < 0.001, "Priority group route length includes boundary without claiming unconstrained optimality");
        var normal = RoutePlanner.Plan(Vector3.Zero, candidates);
        check(normal.Stops[0] == newPending, "Normal distance planning remains separate from priority resume");
        var noPriority = RoutePlanner.PlanPrioritized(Vector3.Zero, candidates, new HashSet<string> { "missing" });
        check(noPriority.Stops.SequenceEqual(normal.Stops) && noPriority.Exact, "Unavailable previous points do not fabricate a destination");
        var allPriority = RoutePlanner.PlanPrioritized(Vector3.Zero, candidates, new HashSet<string> { oldPending.Id, newPending.Id });
        check(allPriority.Exact && allPriority.Stops.SequenceEqual(normal.Stops), "Single priority group retains normal solver guarantees");
        var three = RoutePlanner.PlanPrioritized(Vector3.Zero, [nearDone, oldPending, newPending], new HashSet<string> { oldPending.Id, newPending.Id });
        check(three.Stops.Take(2).All(s => s.Id != nearDone.Id) && three.Stops.Select(s => s.Id).Distinct().Count() == 3, "All priority points precede the secondary group exactly once");
        Start(head, next);
        Scan(0, objects: [Present(head, available: false)]);
        session.Visit(next.Id);
        session.RestartSurvey();
        check(!session.CanDisplay(head.Id, PointDisplayMode.Candidates) && session.CanDisplay(next.Id, PointDisplayMode.Candidates), "Explicit clear resets manual history but retains actual opened evidence");
        Scan(500, objects: [Present(head)]);
        check(session.Get(head.Id)!.Status == SpotStatus.Visible, "Observed respawn can clear spent evidence even when object ID is reused");
    }
}
