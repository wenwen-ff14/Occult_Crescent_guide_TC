using System.Numerics;

namespace CrescentCompass.Core;

public enum SpotKind { Carrot, Bronze, Silver, PotBronze, PotSilver, PotGold, RabbitGold, Tower, Other, Gold, Exploration }
public enum SpotStatus { Candidate, Visible, LastSeen, Visited, Location, Skipped, Unexplored, Explored }
public enum PointDisplayMode { Observed, Visible, Candidates }

public sealed record Spot(string Id, ushort Territory, SpotKind Kind, uint DataId, Vector3 Position, string? Name = null, uint MapId = 0, bool RequiresTower = false, uint LoreId = 0);
// Available=false is positive evidence that the chest is spent. Targetability alone does not prove collection.
public sealed record Observation(ulong ObjectId, uint DataId, SpotKind Kind, Vector3 Position, bool Available = true, bool Targetable = true, bool Opening = false);
public sealed record TrackedSpot(Spot Spot, SpotStatus Status, DateTimeOffset? LastSeen);

public static class CofferKinds
{
    public static bool IsCoffer(SpotKind kind) => kind is not (SpotKind.Carrot or SpotKind.Exploration);
    public static SpotKind TreasureModel(uint model) => model switch
    { 1596 => SpotKind.Bronze, 1597 => SpotKind.Silver, 1598 => SpotKind.Gold, _ => SpotKind.Other };
    public static SpotKind? EventObject(uint dataId) => dataId switch
    {
        2010139 => SpotKind.Carrot,
        2012936 => SpotKind.RabbitGold,
        2014741 => SpotKind.PotGold,
        2014742 => SpotKind.PotSilver,
        2014743 => SpotKind.PotBronze,
        _ => null,
    };
    public static bool IsPot(SpotKind kind) => kind is SpotKind.PotBronze or SpotKind.PotSilver or SpotKind.PotGold;
}

public static class Coordinates
{
    public static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    public static float ToMap(float world, ushort sizeFactor, short offset)
    {
        if (sizeFactor == 0) throw new ArgumentOutOfRangeException(nameof(sizeFactor));
        return (world + offset) / 50f + 1f + 2048f / sizeFactor;
    }
}
