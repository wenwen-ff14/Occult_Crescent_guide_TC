using System.Numerics;
using CrescentCompass.Core;

internal static class CarrotChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data/SouthHorn/carrot_locations.json"));
        var catalog = SpotCatalog.Load(stream);
        var route = CarrotRoute.Order(catalog);
        int[] mapping = [22, 11, 1, 10, 21, 8, 9, 3, 4, 7, 12, 5, 24, 6, 13, 20, 17, 2, 14, 19, 18, 15, 23, 25, 16];
        check(route.Select(s => s.Id).SequenceEqual(mapping.Select(i => $"1252:Carrot:{i}")), "All 25 user-image labels map to unchanged catalog coordinates");
        for (var n = 1; n <= 25; n++)
        {
            var rotated = CarrotRoute.Order(catalog.Reverse(), n);
            check(rotated.Count == 25 && rotated.DistinctBy(s => s.Id).Count() == 25 && CarrotRoute.Number(rotated[0]) == n,
                "Every carrot start makes a complete stable chart loop");
            check(rotated.SequenceEqual(route.Skip(n - 1).Concat(route.Take(n - 1))), "Catalog load order cannot change carrot chart order");
        }
        check(CarrotRoute.Number(route[0] with { Territory = 1346 }) is null && CarrotRoute.Number(route[0] with { Kind = SpotKind.Bronze }) is null,
            "Carrot chart excludes other regions and target types");
        foreach (var invalid in new[] { catalog.Skip(1), catalog.Append(catalog[0]), catalog.Select((s, i) => i == 0 ? s with { Position = new(float.NaN) } : s) })
        {
            var rejected = false;
            try { CarrotRoute.Order(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Incomplete, duplicate and invalid-coordinate carrot routes are rejected");
        }
        var weights = new CarrotSearchWeights(); weights.Reset(route);
        check(route.All(p => weights.Weight(p.Id) == 2), "Every initially unknown pad has weight +2");
        foreach (var pad in route.Take(4)) weights.CheckEmpty(pad.Id);
        check(route.Take(4).All(p => weights.Weight(p.Id) == 0), "Only verified empty pads have zero weight");
        check(weights.ConfirmPickup(route[4].Id, 50), "One confirmed carrot triggers a weight update");
        check(route.Take(5).All(p => weights.Weight(p.Id) == 1) && route.Skip(5).All(p => weights.Weight(p.Id) == 2),
            "User example: after first pickup at #5, #1-4 have +1 and #6-25 have +2");
        check(!weights.ConfirmPickup(route[4].Id, 50) && weights.Pickups == 1, "Duplicate server observations cannot add another pickup");
        weights.CheckEmpty(route[5].Id); weights.ConfirmPickup(route[6].Id, 51);
        check(weights.Weight(route[0].Id) == 2 && weights.Weight(route[5].Id) == 1 && weights.Weight(route[6].Id) == 1,
            "Later pickup increments checked weights, bounded by two, and resets the collected pad first");
        for (var i = 0; i < 20; i++) weights.ConfirmPickup(route[9].Id);
        check(route.All(p => weights.Weight(p.Id) is >= 0 and <= 2), "Weights never accumulate beyond the two-carrot model");
        check(!weights.ConfirmPickup("wrong") && weights.Weight("wrong") is null, "Unknown pads cannot modify carrot weights");
        var session = new SurveySession();
        var chest = new Spot("1252:Bronze:9", 1252, SpotKind.Bronze, 1, Vector3.Zero);
        session.Reset(1252, route.Append(chest)); session.Visit(chest.Id); session.Visit(route[0].Id); session.Skip(route[1].Id);
        check(session.Get(route[1].Id)?.Status == SpotStatus.Skipped, "Verified empty carrots count as skipped patrol stops");
        session.RestartCarrots();
        check(!session.CanPatrol(chest.Id) && route.All(p => session.CanPatrol(p.Id)), "Another carrot round never clears chest patrol history");
        session.Observe([new Observation(100, 2010139, SpotKind.Carrot, route[0].Position, Targetable: false)], DateTimeOffset.UtcNow);
        check(session.Get(route[0].Id)?.Status == SpotStatus.Visible && session.CanDisplay(route[0].Id, PointDisplayMode.Observed), "Untargetable live carrot markers remain visible in search UI");
        weights.Reset(route);
        check(weights.Pickups == 0 && route.All(p => weights.Weight(p.Id) == 2) && weights.ConfirmPickup(route[4].Id, 50), "New session clears weights and pickup deduplication");
        CheckGathering(check, route);
        CheckEmpty(check, route[0]);
    }

    private static void CheckGathering(Action<bool, string> check, IReadOnlyList<Spot> route)
    {
        var pad = route[0]; var player = pad.Position;
        var carrot = new Observation(100, 2010139, SpotKind.Carrot, player, Targetable: false);
        var bunny = new Observation(200, 2012936, SpotKind.RabbitGold, player);
        var gathering = new CarrotGathering(); var calls = 0;
        bool Use(Observation _) { calls++; return true; }
        CarrotGatherUpdate Tick(long now, int? count = 5, Observation[]? observations = null, bool canUse = true, bool casting = false) =>
            gathering.Update(pad, observations ?? [carrot], player, count, canUse, casting, now, Use);
        check(gathering.LiveCarrot(pad, [carrot]) == carrot, "Carrot ground marker need not be targetable to use the inventory item");
        Tick(0, canUse: false); check(calls == 0, "Unsettled or blocked player cannot use a carrot");
        Tick(500, count: null); check(calls == 0, "Unloaded inventory never becomes a zero count or consumption proof");
        Tick(1000); check(calls == 1 && gathering.Busy, "Settled player near a live carrot sends exactly one item use");
        Tick(1500, casting: true); check(calls == 1 && !gathering.PickupConfirmed, "Cast alone is not enough to count pickup");
        check(Tick(2000, count: 4).PickedObject == 100 && gathering.PickupConfirmed, "Inventory decrement confirms exactly one pickup");
        check(Tick(2500, count: 4).PickedObject == 0 && calls == 1, "Consumed item is never used again while waiting for bunny");
        Tick(3000, 4, [bunny]); check(gathering.Bunny == bunny, "New nearby rabbit coffer becomes the patrol interaction target");
        Tick(3500, 4, []); Tick(4000, 4, []);
        check(!Tick(4500, 4, []).Finished, "Unloading without an interaction does not complete a rabbit coffer");
        Tick(5000, 4, [bunny]); gathering.RecordBunnyInteraction(bunny.ObjectId);
        Tick(5500, 4, []); Tick(6000, 4, []);
        check(Tick(6500, 4, []).Finished && !gathering.Busy, "Interacted bunny disappearing across fresh nearby scans completes the stop");
        gathering.Reset(); calls = 0;
        Tick(0); Tick(500, 4); Tick(1000, 4, [bunny]);
        check(!Tick(1500, 4, [bunny with { Available = false }, carrot with { ObjectId = 101 }]).Finished,
            "A second distinct carrot at the same pad is retained after opening the first bunny");
        Tick(2000, 4, [carrot, carrot with { ObjectId = 101 }]);
        check(calls == 2 && Tick(2500, 3, [carrot with { ObjectId = 101 }]).PickedObject == 101, "Same-pad second carrot gets its own consumption proof");
        gathering.Reset(); calls = 0;
        Tick(0);
        for (var now = 500; now <= 15000; now += 500)
        {
            var result = Tick(now);
            if (now == 15000) check(result.Failed && calls == 3, "Rejected item use stops after three spaced attempts without pickup");
        }
        gathering.Reset(); calls = 0;
        check(Tick(0, 0).Failed && calls == 0, "No inventory carrot stops without native item use");
        gathering.Reset(); Tick(0);
        check(Tick(500, 3).Failed, "Unexpected multi-item inventory change is not counted as one pickup");
        gathering.Reset(); Tick(0);
        check(Tick(500, 6).Failed, "Inventory gains cannot become another carrot-use baseline");
        gathering.Reset(); calls = 0; Tick(0);
        for (var now = 500; now <= 6000; now += 500) Tick(now, null);
        check(Tick(6500, 4).PickedObject == 100 && calls == 1, "Late inventory recovery retains pending consumption evidence without a second use");
        foreach (var casting in new[] { false, true })
        {
            gathering.Reset(); calls = 0; Tick(0);
            for (var now = 500; now <= 30000; now += 500)
            {
                var result = Tick(now, casting ? 5 : null, casting: casting);
                if (now == 30000) check(result.Failed && calls == 1, "Unavailable inventory and endless casts have a bounded single-use timeout");
            }
        }
        gathering.Reset(); Tick(0);
        check(Tick(3000, 4).Failed, "A long observation gap does not infer remote consumption");
        gathering.Reset(); Tick(0);
        check(gathering.Update(pad, [], player + new Vector3(100, 0, 0), 4, false, false, 500, Use).Failed, "Leaving pad invalidates pending interaction evidence");
        gathering.Reset(); Tick(0);
        check(Tick(-1, 4).Failed, "Clock rollback cannot validate pending use");
        gathering.Reset(); calls = 0;
        Tick(0, observations: [bunny, carrot]); Tick(500, 4, [bunny]); Tick(1000, 4, [bunny]);
        check(gathering.Bunny is null, "Pre-existing bunny cannot be claimed as the newly used carrot's reward");
        for (var now = 1500; now <= 30500; now += 500)
        {
            var result = Tick(now, 4, [bunny]);
            if (now == 30500) check(result.Failed && calls == 1, "Consumed carrot without new bunny times out without spending again");
        }
        gathering.Reset(); Tick(0); var firstToken = Tick(500, 4).PickupSequence;
        gathering.Reset(); Tick(1000); var secondToken = Tick(1500, 4).PickupSequence;
        check(firstToken != 0 && secondToken > firstToken, "Respawn with recycled object identity gets a fresh pickup event after restart");
    }

    private static void CheckEmpty(Action<bool, string> check, Spot pad)
    {
        var inspector = new CarrotEmptyCheck(); var pos = pad.Position;
        var path = new RouteLeg(pad.Id, pos, [pos], 0);
        bool Tick(long time, IReadOnlyList<Observation>? objects = null, RouteLeg? leg = null, bool ready = true) => inspector.Update(pad, objects ?? [], pos, leg ?? path, ready, time);
        check(!Tick(0) && !Tick(500) && !Tick(1000) && Tick(1500), "Carrot empty pad requires fresh near-ground confirmation over 1.5 seconds");
        inspector.Reset(); Tick(0); Tick(500, ready: false);
        check(!Tick(1000) && !Tick(1500), "Blocked state resets carrot empty confirmation");
        inspector.Reset(); Tick(0);
        check(!Tick(5000) && !Tick(5500), "Scan gap never counts as continuous emptiness");
        foreach (var kind in new[] { SpotKind.Carrot, SpotKind.RabbitGold })
        {
            inspector.Reset();
            var objectAtPad = new Observation(100, 1, kind, pos, Targetable: false);
            check(!Tick(0, [objectAtPad]) && !Tick(2000, [objectAtPad]), "Untargetable carrot or rabbit blocks empty-pad skip");
        }
        foreach (var leg in new[] { path with { Length = 50 }, path with { Length = double.NaN }, path with { DestinationId = "other" } })
        {
            inspector.Reset(); check(!Tick(0, leg: leg) && !Tick(1500, leg: leg), "Obstructed or invalid mesh path cannot prove carrot pad empty");
        }
        check(!inspector.Update(pad, [], pos + new Vector3(0, 8, 0), path, true, 2000), "Different floor is not an empty carrot pad");
        check(!inspector.Update(pad, [], pos, null, true, 2000), "Missing ground route cannot prove carrot absence");
    }
}
