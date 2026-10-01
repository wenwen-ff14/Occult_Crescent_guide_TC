using System.Diagnostics;
using System.Numerics;
using CrescentCompass.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
}
Spot Point(int id, float x, float y = 0, float z = 0, SpotKind kind = SpotKind.Silver) =>
    new(id.ToString(), 1252, kind, (uint)id, new Vector3(x, y, z));

double BruteForce(Vector3 start, IReadOnlyList<Spot> points)
{
    if (points.Count == 0) return 0;
    return points.Min(p => Vector3.Distance(start, p.Position) + BruteForce(p.Position, points.Where(q => q.Id != p.Id).ToArray()));
}

Check(RoutePlanner.Plan(Vector3.Zero, []).Length == 0, "Empty route");
var singleton = RoutePlanner.Plan(new Vector3(2, 0, 0), [Point(1, 7)]);
Check(singleton.Exact && singleton.Length == 5 && singleton.Stops.Count == 1, "Single destination");
var line = RoutePlanner.Plan(Vector3.Zero, [Point(1, 3), Point(2, 1), Point(3, 2)]);
Check(line.Length == 3 && line.Stops.Select(p => p.Id).SequenceEqual(new[] { "2", "3", "1" }), "Open route does not return to origin");

var random = new Random(431);
for (var trial = 0; trial < 20; trial++)
{
    var points = Enumerable.Range(0, 7).Select(i => Point(i, random.Next(-30, 30), random.Next(-5, 5), random.Next(-30, 30))).ToArray();
    var start = new Vector3(7, 2, -9);
    var plan = RoutePlanner.Plan(start, points);
    Check(Math.Abs(plan.Length - BruteForce(start, points)) < 0.0001, $"Exact solver equals exhaustive permutation: {trial}");
    Check(plan.Stops.Select(p => p.Id).Distinct().Count() == points.Length, "Route includes every stop once");
}

var many = Enumerable.Range(0, 120).Select(i => Point(i, random.Next(-950, 950), random.Next(0, 210), random.Next(-950, 950))).ToArray();
var watch = Stopwatch.StartNew();
var large = RoutePlanner.Plan(Vector3.Zero, many);
watch.Stop();
Check(!large.Exact && large.Stops.Count == many.Length, "Large route uses bounded heuristic");
Check(large.Stops.Select(p => p.Id).Distinct().Count() == many.Length, "Heuristic preserves destinations");
var nearestCost = 0d;
var cursor = Vector3.Zero;
var remaining = many.ToList();
while (remaining.Count > 0)
{
    var next = remaining.MinBy(p => Vector3.DistanceSquared(cursor, p.Position))!;
    nearestCost += Vector3.Distance(cursor, next.Position);
    cursor = next.Position;
    remaining.Remove(next);
}
Check(large.Length <= nearestCost + 0.001, "Optimization never worsens nearest-neighbour baseline");
Check(large.Stops.Select(p => p.Id).SequenceEqual(RoutePlanner.Plan(Vector3.Zero, many.Reverse()).Stops.Select(p => p.Id)), "Deterministic under input reordering");
Check(RoutePlanner.Plan(Vector3.Zero, [Point(1, 1), Point(1, 1)]).Stops.Count == 1, "Repeated IDs deduplicated");
Check(RoutePlanner.Plan(Vector3.Zero, [Point(1, 1), Point(2, 1)]).Stops.Count == 2, "Coincident distinct points preserved");
Check(RoutePlanner.Plan(Vector3.Zero, [Point(1, 0, 20)]).Length == 20, "Vertical distance included");
try { RoutePlanner.Plan(Vector3.Zero, [Point(1, float.NaN)]); Check(false, "Must reject NaN"); } catch (ArgumentException) { passed++; }
try { RoutePlanner.Plan(Vector3.Zero, [Point(1, 0), Point(2, 0) with { Territory = 1346 }]); Check(false, "Must reject cross-zone route"); } catch (ArgumentException) { passed++; }
Check(Math.Abs(Coordinates.ToMap(0, 100, 0) - 21.48f) < 0.0001, "Map centre at scale 100");
Check(Math.Abs(Coordinates.ToMap(0, 200, 0) - 11.24f) < 0.0001, "Map centre at scale 200");
Check(Math.Abs(Coordinates.ToMap(100, 100, -100) - 21.48f) < 0.0001, "Map offsets applied");

