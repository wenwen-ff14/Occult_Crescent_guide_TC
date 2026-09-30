using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using Lumina.Data.Files;

internal static class PotFateAudit
{
    internal static void Run(string path)
    {
        using var game = new GameData(path, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var versionPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, "ffxivgame.ver");
        Console.WriteLine($"CLIENT {File.ReadAllText(versionPath).Trim()}");
        var sheet = game.GetExcelSheet<Fate>()!;
        foreach (var id in new uint[] { 1976, 1977, 2072, 2073 })
        {
            Console.WriteLine(sheet.TryGetRow(id, out var fate) ? $"POT FATE {id}: {fate.Name} | {fate.Description}" : $"POT FATE {id}: not in local client");
            if (fate.RowId == 0) continue;
            Console.WriteLine($"LOCATION {id}: {fate.Location}");
        }
        foreach (var fate in sheet.Where(f => f.Name.ToString().Contains("魔法罐")))
            Console.WriteLine($"NAME MATCH {fate.RowId}: {fate.Name}");
        var locations = sheet.Where(f => f.RowId is 1976 or 1977 or 2072 or 2073).ToDictionary(f => f.Location, f => f.RowId);
        foreach (var id in new uint[] { 1252, 1346 })
        {
            if (!game.GetExcelSheet<TerritoryType>()!.TryGetRow(id, out var territory)) continue;
            var bg = territory.Bg.ToString();
            var directory = "bg/" + bg[..(bg.LastIndexOf('/') + 1)];
            var map = territory.Map.Value;
            foreach (var file in new[] { "bg.lgb", "planmap.lgb", "planevent.lgb", "planlive.lgb", "planner.lgb" })
            {
                var scene = game.GetFile<LgbFile>(directory + file);
                if (scene is null) continue;
                foreach (var obj in scene.Layers.SelectMany(layer => layer.InstanceObjects))
                {
                    if (!locations.TryGetValue(obj.InstanceId, out var fateId)) continue;
                    var p = obj.Transform.Translation;
                    var x = (p.X + map.OffsetX) / 50f + 1 + 2048f / map.SizeFactor;
                    var y = (p.Z + map.OffsetY) / 50f + 1 + 2048f / map.SizeFactor;
                    Console.WriteLine($"FATE LOCATION {fateId}: territory={id}, map={map.RowId}, xyz=({p.X}, {p.Y}, {p.Z}), mapXY=({x:F3}, {y:F3}), instance={obj.InstanceId}, source={file}");
                }
            }
        }
    }
}
