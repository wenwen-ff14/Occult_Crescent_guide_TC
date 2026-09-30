using System.Numerics;
using CrescentCompass.Core;

internal static class ExplorationChecks
{
    internal static void Run(Action<bool, string> check, IReadOnlyList<Spot> exploration)
    {
        check(exploration.All(s => s.LoreId > 0 && s.Id.EndsWith($":{s.LoreId}")), "Exploration uses audited MKDLore row IDs, not object IDs or list indexes");
        var session = new SurveySession { OnlyUnexplored = true };
        var coffer = new Spot("chest", 1252, SpotKind.Silver, 123, new Vector3(100, 0, 100));
        session.Reset(1252, exploration.Append(coffer));
        var first = exploration[0]; var second = exploration[1];
        foreach (var mode in Enum.GetValues<PointDisplayMode>())
            check(exploration.All(s => !session.CanDisplay(s.Id, mode) && !session.CanPatrol(s.Id)), $"Unread exploration is never asserted to be unfinished in {mode}");
        check(session.CanDisplay(coffer.Id, PointDisplayMode.Candidates), "Unread journal does not block chest planning");
        session.OnlyUnexplored = false;
        check(exploration.All(s => session.CanDisplay(s.Id, PointDisplayMode.Visible) && session.Get(s.Id)!.Status == SpotStatus.Location), "All-locations option works before journal state arrives");
        session.OnlyUnexplored = true;
        var snapshot = exploration.ToDictionary(s => s.LoreId, s => s == first);
        check(session.UpdateExploration(snapshot), "Initial game snapshot updates the journal");
        check(session.Exploration.KnownCount == 12 && session.Exploration.CompletedCount == 1, "Journal counts cover exactly the 12 island records");
        check(session.Get(first.Id)!.Status == SpotStatus.Explored && session.Get(second.Id)!.Status == SpotStatus.Unexplored, "Game completion and unfinished status are shown separately");
        foreach (var mode in Enum.GetValues<PointDisplayMode>())
            check(!session.CanDisplay(first.Id, mode) && session.CanDisplay(second.Id, mode), $"Personal unfinished journal is independent from object visibility in {mode}");
        var revision = session.Revision;
        check(!session.UpdateExploration(snapshot) && session.Revision == revision, "Unchanged polling does not invalidate the route");
        session.Visit(second.Id);
        check(session.Exploration.State(second) == ExplorationState.Unexplored && !session.CanPatrol(second.Id), "Manual visit does not unlock a game record");
        session.RestartSurvey();
        check(session.CanPatrol(second.Id) && !session.CanPatrol(first.Id), "Clearing patrol history preserves real game completion");
        snapshot[first.LoreId] = false;
        check(!session.UpdateExploration(snapshot) && !session.CanPatrol(first.Id), "Transient false cannot undo an already observed character unlock");
        check(!session.UpdateExploration(new Dictionary<uint, bool>()) && !session.CanPatrol(first.Id), "A missing update preserves the same character's last known result");

        var route = new List<Spot> { second, coffer };
        snapshot[second.LoreId] = true;
        session.UpdateExploration(snapshot);
        var automation = new RouteAutomation();
        automation.Update(session, route, [], Vector3.Zero, 1000, PointDisplayMode.Visible, true, preservePlannedStops: true);
        check(route.Count == 1 && route[0] == coffer, "Completed exploration leaves a retained route without dropping an unloaded chest");
        check(session.Get(second.Id)!.Status == SpotStatus.Explored, "Game completion is not converted into manual visited state");
        session.OnlyUnexplored = false;
        check(session.CanPatrol(second.Id) && session.CanDisplay(second.Id, PointDisplayMode.Observed), "All-locations switch can show and flag finished notes");
        session.OnlyUnexplored = true;
        session.UpdateExploration(exploration.ToDictionary(s => s.LoreId, _ => true));
        check(exploration.All(s => !session.CanPatrol(s.Id)) && session.Exploration.CompletedCount == 12, "All completed means an empty exploration route, not hidden chest points");
        session.Reset(1252, exploration);
        check(session.Exploration.KnownCount == 0 && exploration.All(s => !session.CanPatrol(s.Id)), "Session or character reset clears personal completion and waits for fresh data");
        session.UpdateExploration(exploration.ToDictionary(s => s.LoreId, _ => false));
        check(session.Exploration.CompletedCount == 0 && exploration.All(s => session.CanPatrol(s.Id)), "Another character can have all notes unfinished");
        session.Reset(1346, exploration);
        check(session.Snapshot().Count == 0 && session.Exploration.KnownCount == 0, "No southern progress leaks into another region");
    }
}