var now = DateTimeOffset.UtcNow;
var pad = Point(1, 10, 0, 10, SpotKind.Carrot);
var upper = Point(2, 10, 30, 10, SpotKind.Carrot);
var session = new SurveySession();
session.Reset(1252, [pad, upper]);
Check(session.Get(pad.Id)!.Status == SpotStatus.Candidate, "Catalog is not a live spawn assertion");
session.Observe([new Observation(100, 2010139, SpotKind.Carrot, pad.Position)], now);
Check(session.Get(pad.Id)!.Status == SpotStatus.Visible, "Nearby carrot detected");
Check(session.Get(upper.Id)!.Status == SpotStatus.Candidate, "Different floor not merged");
session.Observe([], now.AddSeconds(1));
Check(session.Get(pad.Id)!.Status == SpotStatus.LastSeen, "Unloaded carrot remains uncertain, not collected");
session.Visit(pad.Id);
session.Observe([new Observation(100, 2010139, SpotKind.Carrot, pad.Position)], now.AddSeconds(2));
Check(session.Get(pad.Id)!.Status == SpotStatus.Visited, "Same object does not undo manual completion");
session.Observe([new Observation(101, 2010139, SpotKind.Carrot, pad.Position)], now.AddSeconds(3));
Check(session.Get(pad.Id)!.Status == SpotStatus.Visible, "New object at a visited pad reopens it");
session.Reset(1252, [pad, upper]);
Check(session.Get(pad.Id)!.Status == SpotStatus.Candidate && session.Get(pad.Id)!.LastSeen is null, "Same-territory instance reset clears sightings");
session.Observe([new Observation(200, 999, SpotKind.Silver, new Vector3(90, 0, 90))], now);
Check(session.Snapshot().Count == 3, "Unknown valid point appears automatically");
session.Observe([new Observation(200, 999, SpotKind.Silver, new Vector3(90, 0, 90), false)], now);
Check(session.Snapshot().Single(s => s.Spot.Kind == SpotKind.Silver).Status == SpotStatus.Visited, "Opened coffer is marked visited");
session.Observe([new Observation(200, 999, SpotKind.Silver, new Vector3(150, 0, 150))], now);
Check(session.Snapshot().Count(s => s.Spot.Kind == SpotKind.Silver) == 2, "Recycled object ID at a different location does not overwrite old pad");
session.Observe([new Observation(301, 2010139, SpotKind.Carrot, pad.Position + Vector3.UnitX)], now);
Check(session.Get(pad.Id)!.Spot.Position == pad.Position + Vector3.UnitX, "Live observation refines candidate coordinates");
var beforeInvalid = session.Snapshot().Count;
session.Observe([new Observation(302, 2010139, SpotKind.Carrot, new Vector3(float.PositiveInfinity, 0, 0))], now);
Check(session.Snapshot().Count == beforeInvalid, "Non-finite observation ignored");
session.Reset(0, [pad]);
session.Observe([new Observation(200, 999, SpotKind.Silver, new Vector3(90, 0, 90))], now);
Check(session.Snapshot().Count == 0, "No scanning outside supported zones");

