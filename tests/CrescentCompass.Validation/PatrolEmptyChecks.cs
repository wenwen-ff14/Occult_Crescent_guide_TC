using System.Numerics;
using CrescentCompass.Core;

internal static class PatrolEmptyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var head = new Spot("1252:Bronze:9", 1252, SpotKind.Bronze, 1, new(50, 0, 0));
        var next = new Spot("1252:Bronze:10", 1252, SpotKind.Bronze, 2, new(100, 0, 0));
        var streamed = new Observation(123, 1, SpotKind.Silver, new(55, 0, 0));
        RouteLeg Path(Vector3 from, double? length = null) => new(head.Id, from, [from, head.Position],
            length ?? Vector3.Distance(from, head.Position));
        string? Block(Vector3 from, IReadOnlyList<Observation> objects, RouteLeg? path, float radius = 60) =>
            PatrolEmptyCheck.BlockReason(from, head, objects, path, radius);

        check(Block(Vector3.Zero, [streamed], Path(Vector3.Zero)) is null, "Nearby streamed coffers permit early inspection at 50m on a verified same-floor path");
        check(Block(Vector3.Zero, [], Path(Vector3.Zero)) is not null, "A distant empty object table does not prove a region is loaded");
        var near = new Vector3(30, 0, 0);
        check(Block(near, [], Path(near)) is null, "Inside 20m an entirely empty pad may be checked without another coffer");
        foreach (var obj in new[] { streamed with { Kind = SpotKind.Carrot }, streamed with { Kind = SpotKind.PotGold },
            streamed with { Position = new(55, 10, 0) }, streamed with { Position = new(90, 0, 0) },
            streamed with { ObjectId = 0 }, streamed with { ObjectId = 0xE0000000 }, streamed with { Position = new(float.NaN, 0, 0) } })
            check(Block(Vector3.Zero, [obj], Path(Vector3.Zero)) is not null, "Unrelated, invalid or different-floor objects cannot prove chest streaming");
        check(Block(Vector3.Zero, [streamed with { Targetable = false }], Path(Vector3.Zero)) is null,
            "A loaded native coffer placeholder is valid region evidence even if not lootable by this player");
        check(Block(new(-11, 0, 0), [streamed], Path(new(-11, 0, 0))) is not null, "No early skipping beyond the bounded 60m range");
        check(Block(Vector3.Zero, [streamed], Path(Vector3.Zero), 30) is not null, "The configured inspection radius remains an upper bound");
        check(Block(new(45, 7, 0), [streamed], Path(new(45, 7, 0))) is not null, "Different-floor pads are not early-empty candidates");
        check(Block(near, [], null) is not null && Block(near, [], Path(near, 70)) is not null &&
            Block(near, [], Path(near, double.NaN)) is not null && Block(near, [], Path(near) with { DestinationId = next.Id }) is not null,
            "Missing, long, invalid or stale-target ground paths block early inspection");

        var session = new SurveySession(); session.Reset(1252, [head, next]);
        var automation = new RouteAutomation();
        List<Spot> remaining = [head, next];
        RouteUpdate Tick(long now, IReadOnlyList<Observation>? obs = null, string? block = null, bool ready = true) =>
            automation.Update(session, remaining, obs ?? [streamed], Vector3.Zero, now, PointDisplayMode.Candidates,
                true, ready, absenceBlockReason: block, preservePlannedStops: true, emptyWaitMs: PatrolEmptyCheck.ConfirmationMs);
        Tick(0); Tick(500);
        check(remaining.Count == 2, "A single missing scan or short grace does not skip a pad");
        check(Tick(1000).Reason == RouteAdvanceReason.Empty && remaining.SequenceEqual([next]) &&
            session.Get(head.Id)?.Status == SpotStatus.Skipped,
            "Three consecutive scans advance a confirmed early empty pad without recording collection");
        session.RestartSurvey(); remaining = [head, next]; automation.Reset();
        Tick(0); Tick(1000);
        check(remaining.Count == 2, "Two sparse scans are insufficient for early-empty confirmation");
        Tick(2001); Tick(2501);
        check(remaining.Count == 2, "A scan gap restarts early-empty confirmation");
        Tick(3001);
        check(remaining.Count == 1, "Confirmation resumes after three fresh scans following a gap");

        foreach (var interruption in new[] { "visible", "opening", "blocked", "occupied", "paused" })
        {
            session.Reset(1252, [head, next]); remaining = [head, next]; automation.Reset();
            Tick(0); Tick(500);
            var chest = new Observation(7, 1, SpotKind.Bronze, head.Position);
            if (interruption == "paused") automation.SetPaused(true);
            Tick(1000, interruption is "visible" or "opening" ? [chest with { Opening = interruption == "opening" }] : null,
                interruption == "blocked" ? "No route" : null, interruption != "occupied");
            if (interruption == "paused") automation.SetPaused(false);
            Tick(1500); Tick(2000);
            check(remaining.Count == 2, $"{interruption} cancels early absence evidence and prevents a stale skip");
            Tick(2500);
            check(remaining.Count == 1, "Early-empty timer restarts only after the obstruction clears");
        }
        session.Reset(1252, [head, next]); remaining = [head, next]; automation.Reset();
        Tick(0); remaining = [next]; Tick(500); Tick(1000);
        check(remaining.Count == 1, "Changing target cannot inherit the prior pad's absence timer");
        session.Reset(1252, [head, next]); session.Skip(head.Id);
        session.Observe([new Observation(7, 1, SpotKind.Bronze, head.Position)], DateTimeOffset.UtcNow);
        check(session.Get(head.Id)?.Status == SpotStatus.Visible && session.CanPatrol(head.Id),
            "An early-skipped pad observed spawning later becomes eligible again, not permanently collected");
    }
}
