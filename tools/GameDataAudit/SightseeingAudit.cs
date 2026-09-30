using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System.Text.Json;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;

internal static class SightseeingAudit
{
    internal static void Run(string path)
    {
        using var game = new GameData(path, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var rows = game.GetExcelSheet<Adventure>()!;
        Console.WriteLine($"ADVENTURE COUNT {rows.Count}");
        var points = new List<object>();
        var territories = new Dictionary<uint, int>();
        var levels = game.GetExcelSheet<Level>()!;
        var maps = game.GetExcelSheet<Map>()!;
        foreach (var row in rows)
        {
            if (!levels.TryGetRow(row.Level.RowId, out var level) || !maps.TryGetRow(level.Map.RowId, out var map))
                throw new InvalidDataException($"Adventure {row.RowId}: invalid level/map reference.");
            territories[level.Territory.RowId] = territories.GetValueOrDefault(level.Territory.RowId) + 1;
            points.Add(new { id = row.RowId, name = row.Name.ToString(), territory = level.Territory.RowId, map = level.Map.RowId,
                x = level.X, y = level.Y, z = level.Z, emote = row.Emote.Value.Name.ToString(), minTime = row.MinTime, maxTime = row.MaxTime,
                mapTerritory = map.TerritoryType.RowId });
        }
        Directory.CreateDirectory(".references");
        File.WriteAllText(".references/sightseeing-audit.json", JsonSerializer.Serialize(points, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"VALID {points.Count} entries / {territories.Count} territories; South Horn={territories.GetValueOrDefault(1252u)}, North Horn={territories.GetValueOrDefault(1346u)}");
        foreach (var pair in territories.Take(5)) Console.WriteLine($"TERRITORY {pair.Key}: {pair.Value}");
        foreach (var type in typeof(Adventure).Assembly.GetTypes().Where(t => t.Namespace == "Lumina.Excel.Sheets" && (t.Name.Contains("Mkd", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Occult", StringComparison.OrdinalIgnoreCase))))
            Console.WriteLine($"RELATED SHEET {type.Name}: {string.Join(", ", type.GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}"))}");
        var lorePoints = new List<object>();
        var scenePoints = new Dictionary<uint, (uint Territory, uint Map, float X, float Y, float Z)>();
        foreach (var id in new uint[] { 1252 })
        {
            if (!game.GetExcelSheet<TerritoryType>()!.TryGetRow(id, out var territory)) continue;
            var bg = territory.Bg.ToString();
            var directory = "bg/" + bg[..(bg.LastIndexOf('/') + 1)];
            foreach (var file in new[] { "bg.lgb", "planmap.lgb", "planevent.lgb", "planlive.lgb" })
            {
                var scene = game.GetFile<LgbFile>(directory + file);
                if (scene is null) continue;
                foreach (var obj in scene.Layers.SelectMany(layer => layer.InstanceObjects))
                {
                    if (obj.Object is not LayerCommon.EventInstanceObject) continue;
                    var p = obj.Transform.Translation;
                    scenePoints[obj.InstanceId] = (id, territory.Map.RowId, p.X, p.Y, p.Z);
                }
            }
        }
        foreach (var lore in game.GetExcelSheet<MKDLore>()!)
        {
            if (lore.Unknown4 == 0) continue;
            if (!scenePoints.TryGetValue(lore.Unknown4, out var point)) { Console.WriteLine($"LORE EXCLUDED {lore.RowId}: {lore.Name}, no matching island scene instance {lore.Unknown4}"); continue; }
            Console.WriteLine($"LOREPOINT {lore.RowId}: {lore.Name} territory={point.Territory} map={point.Map} XYZ={point.X},{point.Y},{point.Z}");
            Console.WriteLine($"LOREUNLOCK {lore.RowId}: TC MKDLore.Unknown2={lore.Unknown2}");
            lorePoints.Add(new { id = lore.RowId, name = lore.Name.ToString(), mapId = point.Map, instanceId = lore.Unknown4, requiresTower = lore.RowId == 30, x = point.X, y = point.Y, z = point.Z });
        }
        var versionPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, "ffxivgame.ver");
        var version = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "version unavailable";
        File.WriteAllText(".references/southhorn-exploration.json", JsonSerializer.Serialize(new { schemaVersion = 1, territoryId = 1252,
            source = $"TC client MKDLore.Unknown4 -> LGB EventObject.InstanceId; {version}", explorations = lorePoints }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var id in new uint[] { 10965, 10966 })
        {
            var log = game.GetExcelSheet<LogMessage>()!.GetRow(id);
            Console.WriteLine($"SURVEY {id} channel={log.LogKind.RowId}: {log.Text}");
        }
        foreach (var obj in game.GetExcelSheet<EObjName>()!.Where(o => o.RowId is >= 2014600 and <= 2014800))
            if (!string.IsNullOrWhiteSpace(obj.Singular.ToString())) Console.WriteLine($"EOBJ {obj.RowId}: {obj.Singular}");
    }
}