var currentOnly = new SurveySession();
var currentChest = Point(500, 20);
currentOnly.Reset(1252, [currentChest]);
Check(!currentOnly.CanDisplay(currentChest.Id, true) && currentOnly.CanDisplay(currentChest.Id, false), "Candidate is hidden in current-only mode, retained for optional survey");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position, Targetable: false)], now);
Check(!currentOnly.CanDisplay(currentChest.Id, true) && currentOnly.Get(currentChest.Id)!.Status != SpotStatus.Visited, "Loaded but untargetable chest is hidden without being marked collected");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position)], now);
Check(currentOnly.CanDisplay(currentChest.Id, true), "Loaded targetable unopened chest appears");
var currentRoute = RoutePlanner.Plan(Vector3.Zero, currentOnly.Snapshot().Where(s => currentOnly.CanDisplay(s.Spot.Id, true)).Select(s => s.Spot)).Stops.ToList();
currentOnly.Observe([], now.AddSeconds(1));
currentRoute.RemoveAll(s => !currentOnly.CanDisplay(s.Id, true));
Check(currentRoute.Count == 0 && currentOnly.Get(currentChest.Id)!.Status == SpotStatus.LastSeen, "Unloaded stop leaves current-only route, never counts as completed");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position)], now.AddSeconds(2));
Check(currentOnly.CanDisplay(currentChest.Id, true), "Returning still-unopened chest reappears");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position, Available: false)], now.AddSeconds(3));
Check(!currentOnly.CanDisplay(currentChest.Id, true) && currentOnly.Get(currentChest.Id)!.Status == SpotStatus.Visited, "Opened flag overrides targetable and removes chest");
Check(!currentOnly.CanDisplay("missing", true) && !currentOnly.CanDisplay("missing", false), "Stale/unknown flag target rejected");

var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*_locations.json", SearchOption.AllDirectories);
Check(files.Length == 6, "Four field snapshots, tower and exploration snapshots packaged");
var catalog = new List<Spot>();
foreach (var file in files)
{
    using var stream = File.OpenRead(file);
    var spots = SpotCatalog.Load(stream);
    Check(spots.Count > 0, $"Catalog parses: {Path.GetFileName(file)}");
    catalog.AddRange(spots);
}
Check(catalog.Select(s => s.Id).Distinct().Count() == catalog.Count, "Global catalog IDs unique");
foreach (var territory in new ushort[] { 1252, 1346 })
{
    var spots = catalog.Where(s => s.Territory == territory).ToArray();
    Check(spots.Count(s => s.Kind == SpotKind.Bronze) == 60 && spots.Count(s => s.Kind == SpotKind.Silver) == 8, "Each field catalog has 60 bronze and 8 silver");
    Check(spots.Where(s => s.Kind is SpotKind.Bronze or SpotKind.Silver).Select(s => s.DataId).Distinct().Count() == 68, "Field DataIds unique");
    Check(spots.Select(s => s.Position).Distinct().Count() == spots.Length, "Distinct static placements");
    var plan = RoutePlanner.Plan(Vector3.Zero, spots);
    Check(plan.Stops.Count == spots.Length && double.IsFinite(plan.Length), "Full real catalog routes successfully");
    Console.WriteLine($"Territory {territory}: {spots.Count(s => s.Kind == SpotKind.Carrot)} carrots, {spots.Count(s => s.Kind == SpotKind.Silver)} silver, {spots.Count(s => s.Kind == SpotKind.Bronze)} bronze.");
}
Check(catalog.Count(s => s.Kind == SpotKind.Tower) == 14 && catalog.Where(s => s.Kind == SpotKind.Tower).All(s => s.Territory == 1252), "Only locally audited south tower candidates");

