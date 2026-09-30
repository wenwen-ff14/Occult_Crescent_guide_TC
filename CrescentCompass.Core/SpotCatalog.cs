using System.Numerics;
using System.Text.Json;

namespace CrescentCompass.Core;

public static class SpotCatalog
{
    public static bool IsSupported(ushort territory) => territory is 1252 or 1346;

    public static IReadOnlyList<Spot> Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var territory = root.GetProperty("territoryId").GetUInt16();
        if (!IsSupported(territory)) throw new InvalidDataException("Unsupported territory in catalog.");
        var carrots = root.TryGetProperty("carrots", out var rows);
        var explorations = root.TryGetProperty("explorations", out var explorationRows);
        if (explorations) rows = explorationRows;
        else if (!carrots) rows = root.GetProperty("treasures");
        var result = new List<Spot>();
        var ids = new HashSet<string>();
        foreach (var row in rows.EnumerateArray())
        {
            var kind = explorations ? SpotKind.Exploration : carrots ? SpotKind.Carrot : row.TryGetProperty("category", out var category) && category.GetString() == "tower" ? SpotKind.Tower : row.GetProperty("sgbId").GetUInt32() switch
            {
                1596 => SpotKind.Bronze,
                1597 => SpotKind.Silver,
                _ => throw new InvalidDataException("Unknown coffer model in catalog."),
            };
            var position = new Vector3(row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(), row.GetProperty("z").GetSingle());
            if (!Coordinates.IsFinite(position)) throw new InvalidDataException("Non-finite catalog coordinate.");
            var id = $"{territory}:{kind}:{row.GetProperty("id").GetInt32()}";
            if (!ids.Add(id)) throw new InvalidDataException("Duplicate catalog ID.");
            if (explorations)
            {
                var name = row.GetProperty("name").GetString();
                var mapId = row.GetProperty("mapId").GetUInt32();
                if (string.IsNullOrWhiteSpace(name) || mapId == 0) throw new InvalidDataException("Exploration location requires a name and map.");
                result.Add(new Spot(id, territory, kind, 0, position, $"{row.GetProperty("id").GetInt32():00} · {name}", mapId,
                    row.TryGetProperty("requiresTower", out var tower) && tower.GetBoolean(), row.GetProperty("id").GetUInt32()));
            }
            else result.Add(new Spot(id, territory, kind, carrots ? 2010139u : row.GetProperty("dataId").GetUInt32(), position));
        }
        return result;
    }
}
