using System.Reflection;
using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

// Read-only metadata audit; never attaches to the running client or reads player data.
if (args is ["--native", var path])
{
    var bytes = File.ReadAllBytes(path);
    using var pe = new PEReader(new MemoryStream(bytes));
    var section = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    Console.WriteLine($"Client SHA256: {Convert.ToHexString(SHA256.HashData(bytes))}");
    foreach (var (type, name) in new[] { (typeof(InfoModule), "GetInfoProxyById"),
        (typeof(InfoModule), "GetLocalContentId"), (typeof(InfoProxyCommonList), "GetEntry") })
    {
        var signature = (string)type.GetMethod(name)!.CustomAttributes.Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value!;
        var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
        List<int> matches = [];
        for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } value && value != bytes[i + j]) { match = false; break; }
            if (match) matches.Add(i);
        }
        if (matches.Count != 1) throw new InvalidDataException($"{name}: expected unique signature, got {matches.Count}");
        var raw = matches[0];
        var rva = raw - section.PointerToRawData + section.VirtualAddress;
        var target = bytes[raw] == 0xE8 ? rva + 5 + BitConverter.ToInt32(bytes, raw + 1) : rva;
        Console.WriteLine($"PASS {type.Name}.{name}: signature at {rva:X}, function RVA {target:X}");
    }
    return;
}
foreach (var type in new[] { typeof(InfoModule), typeof(InfoProxyFriendList), typeof(InfoProxyCommonList),
    typeof(InfoProxyCommonList.CharacterData), typeof(InfoProxyPageInterface), typeof(Character) })
{
    Console.WriteLine($"{type.Name} size={type.StructLayoutAttribute?.Size:X}");
    foreach (var field in type.GetFields().Where(f => !f.IsStatic &&
        (type != typeof(Character) || f.Name.Contains("World") || f.Name.Contains("ContentId") || f.Name.Contains("Flags"))))
        Console.WriteLine($"  {field.Name}: {field.FieldType.Name} offset={field.GetCustomAttribute<FieldOffsetAttribute>()?.Value:X}");
    if (type == typeof(Character)) continue;
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        Console.WriteLine($"  {method}");
        foreach (var attribute in method.CustomAttributes.Where(a => a.AttributeType.Name is "MemberFunctionAttribute" or "StaticAddressAttribute" or "VirtualFunctionAttribute"))
            Console.WriteLine($"    {attribute}");
    }
}