var exploration = catalog.Where(s => s.Kind == SpotKind.Exploration).ToArray();
Check(exploration.Length == 12 && exploration.All(s => s.Territory == 1252 && s.MapId == 967 && s.Name is not null), "Twelve verified outdoor island record locations with explicit map and labels");
Check(exploration.Select(s => int.Parse(s.Id.Split(':')[2])).Order().SequenceEqual(new[] { 5, 7, 9, 12, 13, 15, 18, 19, 21, 22, 23, 24 }), "Only outdoor island survey locations, excluding tower, Phantom Village and non-location records");
Check(exploration.All(s => !s.RequiresTower && s.Id != "1252:Exploration:30"), "Tower library is excluded from the exploration catalog used by lists, flags and routing");
session.Reset(1252, exploration);
Check(exploration.All(s => session.CanDisplay(s.Id, PointDisplayMode.Visible) && session.CanDisplay(s.Id, PointDisplayMode.Observed) && session.Get(s.Id)!.Status == SpotStatus.Location), "Exploration independent from nearby-object display filter and never presented as spawned treasure");
session.Visit(exploration[0].Id);
Check(!session.CanDisplay(exploration[0].Id, PointDisplayMode.Observed), "Manual exploration visit leaves route");
session.RestartSurvey();
Check(session.CanDisplay(exploration[0].Id, PointDisplayMode.Observed), "Exploration manual progress can reset");
session.Reset(1346, exploration);
Check(session.Snapshot().Count == 0, "No south exploration markers in north or other zones");

currentOnly.Reset(1252, [currentChest]);
Check(!currentOnly.CanDisplay(currentChest.Id, PointDisplayMode.Observed), "Whole-island known mode does not expose unobserved static candidates");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position, Targetable: false)], now);
Check(!currentOnly.CanDisplay(currentChest.Id, PointDisplayMode.Observed), "Never-interactable chest does not enter known-target mode");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position)], now);
currentOnly.Observe([], now.AddSeconds(1));
Check(currentOnly.CanDisplay(currentChest.Id, PointDisplayMode.Observed) && currentOnly.Get(currentChest.Id)!.Status == SpotStatus.LastSeen, "Whole-island known position survives render distance and remains uncertain");
currentOnly.Observe([new Observation(700, 500, SpotKind.Silver, currentChest.Position, Available: false)], now.AddSeconds(2));
Check(!currentOnly.CanDisplay(currentChest.Id, PointDisplayMode.Observed), "Observed opened chest leaves island known route");
currentOnly.Reset(1252, [currentChest]);
Check(!currentOnly.CanDisplay(currentChest.Id, PointDisplayMode.Observed), "New instance clears remembered targets");

var counts = TreasureSurvey.Parse("在當前區域中感知到了2個銀寶箱、10個銅寶箱……！", now);
Check(counts is { Silver: 2, Bronze: 10 } && counts.CapturedAt == now, "TC personal treasure survey records count snapshot and timestamp");
Check(TreasureSurvey.Parse("在當前區域中感知到了２個銀寶箱、１０個銅寶箱……！", now) is { Silver: 2, Bronze: 10 }, "Fullwidth numerals normalized");
Check(TreasureSurvey.Parse("目前區域現在似乎沒有寶箱……", now) is { Silver: 0, Bronze: 0 }, "No-treasure system message supplies explicit zero");
Check(TreasureSurvey.Parse("在當前區域中感知到了0個銀寶箱、0個銅寶箱……！", now) is { Silver: 0, Bronze: 0 }, "Explicit zero count survey parsed");
Check(TreasureSurvey.Parse("玩家說：在當前區域中感知到了2個銀寶箱、10個銅寶箱……！", now) is null, "Player quotation is not an anchored system template");
Check(TreasureSurvey.Parse("在當前區域中感知到了-1個銀寶箱、10個銅寶箱……！", now) is null && TreasureSurvey.Parse("在當前區域中感知到了999999999999999個銀寶箱、10個銅寶箱……！", now) is null, "Invalid or overflowing counts rejected");
Check(TreasureSurvey.Parse("", now) is null, "Unknown survey is not zero");

var potFiles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "pot_candidates.json", SearchOption.AllDirectories);
Check(potFiles.Length == 2, "Both pot catalogs packaged");
var potCatalog = potFiles.SelectMany(file => { using var stream = File.OpenRead(file); return PotCatalog.Load(stream); }).ToArray();
Check(potCatalog.Count(c => c.Territory == 1252) == 80 && potCatalog.Count(c => c.Territory == 1252 && c.Bonus) == 20, "South pot catalog: 60 primary + 20 bonus");
Check(potCatalog.Count(c => c.Territory == 1346) == 83 && potCatalog.Count(c => c.Territory == 1346 && c.Bonus) == 21, "North source: 62 primary + 21 bonus entries");
Check(potCatalog.Where(c => c.Territory == 1346).Select(c => c.Position).Distinct().Count() == 82, "North primary/bonus shared pad retained with both memberships");

