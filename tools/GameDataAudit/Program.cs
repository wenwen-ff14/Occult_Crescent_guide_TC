using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System.Runtime.Loader;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using System.Text.Json;

if (args.Length is < 1 or > 2) throw new ArgumentException("Supply the game's sqpack directory and optional --sightseeing, --pot-fates or --ce-triggers.");
AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina"
    ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
if (args.Length == 2 && args[1] == "--sightseeing") SightseeingAudit.Run(args[0]);
else if (args.Length == 2 && args[1] == "--pot-fates") PotFateAudit.Run(args[0]);
else if (args.Length == 2 && args[1] == "--ce-triggers") CeTriggerAudit.Run(args[0]);
else Audit.Run(args[0]);

internal static class Audit
{
internal static void Run(string path)
{
using var game = new GameData(path, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
var versionPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, "ffxivgame.ver");
var clientVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "unavailable";
Console.WriteLine($"CLIENT {clientVersion}");
Directory.CreateDirectory(".references");
foreach (var language in new[] { Language.TraditionalChinese, Language.ChineseTraditional, Language.English })
{
    try
    {
        var statuses = game.GetExcelSheet<Status>(language)!;
        var matching = statuses.Where(s => Contains(s.Name.ToString()) || s.Description.ToString().Contains("財寶")).ToArray();
        Console.WriteLine($"STATUS LANGUAGE {language}: {statuses.Count}");
        foreach (var status in matching) Console.WriteLine($"STATUS {status.RowId}: {status.Name} | {status.Description}");
        var logs = game.GetExcelSheet<LogMessage>(language)!;
        foreach (var id in new uint[] { 10985, 10986, 10987, 10988, 10989, 10990, 10993, 10994, 10995 })
            Console.WriteLine($"LOGCHANNEL {id}: {logs.GetRow(id).LogKind.RowId}");
        foreach (var log in logs.Where(l => Contains(l.Text.ToString()))) Console.WriteLine($"LOG {log.RowId}: {log.Text}");
        var addons = game.GetExcelSheet<Addon>(language)!;
        foreach (var addon in addons.Where(a => Contains(a.Text.ToString()))) Console.WriteLine($"ADDON {addon.RowId}: {addon.Text}");
        var objects = game.GetExcelSheet<EObjName>(language)!;
        foreach (var obj in objects.Where(o => o.RowId is 2010139 or 2012936 or 2014741 or 2014742 or 2014743))
            Console.WriteLine($"EOBJ {obj.RowId}: {obj.Singular}");
        break;
    }
    catch (Exception e) { Console.WriteLine($"{language}: {e.Message}"); }
}
foreach (var id in new uint[] { 1252, 1346 })
{
    try
    {
        if (game.GetExcelSheet<TerritoryType>()!.TryGetRow(id, out var territory))
        {
            Console.WriteLine($"ZONE {id}: {territory.Name}, BG={territory.Bg}, Map={territory.Map.RowId}");
            var bg = territory.Bg.ToString();
            var directory = "bg/" + bg[..(bg.LastIndexOf('/') + 1)];
            var placements = new List<object>();
            foreach (var file in new[] { "bg.lgb", "planmap.lgb", "planevent.lgb", "planlive.lgb", "planner.lgb" })
            {
                var scene = game.GetFile<LgbFile>(directory + file);
                if (scene is null) continue;
                Console.WriteLine($"SCENE {directory + file}: {scene.Layers.Length} layers");
                foreach (var layer in scene.Layers)
                foreach (var obj in layer.InstanceObjects)
                {
                    var translation = obj.Transform.Translation;
                    if (obj.Object is LayerCommon.TreasureInstanceObject treasure)
                    {
                        var model = game.GetExcelSheet<Treasure>()!.GetRow(treasure.ParentData.BaseId).SGB.RowId;
                        placements.Add(new { instanceId = obj.InstanceId, dataId = treasure.ParentData.BaseId, sgbId = model,
                            x = translation.X, y = translation.Y, z = translation.Z, layer = layer.Name, source = file });
                    }
                    if (obj.Object is LayerCommon.EventInstanceObject eventObj && eventObj.ParentData.BaseId is 2010139 or 2012936 or 2014741 or 2014742 or 2014743)
                        Console.WriteLine($"EVENTPOINT {eventObj.ParentData.BaseId} {translation.X},{translation.Y},{translation.Z} layer={layer.Name}");
                }
            }
            var target = Path.Combine(".references", $"local-{id}-treasures.json");
            File.WriteAllText(target, JsonSerializer.Serialize(new { territoryId = id, clientVersion, treasures = placements }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PLACEMENTS {id}: {placements.Count}");
        }
        else Console.WriteLine($"ZONE {id}: not in local client");
    }
    catch (Exception e) { Console.WriteLine($"ZONE {id}: {e.Message}"); }
}
try
{
    var treasures = game.GetExcelSheet<Treasure>()!;
    foreach (var t in treasures.Where(t => t.RowId is >= 1789 and <= 1856 or >= 2006 and <= 2073))
        Console.WriteLine($"TREASURE {t.RowId}: SGB={t.SGB.RowId}");
}
catch (Exception e) { Console.WriteLine($"TREASURE: {e.Message}"); }
}

static bool Contains(string text) => new[] { "魔法罐", "魔法壺", "魔法壶", "尋寶", "寻宝", "財寶", "财宝", "Cache Me", "elixir", "persistent pot", "coffer lies", "treasure is", "魔法靈藥", "魔法灵药" }
    .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
