using System.Numerics;
using System.Text.Json;

namespace CrescentCompass.Core;

public sealed record PotCandidate(string Id, ushort Territory, int Fate, bool Bonus, Vector3 Position);

public static class PotCatalog
{
    public static IReadOnlyList<PotCandidate> Load(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var territory = root.GetProperty("territoryId").GetUInt16();
        if (!SpotCatalog.IsSupported(territory)) throw new InvalidDataException("Unsupported pot territory.");
        var result = new List<PotCandidate>();
        var ids = new HashSet<string>();
        foreach (var row in root.GetProperty("candidates").EnumerateArray())
        {
            var id = $"{territory}:pot:{row.GetProperty("id").GetInt32()}";
            var position = new Vector3(row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(), row.GetProperty("z").GetSingle());
            if (!Coordinates.IsFinite(position) || !ids.Add(id)) throw new InvalidDataException("Invalid pot candidate.");
            result.Add(new(id, territory, row.GetProperty("fateId").GetInt32(), row.GetProperty("bonus").GetBoolean(), position));
        }
        return result;
    }
}