Check(CofferKinds.EventObject(2014741) == SpotKind.PotGold && CofferKinds.EventObject(2014742) == SpotKind.PotSilver && CofferKinds.EventObject(2014743) == SpotKind.PotBronze, "All three pot event coffer grades");
Check(CofferKinds.EventObject(2012936) == SpotKind.RabbitGold && CofferKinds.EventObject(999) is null, "Rabbit coffer distinct from unrelated events");
Check(CofferKinds.TreasureModel(1598) == SpotKind.Gold && CofferKinds.TreasureModel(999) == SpotKind.Other, "Gold and future Treasure models not silently omitted");
PotLocalizedHintChecks.Run(Check);
foreach (var direction in Enumerable.Range(1, 8))
{
    var parsed = PotHints.Parse($"財寶好像是在{PotSession.DirectionName(direction)}方向不遠的地方！");
    Check(parsed?.Direction == direction && parsed.Distance == "不遠", "TC eight-direction template");
    var radians = (direction - 1) * MathF.PI / 4;
    var dest = new Vector3(MathF.Sin(radians) * 100, 200, -MathF.Cos(radians) * 100);
    Check(PotSession.MatchesDirection(Vector3.Zero, dest, direction), "World -Z is north; bearing ignores vertical height");
    Check(!PotSession.MatchesDirection(Vector3.Zero, -dest, direction), "Opposite bearing excluded");
}
foreach (var distance in new[] { "很近", "不遠", "稍遠", "很遠" })
    Check(PotHints.Parse($"財寶好像是在「西北」方向{distance}的地方！")?.Distance == distance, "All TC distance texts parsed without invented ranges");
Check(PotHints.Parse("財寶好像是在未知方向不遠的地方！") is null && PotHints.Parse("玩家說：財寶好像是在北方向不遠的地方！") is null, "Malformed/prefixed text rejected");
Check(PotHints.Parse("發現了財寶！！")?.Kind == PotHintKind.Reveal && PotHints.Parse("似乎能夠告知第二處財寶所在地！")?.Kind == PotHintKind.Bonus, "Reveal and second-chance messages");
Check(PotHints.Parse("時間過了太久，已經發現的財寶消失了。")?.Kind == PotHintKind.Expired, "Expiration message");

