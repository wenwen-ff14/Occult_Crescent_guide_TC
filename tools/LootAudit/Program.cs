using System.Reflection;
using System.Text.Json;
using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina" ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
Run(args);
static void Run(string[] args)
{
if (args.FirstOrDefault() == "--fieldrefs")
{
    var bytes = File.ReadAllBytes(args[1]);
    using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes));
    var section = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var pattern = BitConverter.GetBytes(Convert.ToInt32(args[2], 16));
    for (var i = section.PointerToRawData; i < section.PointerToRawData + section.SizeOfRawData - 4; i++)
        if (bytes.AsSpan(i, 4).SequenceEqual(pattern)) Console.WriteLine($"{i - section.PointerToRawData + section.VirtualAddress:X}");
    return;
}
if (args.Contains("--ui-sdk"))
{
    var asm = typeof(AgentShop).Assembly;
    foreach (var name in new[] { "FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase", "FFXIVClientStructs.FFXIV.Client.UI.AddonInputNumeric", "FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno" })
    {
        var type = asm.GetType(name);
        if (type == null) continue;
        Console.WriteLine(name);
        foreach (var f in type.GetFields().Where(f => !f.IsStatic)) Console.WriteLine($"FIELD {f.Name}: {f.FieldType} offset={f.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()?.Value:X}");
        foreach (var m in type.GetMethods().Where(m => m.Name.Contains("Callback") || m.Name.Contains("Value"))) Console.WriteLine(m);
    }
    return;
}
if (args.FirstOrDefault() == "--disasm")
{
    var bytes = File.ReadAllBytes(args[1]);
    using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes));
    var rva = Convert.ToInt32(args[2], 16);
    var section = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize);
    var raw = rva - section.VirtualAddress + section.PointerToRawData;
    var decoder = Iced.Intel.Decoder.Create(64, new Iced.Intel.ByteArrayCodeReader(bytes, raw, 4096));
    decoder.IP = (ulong)rva;
    for (var i = 0; i < (args.Length > 3 ? int.Parse(args[3]) : 120); i++)
    { decoder.Decode(out var ins); Console.WriteLine($"{ins.IP:X}: {ins}"); }
    return;
}
if (args.FirstOrDefault() == "--native")
{
    var bytes = File.ReadAllBytes(args[1]);
    using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes));
    var section = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var discard = (string)typeof(InventoryManager).GetMethod("DiscardItem")!.CustomAttributes.Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value!;
    Console.WriteLine("Client SHA256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    foreach (var (name, signature) in new[] { ("Sell", "48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B F2 8B E9"), ("Discard", discard),
        ("SaleYesNoProxy", "48 8D 0D ?? ?? ?? ?? 48 89 4C 24 20 48 8B CF E8") })
    {
        var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
        var matches = new List<int>();
        for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } b && b != bytes[i + j]) { match = false; break; }
            if (match) matches.Add(i);
        }
        if (matches.Count != 1) throw new InvalidDataException($"{name}: expected unique signature, got {matches.Count}");
        Console.WriteLine($"PASS {name}: unique signature, RVA {matches[0] - section.PointerToRawData + section.VirtualAddress:X}");
        if (name == "SaleYesNoProxy") Console.WriteLine($"SaleYesNoProxy target RVA: {matches[0] - section.PointerToRawData + section.VirtualAddress + 7 + BitConverter.ToInt32(bytes, matches[0] + 3):X}");
    }
    return;
}
if (args.Contains("--sdk"))
{
    foreach (var type in new[] { typeof(InventoryManager), typeof(InventoryItem), typeof(InventoryContainer), typeof(AgentInventoryContext), typeof(AgentInventoryContext.InventoryContextEvent), typeof(AgentShop), typeof(FFXIVClientStructs.FFXIV.Client.Game.Event.ShopEventHandler), typeof(FFXIVClientStructs.FFXIV.Client.Game.Event.ShopEventHandler.AgentProxy) })
    {
        Console.WriteLine(type.Name);
        foreach (var f in type.GetFields().Where(f => !f.IsStatic)) Console.WriteLine($"FIELD {f.Name}: {f.FieldType} offset={f.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()?.Value:X}");
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))) Console.WriteLine($"METHOD {m}");
    }
    return;
}
using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
using var drops = JsonDocument.Parse(File.ReadAllText(args[1]));
using var names = JsonDocument.Parse(File.ReadAllText(args[2]));
var sources = new SortedDictionary<uint, HashSet<string>>();
foreach (var region in drops.RootElement.EnumerateArray())
foreach (var variant in region.GetProperty("Variants").EnumerateArray())
foreach (var patch in variant.GetProperty("Patches").EnumerateObject())
foreach (var item in patch.Value.GetProperty("Items").EnumerateArray())
{
    var id = item.GetProperty("Id").GetUInt32();
    if (!sources.TryGetValue(id, out var set)) sources[id] = set = [];
    set.Add($"{region.GetProperty("Name").GetString()} / {variant.GetProperty("Name").GetString()}");
}
var sheet = game.GetExcelSheet<Item>()!;
using var supplement = JsonDocument.Parse(File.ReadAllText("docs/audit/loot-tower-supplement.json"));
var extraNames = new Dictionary<uint, string>();
foreach (var item in supplement.RootElement.EnumerateArray())
{
    var id = item.GetProperty("Id").GetUInt32();
    if (!sources.TryGetValue(id, out var set)) sources[id] = set = [];
    var source = item.GetProperty("Source").GetString()!;
    set.Add(source);
    if (source == "North Horn / Forked Tower Magic") set.Add("North Horn / Forked Tower Magic Extreme");
    extraNames[id] = item.GetProperty("EnglishName").GetString()!;
}
var rows = sources.Where(pair => !CrescentCompass.Core.LootCleanup.IsExcluded(pair.Key)).Select(pair => {
    var found = sheet.TryGetRow(pair.Key, out var row) && !string.IsNullOrWhiteSpace(row.Name.ToString());
    var en = names.RootElement.TryGetProperty(pair.Key.ToString(), out var n) ? n.GetProperty("En").GetString()! : extraNames.GetValueOrDefault(pair.Key, $"Item {pair.Key}");
    return new { Id = pair.Key, Name = found ? row.Name.ToString() : en, EnglishName = en, Sources = pair.Value.Order().ToArray(), Available = found, PriceLow = found ? row.PriceLow : 0, SearchCategory = found ? row.ItemSearchCategory.RowId : 0 };
}).ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(args[3])!);
File.WriteAllText(args[3], JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{rows.Length} items; {rows.Count(r => r.Available)} available in local client");
foreach (var r in rows.Where(r => !r.Available || r.SearchCategory == 0)) Console.WriteLine($"{r.Id}: {r.Name}, available={r.Available}, category={r.SearchCategory}");
}
