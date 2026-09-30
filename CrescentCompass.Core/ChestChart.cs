namespace CrescentCompass.Core;

/// <summary>The user's South Horn chart numbering, independent of catalog IDs and route position.</summary>
public static class ChestChart
{
    public const string Id = "south-horn-68-v1";
    public const ushort Territory = 1252;
    public const int Count = 68;
    // Map: Spiral Lance (Coeurl). Pathing: Selene Amaris (Omega) and Spiral Lance (Coeurl).
    private static readonly int[] CatalogIds =
    [
        9, 10, 14, 19, 21, 22, 24, 51, 52, 6, 54, 53, 23, 20, 68, 26, 13, 11, 12, 15, 16, 17, 18, 1, 48, 49, 50, 65, 4, 42, 41, 37, 38, 47, 43, 36, 35, 40, 39, 5, 28, 66, 2, 44, 59, 62, 64, 63, 8, 61, 60, 46, 45, 67, 27, 25, 30, 29, 31, 32, 3, 33, 34, 56, 57, 7, 58, 55,
    ];
    private static readonly string[] SpotIds = CatalogIds.Select(id => $"1252:{(id <= 8 ? SpotKind.Silver : SpotKind.Bronze)}:{id}").ToArray();
    private static readonly Dictionary<string, int> Numbers = SpotIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index + 1);
    public static int? Number(Spot? spot) => spot is not null && spot.Territory == Territory && Numbers.TryGetValue(spot.Id, out var n) ? n : null;
    public static int Next(int number) => number is >= 1 and < Count ? number + 1 : 1;
    public static IReadOnlyList<Spot> Order(IEnumerable<Spot> catalog, int start)
    {
        if (start is < 1 or > Count) throw new ArgumentOutOfRangeException(nameof(start));
        var byId = catalog.Where(s => s.Territory == Territory).ToDictionary(s => s.Id);
        var result = new List<Spot>(Count);
        for (var offset = 0; offset < Count; offset++)
        {
            var id = SpotIds[(start - 1 + offset) % Count];
            if (!byId.TryGetValue(id, out var spot) || spot.RequiresTower || spot.Kind is not (SpotKind.Bronze or SpotKind.Silver))
                throw new InvalidDataException($"Missing or invalid chart point: {id}");
            result.Add(spot);
        }
        return result;
    }
}