var pot = new PotSession();
PotCandidate Pad(string id, float x, float z, bool bonus = false) => new(id, 1252, bonus ? 0 : 1976, bonus, new Vector3(x, 0, z));
var northPad = Pad("north", 0, -100); var eastPad = Pad("east", 100, 0); var bonusPad = Pad("bonus", 100, -100, true);
PotCandidate[] pads = [northPad, eastPad, bonusPad, Pad("shared-bonus", 0, -100, true)];
Check(!pot.UpdateBuff(true, 1, Vector3.Zero, pads, []) && !pot.Active, "No pot acquisition outside supported zones");
Check(pot.UpdateBuff(true, 1252, Vector3.Zero, pads, []) && pot.Active && pot.Target is not null && !pot.Revealed, "Acquisition schedules a candidate, never a confirmed chest");
Check(pot.Candidates.Count == 3, "Duplicate memberships not duplicate map targets");
Check(pot.Searching && pot.Target is not null, "Pot search exposes its candidate independently of the ordinary chest display mode");
var revision = pot.Revision;
Check(!pot.UpdateBuff(true, 1252, new Vector3(200, 0, 100), pads, []) && pot.Revision == revision, "Polling/moving does not repeat acquisition or flags");
pot.Apply(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, now);
Check(pot.Candidates.Count == 1 && pot.Target == northPad.Position && !pot.Revealed, "Hint narrows from the captured origin; distance bucket does not discard a pad");
pot.Apply(new(PotHintKind.Direction, 5, "很近"), Vector3.Zero, now);
Check(pot.Target is null && pot.Candidates.Count == 1, "Conflicting hint invalidates target without false fallback flag");
pot.Restart(Vector3.Zero);
pot.Apply(new(PotHintKind.Bonus), Vector3.Zero, now);
Check(pot.Candidates.Count == 2 && pot.Candidates.All(c => c.Bonus), "Bonus replaces primary pool, keeps shared bonus pad");
pot.Apply(new(PotHintKind.Expired), Vector3.Zero, now);
Check(pot.Target is null && !pot.Revealed, "Expiration clears target");
pot.UpdateBuff(false, 1252, Vector3.Zero, pads, []);
Check(!pot.Active && pot.Target is null && pot.Candidates.Count == 0, "Buff loss resets search");
var oldCoffer = new Observation(91, 2014741, SpotKind.PotGold, new Vector3(2, 0, 0));
var newCoffer = new Observation(92, 2014743, SpotKind.PotBronze, new Vector3(8, 0, 0));
pot.UpdateBuff(true, 1252, Vector3.Zero, pads, [oldCoffer]);
Check(!pot.ObserveReveal([newCoffer], now), "No automatic chest claim without a personal reveal message");
pot.Apply(new(PotHintKind.Reveal), Vector3.Zero, now);
Check(!pot.ObserveReveal([oldCoffer], now), "Ignore coffer already present before acquisition");
Check(!pot.ObserveReveal([newCoffer, newCoffer with { ObjectId = 93 }], now), "Ambiguous nearby coffers not guessed");
Check(!pot.ObserveReveal([newCoffer with { Available = false }], now), "Uninteractable coffer excluded");
Check(!pot.ObserveReveal([newCoffer with { Targetable = false }], now), "Untargetable unopened pot coffer cannot be confirmed");
Check(!pot.ObserveReveal([newCoffer with { Position = new Vector3(100, 0, 0) }], now), "Distant coffer excluded");
Check(pot.ObserveReveal([newCoffer, oldCoffer], now.AddSeconds(1)) && pot.Revealed && pot.Target == newCoffer.Position, "Unambiguous nearby new coffer gets actual observed coordinates");
Check(pot.Target == newCoffer.Position, "Confirmed pot replaces candidate with actual coordinates");
revision = pot.Revision;
Check(!pot.ObserveReveal([newCoffer], now.AddSeconds(2)) && pot.Revision == revision, "Revealed coffer does not spam flag revisions");
Check(pot.ObserveReveal([], now.AddSeconds(3)) && pot.Target is null && !pot.Revealed, "Unloaded/collected event chest no longer advertised as currently present");
Check(pot.Target is null, "Unloaded pot no longer supplies a flag or overlay target");
pot.Apply(new(PotHintKind.Reveal), Vector3.Zero, now);
Check(!pot.ObserveReveal([newCoffer with { ObjectId = 99 }], now.AddSeconds(9)), "Late unrelated coffer excluded");
pot.Reset();
Check(!pot.Apply(new(PotHintKind.Reveal), Vector3.Zero, now) && !pot.Active, "Reset disarms queued hints");
pot.UpdateBuff(true, 1346, Vector3.Zero, potCatalog, []);
Check(pot.Candidates.Count == 82 && pot.Candidates.All(c => c.Territory == 1346), "New territory cannot retain south pot pads");
RouteAutomationChecks.Run(Check);
ExplorationChecks.Run(Check, exploration);
PlayerVisibilityChecks.Run(Check);
PotAutomationChecks.Run(Check);
PotFateChecks.Run(Check);
CeCooldownChecks.Run(Check);
await WalkingRouteChecks.Run(Check);
PatrolContextChecks.Run(Check);
await DirectedRouteChecks.Run(Check);
await ChestChartChecks.Run(Check);
Console.WriteLine($"PASS: {passed} checks. 120-stop route computed in {watch.ElapsedMilliseconds} ms.");
