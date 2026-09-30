namespace CrescentCompass.Core;

public enum ExplorationState { Unknown, Unexplored, Explored }

// Character unlocks are independent of manual patrol history. Only a successful game query supplies a value.
public sealed class ExplorationProgress
{
    private readonly Dictionary<uint, bool> states = [];
    public int KnownCount => states.Count;
    public int CompletedCount => states.Values.Count(v => v);
    public ExplorationState State(Spot spot) => spot.Kind != SpotKind.Exploration || spot.LoreId == 0 || !states.TryGetValue(spot.LoreId, out var complete)
        ? ExplorationState.Unknown : complete ? ExplorationState.Explored : ExplorationState.Unexplored;
    public bool Allows(Spot spot, bool onlyUnexplored) => spot.Kind != SpotKind.Exploration || !onlyUnexplored || State(spot) == ExplorationState.Unexplored;

    public bool Update(IReadOnlyDictionary<uint, bool> snapshot)
    {
        var changed = false;
        foreach (var (id, complete) in snapshot)
        {
            if (id == 0) continue;
            // Unlocks do not regress within a character session; a transient false must not re-advertise a finished note.
            if (states.TryGetValue(id, out var previous) && (previous || previous == complete)) continue;
            states[id] = complete;
            changed = true;
        }
        return changed;
    }

    public void Reset() => states.Clear();
}
