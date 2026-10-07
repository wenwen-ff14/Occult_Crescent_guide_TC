using System.Numerics;
using CrescentCompass.Core;

internal static class AutoPatrolChecks
{
    private sealed class Navigation : IChestPatrolNavigation
    {
        public bool Ready { get; set; } = true;
        public PatrolMovement Movement { get; set; }
        public IReadOnlyList<Vector3> RemainingPath => Moves.LastOrDefault() ?? [];
        public int Queries { get; private set; }
        public int RecoveryQueries { get; private set; }
        public int Stops { get; private set; }
        public int Jumps { get; private set; }
        public bool JumpResult { get; set; }
        public bool TryJump() { Jumps++; return JumpResult; }
        public List<Vector3[]> Moves { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public Func<Vector3, Vector3, Task<IReadOnlyList<Vector3>>>? Query { get; set; }
        public Func<Vector3, Vector3, Task<IReadOnlyList<Vector3>>>? RecoveryQuery { get; set; }
        public Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to, CancellationToken cancellation)
        {
            Queries++; LastToken = cancellation;
            return Query?.Invoke(from, to) ?? Task.FromResult<IReadOnlyList<Vector3>>([from, to]);
        }
        public Task<IReadOnlyList<Vector3>> RecoverPath(Vector3 from, Vector3 to, IReadOnlyList<Vector3> failedPath,
            int attempt, CancellationToken cancellation)
        {
            RecoveryQueries++; Queries++; LastToken = cancellation;
            return RecoveryQuery?.Invoke(from, to) ?? Task.FromResult<IReadOnlyList<Vector3>>([from, from + Vector3.UnitZ * 4, to]);
        }
        public void Move(IReadOnlyList<Vector3> path) { Moves.Add(path.ToArray()); Movement = PatrolMovement.Owned; }
        public void Stop() { if (Movement == PatrolMovement.Owned) { Stops++; Movement = PatrolMovement.Idle; } }
    }

