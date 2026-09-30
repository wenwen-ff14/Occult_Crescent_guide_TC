using System.Numerics;
using CrescentCompass.Core;

internal static class PatrolContextChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var context = new PatrolContext();
        check(context.Update(true, false, true, 1252, 2, 0) == PatrolContextChange.Reset, "First island entry initializes a survey");
        check(context.Update(true, false, true, 1252, 2, 500) == PatrolContextChange.Active, "Stable context does not reset repeatedly");
        check(context.Update(true, true, false, 0, 0, 1000) == PatrolContextChange.Suspended && context.Territory == 1252 && context.Instance == 2,
            "Teleport with missing territory/player/instance suspends rather than forgetting the island");
        check(context.Update(true, true, false, 0, 0, 60000) == PatrolContextChange.Suspended, "Long loading screens cannot expire the patrol");
        check(context.Update(true, false, true, 1252, 0, 61000) == PatrolContextChange.Suspended && context.Instance == 2,
            "Transient zero instance number cannot reset a previously identified instance");
        check(context.Update(true, false, true, 1252, 2, 62000) == PatrolContextChange.Suspended, "Landing waits for stable context");
        check(context.Update(true, false, true, 1252, 2, 62999) == PatrolContextChange.Suspended, "Partial landing stabilization does not resume");
        check(context.Update(true, false, true, 1252, 2, 63000) == PatrolContextChange.Resumed && !context.IsSuspended,
            "Same-island same-instance teleport resumes without reset");
        context.Suspend(); // TerritoryChanged can fire even when the final territory is unchanged.
        context.Update(true, false, true, 1252, 2, 64000);
        check(context.Update(true, false, true, 1252, 2, 65000) == PatrolContextChange.Resumed, "Repeated same-territory event is a pause, not a new visit");
        context.Update(true, true, false, 0, 0, 66000);
        context.Update(true, false, true, 1252, 2, 67000);
        context.Update(true, false, true, 1252, 3, 67500);
        check(context.Update(true, false, true, 1252, 3, 68000) == PatrolContextChange.Suspended, "Changing landing context restarts stabilization");
        check(context.Update(true, false, true, 1252, 3, 68500) == PatrolContextChange.Reset && context.Instance == 3,
            "A confirmed different instance resets stale personal treasure state");
        check(context.Update(true, false, true, 1346, 1, 69000) == PatrolContextChange.Reset && context.Territory == 1346, "South-to-north travel starts a different survey");
        check(context.Update(true, false, true, 1, 0, 70000) == PatrolContextChange.Reset && context.Territory == 0, "Leaving the islands clears the survey");
        context.Update(true, false, true, 1252, 0, 71000);
        context.Update(true, true, false, 0, 0, 72000);
        context.Update(true, false, true, 1252, 0, 73000);
        check(context.Update(true, false, true, 1252, 0, 74000) == PatrolContextChange.Resumed, "Clients consistently reporting instance zero can still resume");
        check(context.Update(false, true, false, 0, 0, 75000) == PatrolContextChange.Suspended && context.Territory == 1252,
            "A missing login snapshot during teleport is not mistaken for logout");
        context.Logout();
        check(context.Territory == 0 && !context.IsSuspended, "An explicit logout event clears the session immediately even while loading");
        context.Update(true, false, true, 1252, 2, 76000);
        check(context.Update(false, false, false, 0, 0, 77000) == PatrolContextChange.Suspended, "Brief signed-out snapshot gets a grace period");
        check(context.Update(false, false, false, 0, 0, 82000) == PatrolContextChange.Reset && context.Territory == 0,
            "Sustained signed-out state clears the survey if a logout event was missed");

        var points = Enumerable.Range(1, 4).Select(i => new Spot($"teleport-{i}", 1252, SpotKind.Silver, (uint)i, new Vector3(i * 100, 0, 0))).ToArray();
        var session = new SurveySession(); session.Reset(1252, points);
        session.Observe(points.Select((p, i) => new Observation((ulong)i + 10, p.DataId, p.Kind, p.Position)), DateTimeOffset.UtcNow);
        session.Visit(points[0].Id); session.Skip(points[1].Id);
        var remaining = points.Skip(2).ToList();
        // The actual pause operation invalidates only visibility; route filtering retains already selected stops.
        session.Observe([], DateTimeOffset.UtcNow);
        var automation = new RouteAutomation();
        automation.Update(session, remaining, [], new(-500, 0, 0), 80000, PointDisplayMode.Visible, true, preservePlannedStops: true);
        check(remaining.SequenceEqual(points.Skip(2)), "Teleport does not delete unloaded planned chests even in visible-only list mode");
        check(session.Get(points[0].Id)?.Status == SpotStatus.Visited && session.Get(points[1].Id)?.Status == SpotStatus.Skipped,
            "Visited and skipped history survives the visibility pause");
        check(!session.CanDisplay(points[2].Id, PointDisplayMode.Visible) && session.CanPatrol(points[2].Id), "A retained planned destination can be navigated without claiming it is loaded");
        session.Visit(points[2].Id);
        var result = automation.Update(session, remaining, [], new(-500, 0, 0), 81000, PointDisplayMode.Visible, true, preservePlannedStops: true);
        check(result.Reason == RouteAdvanceReason.Opened && remaining.Single() == points[3], "Retained route still removes completed chests and advances");
    }
}
