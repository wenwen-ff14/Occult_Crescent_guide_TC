using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using CrescentCompass.Core;
using System.Numerics;

internal static class CeMapAudit
{
    internal static void Run(string path)
    {
        using var game = new GameData(path, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var versionPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, "ffxivgame.ver");
        Console.WriteLine($"CLIENT {File.ReadAllText(versionPath).Trim()}");
        var map = game.GetExcelSheet<Map>()!.GetRow(967);
        if (map.SizeFactor != 100 || map.OffsetX != 0 || map.OffsetY != 0) throw new InvalidDataException("Map projection changed.");
        Console.WriteLine($"MAP {map.RowId}: {map.Id}, size={map.SizeFactor}, offset={map.OffsetX},{map.OffsetY}");
        var id = map.Id.ToString();
        if ($"ui/map/{id}/{id.Replace("/", "")}_m.tex" != CeMapCatalog.TexturePath) throw new InvalidDataException("Map path changed.");
        Export(game, CeMapCatalog.TexturePath, "map-967");
        var sheet = game.Excel.GetSheet<RawRow>(name: "DynamicEvent");
        var eventTypes = game.Excel.GetSheet<RawRow>(name: "DynamicEventType");
        var territory = game.GetExcelSheet<TerritoryType>()!.GetRow(1252);
        var bg = territory.Bg.ToString();
        var directory = "bg/" + bg[..(bg.LastIndexOf('/') + 1)];
        var locations = new Dictionary<uint, uint>();
        for (uint i = 33; i <= 47; i++)
        {
            var row = sheet.GetRow(i);
            var point = CeMapCatalog.Find((ushort)i) ?? throw new InvalidDataException($"Missing CE {i}");
            var icon = Convert.ToUInt32(eventTypes.GetRow(Convert.ToUInt32(row.ReadColumn(0))).ReadColumn(0));
            if (point.IconId != icon || row.ReadStringColumn(11).ToString() != CeCooldownTracker.Find((ushort)i)!.Name)
                throw new InvalidDataException($"CE name/icon mismatch: {i}");
            locations.Add(row.ReadUInt32Column(5), i);
            Console.WriteLine($"CE {i}: {row.ReadStringColumn(11)}, icon={icon}, location={row.ReadUInt32Column(5)}");
        }
        var matched = new HashSet<uint>();
        foreach (var file in new[] { "bg.lgb", "planmap.lgb", "planevent.lgb", "planlive.lgb", "planner.lgb" })
        {
            var scene = game.GetFile<LgbFile>(directory + file);
            if (scene is null) continue;
            foreach (var obj in scene.Layers.SelectMany(l => l.InstanceObjects))
                if (locations.TryGetValue(obj.InstanceId, out var ce))
                {
                    var p = obj.Transform.Translation;
                    if (Vector2.Distance(CeMapCatalog.Find((ushort)ce)!.WorldPosition, new(p.X, p.Z)) > 0.01f)
                        throw new InvalidDataException($"CE position mismatch: {ce}");
                    matched.Add(ce);
                    Console.WriteLine($"POSITION {ce}: {p.X}, {p.Y}, {p.Z}; map={p.X / 50f + 21.48f:F3},{p.Z / 50f + 21.48f:F3}; {file}");
                }
        }
        if (matched.Count != 15) throw new InvalidDataException("Not all 15 CE positions were verified.");
        foreach (var icon in new uint[] { 63909, 63911 })
        {
            var iconPath = $"ui/icon/{icon / 1000 * 1000:000000}/{icon:000000}.tex";
            Export(game, iconPath, icon.ToString());
        }
        Console.WriteLine("PASS: 15 CE names/positions/icons and map projection verified against local TC data.");
    }

    private static void Export(GameData game, string path, string name)
    {
        var texture = game.GetFile<TexFile>(path) ?? throw new InvalidDataException("Missing texture " + path);
        Directory.CreateDirectory("artifacts/ce-map-textures");
        using var output = new BinaryWriter(File.Create($"artifacts/ce-map-textures/{name}.rgba"));
        var rgba = texture.ImageData.ToArray();
        for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        output.Write((int)texture.Header.Width); output.Write((int)texture.Header.Height); output.Write(rgba);
        Console.WriteLine($"TEXTURE {path}: {texture.Header.Width}x{texture.Header.Height}");
    }
}