    public static void Run(Action<bool, string> check)
    {
        var first = new Spot("1252:Bronze:9", 1252, SpotKind.Bronze, 1, new(100, 0, 0));
        var next = new Spot("1252:Bronze:10", 1252, SpotKind.Bronze, 2, new(150, 0, 0));
        var frame = new ChestPatrolFrame(new(1252, 1, 10, true), Vector3.Zero, first);
        Navigation nav = new();
        AutoChestPatrol patrol = new(nav);
        void StartMoving()
        {
            nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(frame, 0); patrol.Update(frame, 1);
            check(nav.Moves.Count == 1 && nav.Movement == PatrolMovement.Owned, "Explicit start finds and follows one ground path");
        }
        patrol.Update(frame, 0);
        check(nav.Queries == 0 && nav.Moves.Count == 0, "Patrol is idle until explicitly started");
        StartMoving();
        patrol.Update(frame with { Position = new(1, 0, 0) }, 1000);
        check(nav.Queries == 1 && nav.Moves.Count == 1, "Moving does not replace its path every frame");
        patrol.Update(frame with { Target = next }, 1100);
        patrol.Update(frame with { Target = next }, 1101);
        check(nav.Stops == 1 && nav.Moves.Count == 2 && nav.Moves[^1][^1] == next.Position, "Advancing a route stop stops the old movement and finds a new path");
        patrol.Update(frame with { Target = null }, 1200);
        check(!patrol.Enabled && !patrol.Faulted && nav.Movement == PatrolMovement.Idle, "Completing the route stops movement without starting another loop");

        foreach (var blocked in new[] {
            frame with { Player = frame.Player with { Occupied = true } },
            frame with { Player = frame.Player with { Paused = true } },
            frame with { Player = frame.Player with { InFlight = true } },
            frame with { Player = frame.Player with { RidingPillion = true } },
            frame with { Player = frame.Player with { Ready = false } },
            frame with { Planning = true }, frame with { EventNavigation = true },
            frame with { Suspended = true }, frame with { ObservationsFresh = false } })
        {
            StartMoving(); patrol.Update(blocked, 1000);
            check(patrol.Enabled && nav.Movement == PatrolMovement.Idle && nav.Moves.Count == 1, "Blocked gameplay stops owned movement while retaining patrol intent");
            patrol.Update(frame, 2000); patrol.Update(frame, 2001);
            check(patrol.Enabled && nav.Moves.Count == 2, "Resuming a temporary block finds a fresh ground path");
        }
        foreach (var changed in new[] {
            frame with { Player = frame.Player with { CharacterId = 11 } },
            frame with { Player = frame.Player with { Instance = 2 } },
            frame with { Player = frame.Player with { Territory = 1346 } },
            frame with { Player = frame.Player with { Dead = true } } })
        {
            StartMoving(); patrol.Update(changed, 1000); patrol.Update(frame, 2000);
            check(!patrol.Enabled && nav.Movement == PatrolMovement.Idle && nav.Moves.Count == 1, "Death or session change never automatically restarts movement");
        }
        StartMoving(); nav.Ready = false; patrol.Update(frame, 1000);
        check(patrol.Faulted && !patrol.Enabled && nav.Stops == 1, "Navigation unload or disabled movement stops the patrol");
        StartMoving(); nav.Movement = PatrolMovement.External; patrol.Update(frame, 1000);
        check(!patrol.Enabled && nav.Stops == 0 && nav.Movement == PatrolMovement.External, "An external replacement is never stopped or overwritten");
        StartMoving(); nav.Movement = PatrolMovement.Idle; patrol.Update(frame, 1000); patrol.Update(frame, 2000);
        check(patrol.Faulted && !patrol.Enabled && nav.Moves.Count == 1, "Manual movement cancellation is respected and not retried");
        StartMoving(); patrol.Update(frame, 6_001);
        check(patrol.Enabled && !patrol.Faulted && nav.Stops == 1 && patrol.Recovering && patrol.PathFrom(frame.Position) is null, "No progress stops owned movement before a bounded local recovery");
        patrol.Update(frame, 6_002); patrol.Update(frame, 6_003);
        check(nav.Moves.Count == 2 && nav.Queries == 2 && nav.RecoveryQueries == 1, "Stuck recovery requests an alternate path for only the current leg without planning the entire tour");
        patrol.Update(frame, 12_003); patrol.Update(frame, 12_004); patrol.Update(frame, 12_005);
        patrol.Update(frame, 18_005); patrol.Update(frame, 18_006); patrol.Update(frame, 18_007);
        patrol.Update(frame, 24_007);
        check(patrol.Faulted && !patrol.Enabled && nav.Moves.Count == 4, "Three failed recoveries stop without skipping or restarting forever");
        StartMoving(); patrol.Update(frame with { Position = new(5, 0, 0) }, 5_000); patrol.Update(frame with { Position = new(5, 0, 0) }, 10_000);
        check(patrol.Enabled, "Actual progress refreshes the stuck timer");

        StartMoving();
        var arrived = frame with { Position = first.Position - new Vector3(1.8f, 0, 0), ChestInRange = true };
        patrol.Update(arrived, 1000);
        check(nav.Stops == 1 && patrol.Enabled && !patrol.CanOpenChest, "Stop before interaction when a loaded chest is within the two-meter opening radius");
        patrol.Update(arrived, 1249);
        check(!patrol.CanOpenChest, "Patrol opener waits for the bounded settling interval");
        patrol.Update(arrived, 1250);
        check(patrol.CanOpenChest, "A stationary nearby chest is released to the opener after settling");
        patrol.Update(arrived with { Position = arrived.Position + new Vector3(0.2f, 0, 0) }, 1251);
        check(!patrol.CanOpenChest, "Continuing to drift restarts settling instead of interacting while moving");
        patrol.Update(arrived, 21_000);
        check(!patrol.Enabled && patrol.Faulted && nav.Queries == 1, "Rejected opening or missing empty confirmation times out instead of walking forever");
        StartMoving(); patrol.Update(frame with { Position = first.Position + new Vector3(0, 5, 0) }, 1000);
        check(nav.Stops == 0, "A different floor is not arrival");

        foreach (var path in new Vector3[][] { [], [Vector3.Zero, new(80, 0, 0)], [new(4, 0, 0), first.Position],
            [Vector3.Zero, new(float.NaN, 0, 0)], [Vector3.Zero, first.Position + new Vector3(0, 2, 0)] })
        {
            nav = new() { Query = (_, _) => Task.FromResult<IReadOnlyList<Vector3>>(path) };
            patrol = new(nav); patrol.Start(); patrol.Update(frame, 0); patrol.Update(frame, 1);
            check(patrol.Faulted && nav.Moves.Count == 0, "Invalid, incomplete or wrong-floor paths never start movement");
        }
        var projected = new[] { Vector3.Zero, first.Position - Vector3.UnitX };
        nav = new() { Query = (_, _) => Task.FromResult<IReadOnlyList<Vector3>>(projected) };
        patrol = new(nav); patrol.Start(); patrol.Update(frame, 0); patrol.Update(frame, 1);
        check(nav.Moves.Single().SequenceEqual(projected), "Mesh endpoint within interaction range is used without appending an unverified straight segment");

        foreach (var cancel in new[] { "stop", "pause", "switch" })
        {
            var pending = new TaskCompletionSource<IReadOnlyList<Vector3>>();
            nav = new() { Query = (_, _) => pending.Task };
            patrol = new(nav); patrol.Start(); patrol.Update(frame, 0);
            var oldToken = nav.LastToken;
            if (cancel == "stop") patrol.Stop();
            else if (cancel == "pause") patrol.Update(frame with { Player = frame.Player with { Paused = true } }, 1);
            else patrol.Update(frame with { Target = next }, 1);
            check(oldToken.IsCancellationRequested, "Stopping, pausing or switching targets cancels only the owned path request");
            pending.SetResult([Vector3.Zero, first.Position]);
            if (cancel == "stop") patrol.Update(frame, 2);
            else if (cancel == "pause") patrol.Update(frame with { Player = frame.Player with { Paused = true } }, 2);
            else patrol.Update(frame with { Target = next }, 2);
            check(nav.Moves.Count == 0, "Late cancelled results cannot move toward an old destination");
        }
        var delayed = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        nav = new() { Query = (_, _) => delayed.Task }; patrol = new(nav); patrol.Start(); patrol.Update(frame, 0);
        delayed.SetResult([Vector3.Zero, first.Position]);
        patrol.Update(frame with { Position = new(5, 0, 0) }, 1);
        check(nav.Moves.Count == 0 && patrol.Enabled, "Moving away during pathfinding discards the stale origin");
        nav.Query = null; patrol.Update(frame with { Position = new(5, 0, 0) }, 2); patrol.Update(frame with { Position = new(5, 0, 0) }, 3);
        check(nav.Moves.Count == 1 && nav.Moves[0][0] == new Vector3(5, 0, 0), "Stale origin is replanned from the current position");
        nav = new() { Query = (_, _) => new TaskCompletionSource<IReadOnlyList<Vector3>>().Task };
        patrol = new(nav); patrol.Start(); patrol.Update(frame, 0); patrol.Update(frame, 16_000);
        check(patrol.Faulted && nav.LastToken.IsCancellationRequested && nav.Moves.Count == 0, "A provider that never finishes is cancelled after the timeout");
        nav = new() { Query = (_, _) => Task.FromException<IReadOnlyList<Vector3>>(new InvalidOperationException("No mesh")) };
        patrol = new(nav); patrol.Start(); patrol.Update(frame, 0); patrol.Update(frame, 1);
        check(patrol.Faulted && nav.Moves.Count == 0, "Pathfinding errors stop without movement");

        Vector3[] owned = [Vector3.Zero, Vector3.UnitX, new(10, 0, 0)];
        check(AutoChestPatrol.OwnsPath(owned, owned) && AutoChestPatrol.OwnsPath(owned, owned[1..]), "Ownership recognizes only the submitted path and its remaining suffixes");
        check(!AutoChestPatrol.OwnsPath(owned, [new(2, 0, 0), new(10, 0, 0)]) && !AutoChestPatrol.OwnsPath(owned, []) &&
            !AutoChestPatrol.OwnsPath(null, owned), "Matching destination alone does not grant ownership of another route");

        StartMoving();
        patrol.Update(frame with { Position = new(-2, 0, 0) }, 5_000);
        patrol.Update(frame, 6_001);
        check(nav.Stops == 1 && patrol.Enabled, "Moving away and back does not disguise lack of route progress");
        check(AutoChestPatrol.RemainingDistance(new(0, 0, 5), [Vector3.Zero, new(0, 0, 10), new(10, 0, 10)]) == 15,
            "Progress tracks the bent polyline, not straight-line distance to the chest");

        nav = new(); patrol = new(nav); patrol.Start();
        var lookahead = frame with { Following = next };
        patrol.Update(lookahead, 0); patrol.Update(lookahead, 1);
        check(nav.Moves.Count == 1 && nav.Queries == 2 && nav.Moves[0][^1] == first.Position,
            "First leg starts immediately and the following fixed leg is only preloaded, never moved");
        patrol.Update(lookahead, 10); patrol.Update(lookahead, 20);
        check(nav.Queries == 2, "Lookahead makes only one speculative query per active leg");
        patrol.Stop();

        var warm = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        nav = new() { Query = (a, b) => b == next.Position ? warm.Task : Task.FromResult<IReadOnlyList<Vector3>>([a, b]) };
        patrol = new(nav); patrol.Start(); patrol.Update(lookahead, 0); patrol.Update(lookahead, 1);
        var warmToken = nav.LastToken;
        patrol.Update(lookahead with { Player = frame.Player with { Paused = true } }, 2);
        warm.SetResult([first.Position, next.Position]);
        check(warmToken.IsCancellationRequested && nav.Moves.Count == 1, "Pausing cancels lookahead and a late preload can never move the player");

        nav = new() { Query = (a, b) => b == next.Position ? Task.FromException<IReadOnlyList<Vector3>>(new InvalidOperationException()) :
            Task.FromResult<IReadOnlyList<Vector3>>([a, b]) };
        patrol = new(nav); patrol.Start(); patrol.Update(lookahead, 0); patrol.Update(lookahead, 1); patrol.Update(lookahead, 2);
        check(patrol.Enabled && !patrol.Faulted && nav.Moves.Count == 1, "Speculative next-leg failures do not stop current movement");

        var cache = new PatrolPathCache(); var meshQueries = 0;
        nav = new() { Query = (a, b) => cache.FindPath(a, b, (x, y, _) =>
        {
            meshQueries++;
            return Task.FromResult<IReadOnlyList<Vector3>>([x, y]);
        }, default) };
        patrol = new(nav); patrol.Start(); patrol.Update(lookahead, 0); patrol.Update(lookahead, 1);
        patrol.Update(arrived with { Position = first.Position - new Vector3(1.5f, 0, 0) }, 1000);
        var advance = frame with { Position = first.Position - new Vector3(1.5f, 0, 0), Target = next };
        patrol.Update(advance, 1500); patrol.Update(advance, 1501);
        check(meshQueries == 3 && nav.Moves.Count == 2 && nav.Moves[^1][^1] == next.Position,
            "Chest standoff validates a short connector and preserves the preloaded fixed leg");

        var session = new SurveySession(); session.Reset(1252, [first, next]);
        List<Spot> remaining = [first, next];
        var route = new RouteAutomation();
        string? EmptyBlock(Vector3 from) => PatrolEmptyCheck.BlockReason(from, first, [],
            new(first.Id, from, [from, first.Position], Vector3.Distance(from, first.Position)), 60);
        for (var tick = 0; tick <= 3500; tick += 500)
            route.Update(session, remaining, [], first.Position - new Vector3(30, 0, 0), tick, PointDisplayMode.Observed, true,
                absenceBlockReason: EmptyBlock(first.Position - new Vector3(30, 0, 0)), preservePlannedStops: true);
        check(remaining.Count == 2 && session.Get(first.Id)?.Status != SpotStatus.Skipped, "Auto patrol cannot skip a candidate while still approaching within the old 60-meter radius");
        for (var tick = 4000; tick <= 7000; tick += 500)
            route.Update(session, remaining, [], first.Position, tick, PointDisplayMode.Observed, true,
                absenceBlockReason: EmptyBlock(first.Position), preservePlannedStops: true);
        check(remaining.Count == 1 && remaining[0] == next && session.Get(first.Id)?.Status == SpotStatus.Skipped,
            "After reaching the pad, three seconds of confirmed absence advances without claiming collection");
        check(EmptyBlock(first.Position + new Vector3(0, 7, 0)) is not null, "Empty checks cannot inspect a different floor through the vertical guard");


        StartMoving(); nav.RecoveryQuery = (_, _) => Task.FromResult<IReadOnlyList<Vector3>>([]);
        patrol.Update(frame, 6001); patrol.Update(frame, 6002); patrol.Update(frame, 6003);
        check(patrol.Faulted && nav.Moves.Count == 1 && nav.RecoveryQueries == 1, "No alternative stops without replaying the failed path");
        StartMoving();
        var recovery = new TaskCompletionSource<IReadOnlyList<Vector3>>();
        nav.RecoveryQuery = (_, _) => recovery.Task;
        patrol.Update(frame, 6001); patrol.Update(frame, 6002);
        var recoveryToken = nav.LastToken;
        patrol.Stop(); recovery.SetResult([Vector3.Zero, first.Position]); patrol.Update(frame, 6003);
        check(recoveryToken.IsCancellationRequested && nav.Moves.Count == 1, "Late recovery results cannot restart a stopped patrol");
        StartMoving(); nav.RecoveryQuery = (_, _) => new TaskCompletionSource<IReadOnlyList<Vector3>>().Task;
        patrol.Update(frame, 6001); patrol.Update(frame, 6002); patrol.Update(frame, 22002);
        check(patrol.Faulted && nav.LastToken.IsCancellationRequested, "Hung recovery is bounded and cancelled");
        StartMoving(); patrol.Update(frame, 6001); patrol.Update(frame, 6002);
        nav.Movement = PatrolMovement.External; patrol.Update(frame, 6003);
        check(patrol.Faulted && nav.Movement == PatrolMovement.External && nav.Moves.Count == 1,
            "External navigation taking over during recovery is never overwritten");
        StartMoving(); patrol.Update(frame, 6001); patrol.Update(frame, 6002);
        patrol.Update(frame with { Position = new(0, 0, 0.6f) }, 6003);
        check(patrol.Faulted && nav.Moves.Count == 1, "Moving away from a recovery origin cannot introduce an unverified connector");

        var combat = frame with { Player = frame.Player with { InCombat = true } };
        nav = new(); patrol = new(nav); patrol.Start();
        patrol.Update(combat, 0); patrol.Update(combat, 1);
        check(patrol.Enabled && nav.Moves.Count == 1 && nav.Movement == PatrolMovement.Owned,
            "Patrol can start and follow a ground path while already in combat");
        patrol.Update(frame, 1000); patrol.Update(combat, 2000);
        check(nav.Queries == 1 && nav.Stops == 0 && nav.Moves.Count == 1,
            "Entering and leaving combat neither stops movement nor replaces the cached path");
        patrol.Update(combat, 6001); patrol.Update(combat, 6002); patrol.Update(combat, 6003);
        check(patrol.Enabled && nav.RecoveryQueries == 1 && nav.Moves.Count == 2,
            "Combat transitions do not reset stuck detection and recovery still moves in combat");
        var combatArrival = arrived with { Player = combat.Player };
        patrol.Update(combatArrival, 7000); patrol.Update(combatArrival, 7250);
        check(patrol.CanOpenChest && patrol.Enabled && !patrol.Faulted,
            "Combat arrival uses normal settling and permits the patrol opener instead of waiting for combat to end");

        session.Reset(1252, [first, next]); remaining = [first, next]; route = new();
        var approach = first.Position - new Vector3(10, 0, 0);
        for (var tick = 0; tick <= 1000; tick += 500)
            route.Update(session, remaining, [], approach, tick, PointDisplayMode.Observed, true,
                canCheckAbsence: combat.Player.GetBlockReason(allowCombat: true).Length == 0,
                absenceBlockReason: EmptyBlock(approach), preservePlannedStops: true, emptyWaitMs: PatrolEmptyCheck.ConfirmationMs);
        check(remaining.Count == 1 && remaining[0] == next && session.Get(first.Id)?.Status == SpotStatus.Skipped,
            "Three confirmed empty scans advance in combat without claiming the chest was collected");
        patrol.Update(combat with { Position = approach, Target = remaining[0] }, 8000);
        patrol.Update(combat with { Position = approach, Target = remaining[0] }, 8001);
        check(nav.Moves.Count == 3 && nav.Moves[^1][^1] == next.Position,
            "Combat patrol continues to the next route stop after an empty pad");
        foreach (var blocked in new[]
        {
            combat with { Player = combat.Player with { Paused = true } },
            combat with { Player = combat.Player with { Occupied = true } },
            combat with { Player = combat.Player with { InFlight = true } },
            combat with { Player = combat.Player with { RidingPillion = true } },
            combat with { Player = combat.Player with { Dead = true } },
        })
        {
            StartMoving(); patrol.Update(blocked, 1000);
            check(nav.Movement == PatrolMovement.Idle && !patrol.CanOpenChest,
                "Allowing combat does not bypass pause, occupied, flight, passenger or death restrictions");
        }
        StartMoving(); nav.Movement = PatrolMovement.Idle; patrol.Update(combat, 1000);
        check(patrol.Faulted && nav.Moves.Count == 1, "Combat never overrides a manual navigation cancellation");
        StartMoving(); nav.Movement = PatrolMovement.External; patrol.Update(combat, 1000);
        check(patrol.Faulted && nav.Stops == 0, "Combat never takes control away from external navigation");

        foreach (var accepted in new[] { false, true })
        {
            StartMoving(); nav.JumpResult = accepted;
            patrol.Update(combat, 3000);
            check(nav.Jumps == 0, "Three full seconds of no progress precede a jump attempt");
            patrol.Update(combat, 3001); patrol.Update(combat, 3002); patrol.Update(combat, 5000);
            check(nav.Jumps == 1 && nav.Stops == 0, "Accepted and rejected jumps are both throttled while movement continues in combat");
            patrol.Update(combat, 5001);
            check(nav.Jumps == 2, "A grounded snag allows another jump after two seconds");
            patrol.Update(combat, 6001);
            check(patrol.Recovering && nav.Stops == 1 && nav.Jumps == 2,
                "Jump attempts never reset the six-second alternate-path deadline");
        }
        StartMoving(); nav.JumpResult = true; patrol.Update(frame, 3001);
        var airborne = combat with { Player = combat.Player with { Jumping = true }, Position = new(0, 2, 0) };
        patrol.Update(airborne, 3500); patrol.Update(airborne, 6001);
        check(patrol.Enabled && !patrol.Recovering && nav.Stops == 0 && nav.Jumps == 1,
            "Airborne combat frames keep the owned path and defer repathing until landing");
        patrol.Update(combat, 6500);
        check(patrol.Recovering && nav.Stops == 1, "Vertical-only jumping does not count as progress after landing");
        StartMoving(); nav.JumpResult = true; patrol.Update(frame, 3001);
        patrol.Update(airborne with { Position = first.Position, ChestInRange = true }, 3500);
        check(!patrol.CanOpenChest && nav.Stops == 0, "An airborne chest approach never stops early or opens mid-jump");
        patrol.Update(combatArrival, 4000); patrol.Update(combatArrival, 4250);
        check(patrol.CanOpenChest && nav.Stops == 1, "Landing restores the normal stop and settle before opening");
        StartMoving(); nav.JumpResult = true; patrol.Update(frame, 3001);
        patrol.Update(airborne, 3200);
        patrol.Update(combat with { Position = new(5, 0, 0) }, 4000);
        patrol.Update(combat with { Position = new(5, 0, 0) }, 6999);
        check(nav.Jumps == 1 && !patrol.Recovering && nav.Stops == 0, "Successful horizontal jump progress refreshes the stuck timer");
        foreach (var movement in new[] { PatrolMovement.Idle, PatrolMovement.External })
        {
            StartMoving(); nav.Movement = movement; patrol.Update(airborne, 3001);
            check(patrol.Faulted && nav.Jumps == 0 && nav.Stops == 0,
                "Airborne state cannot override manual cancellation or external navigation");
        }
        foreach (var blocked in new[] {
            airborne with { Player = airborne.Player with { Paused = true } },
            airborne with { Player = airborne.Player with { Occupied = true } },
            airborne with { Player = airborne.Player with { Ready = false } },
            airborne with { Player = airborne.Player with { Dead = true } },
        })
        {
            StartMoving(); patrol.Update(blocked, 3001);
            check(nav.Jumps == 0 && nav.Stops == 1, "Jump handling preserves pause, cast, loading and death guards");
        }
        nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(airborne, 0);
        check(nav.Queries == 0 && nav.Jumps == 0, "A patrol started while airborne waits for landing");
        StartMoving(); patrol.Update(airborne, 100); patrol.Update(airborne, 10_100);
        check(patrol.Faulted && nav.Stops == 1, "A permanently airborne state has a finite stop deadline");
        StartMoving(); nav.JumpResult = true;
        long legStarted = 1;
        for (var leg = 0; leg < 4; leg++)
        {
            patrol.Update(combat, legStarted + 3000); patrol.Update(combat, legStarted + 5000);
            patrol.Update(combat, legStarted + 6000);
            if (leg == 3) break;
            patrol.Update(combat, legStarted + 6001); patrol.Update(combat, legStarted + 6002);
            legStarted += 6002;
        }
        check(nav.Jumps == AutoChestPatrol.MaxJumpAttempts && patrol.Faulted && nav.RecoveryQueries == 3,
            "Jump budget spans recovery paths at the same stop and cannot retry indefinitely");
        StartMoving(); nav.Movement = PatrolMovement.Idle;
        patrol.Update(frame with { Position = first.Position, ChestInRange = true }, 1000);
        patrol.Update(frame with { Position = first.Position, ChestInRange = true }, 1250);
        check(patrol.CanOpenChest && !patrol.Faulted, "A naturally completed path still permits settled arrival");
        StartMoving(); nav.Movement = PatrolMovement.Idle;
        var airborneArrival = airborne with { Position = first.Position, ChestInRange = true };
        patrol.Update(airborneArrival, 1000);
        check(patrol.Enabled && !patrol.CanOpenChest, "A path that naturally finishes at the chest mid-jump waits for landing");
        patrol.Update(airborneArrival with { Player = combat.Player }, 1250);
        patrol.Update(airborneArrival with { Player = combat.Player }, 1500);
        check(patrol.CanOpenChest && !patrol.Faulted, "A mid-jump path completion resumes normal opening after landing");
        nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(frame, 0);
        patrol.Update(frame with { Position = new(0, 0, 0.2f) }, 1);
        check(nav.Moves.Count == 0 && patrol.Enabled, "Even a small origin change cannot add an unqueried path into a nearby obstacle");
        var carrot = new Spot("1252:Carrot:22", 1252, SpotKind.Carrot, 2010139, first.Position);
        var carrotFrame = frame with { Target = carrot, CarrotMode = true };
        nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(carrotFrame, 0); patrol.Update(carrotFrame, 1);
        check(nav.Moves.Count == 1 && !patrol.Faulted, "Carrot mode reuses ground movement for the validated 25-point chart");
        var atCarrot = carrotFrame with { Position = carrot.Position, ChestInRange = true, Player = frame.Player with { InCombat = true } };
        patrol.Update(atCarrot, 500); patrol.Update(atCarrot, 750);
        check(patrol.CanOpenChest && nav.Stops == 1, "Carrot patrol keeps combat running but settles before interacting");
        patrol.Update(atCarrot with { ChestInRange = false, ChestOpening = true }, 21_000);
        check(!patrol.Faulted, "Carrot item and bunny sequence is not cut off by the shorter coffer arrival timeout");
        nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(carrotFrame with { CarrotMode = false }, 0);
        check(patrol.Faulted && nav.Queries == 0, "Chest route mode never silently moves to carrot targets");
        nav = new(); patrol = new(nav); patrol.Start(); patrol.Update(frame with { CarrotMode = true }, 0);
        check(patrol.Faulted && nav.Queries == 0, "Carrot route mode never silently moves to chest targets");
    }
}
