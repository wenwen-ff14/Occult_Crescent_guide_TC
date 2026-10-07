using System.Reflection;
using System.Reflection.PortableExecutable;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina"
    ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
Run(args);

static void Run(string[] args)
{
if (args.Length == 3 && args[0] == "--jump") { JumpAudit.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--carrot") { CarrotAudit.Run(args[1], args[2]); return; }
if (args.Length > 0) CheckNative(args[0]);
foreach (var type in typeof(PublicContentOccultCrescent).Assembly.GetTypes().Where(t => !t.Name.Contains("Addresses") && !t.Name.Contains("Delegates")))
{
    foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => m.Name.Contains("Walk", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Knowledge", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"MOVEMENT {type.FullName}: {member}");
}

static void CheckNative(string path)
{
    var bytes = File.ReadAllBytes(path);
    using var client = new PEReader(new MemoryStream(bytes));
    var section = client.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var attribute = typeof(Control).GetMethod("Instance")!.CustomAttributes.Single(a => a.AttributeType.Name == "StaticAddressAttribute");
    var signature = (string)attribute.ConstructorArguments[0].Value!;
    var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
    List<int> matches = [];
    for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
    {
        var found = true;
        for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } b && bytes[i + j] != b) { found = false; break; }
        if (found) matches.Add(i);
    }
    if (matches.Count != 1) throw new InvalidDataException($"Control.Instance: expected one SDK signature match, got {matches.Count}");
    Console.WriteLine("CLIENT SHA256 " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    Console.WriteLine($"PASS Control.Instance SDK signature unique at RVA {matches[0] - section.PointerToRawData + section.VirtualAddress:X}");
}
// Offline SDK inspection only; no connection to the running game.
foreach (var type in typeof(PublicContentOccultCrescent).Assembly.GetTypes().Where(t => t.Name is
    "OccultCrescentState" or "PlayerState" or "ForayInfo" or "Control"))
{
    Console.WriteLine($"TYPE {type.FullName} SIZE {type.StructLayoutAttribute?.Size:X}");
    foreach (var f in type.GetFields().Where(f => !f.IsStatic && (type.Name != "Control" || f.Name == "IsWalking") &&
        (type.Name != "PlayerState" || f.Name.Contains("Knowledge") || f.Name.Contains("Occult"))))
        Console.WriteLine($"FIELD {f.Name}:{f.FieldType} @{f.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()?.Value:X}");
    foreach (var p in type.GetProperties().Where(p => p.Name.Contains("Walk") || p.Name.Contains("Knowledge"))) Console.WriteLine($"PROPERTY {p}");
    foreach (var m in type.GetMethods().Where(m => m.Name.Contains("Walk") || m.Name.Contains("Knowledge"))) Console.WriteLine($"METHOD {m}");
}
foreach (var type in typeof(BNpcBase).Assembly.GetTypes().Where(t => t.Namespace == typeof(BNpcBase).Namespace && (t.Name is "BNpcBase" or "Behavior" or "MKDBNpcData")))
{
    Console.WriteLine("SHEET " + type.Name);
    foreach (var p in type.GetProperties()) Console.WriteLine($"  {p.Name}:{p.PropertyType}");
}
}
