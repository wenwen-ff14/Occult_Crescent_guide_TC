using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

// Offline, read-only SDK/client compatibility check. Does not attach to the running game.
if (args.Length != 2) throw new ArgumentException("ChestAudit <FFXIVClientStructs.dll> <ffxiv_dx11.exe>");
using var sdk = new PEReader(File.OpenRead(args[0]));
var md = sdk.GetMetadataReader();
var bytes = File.ReadAllBytes(args[1]);
using var client = new PEReader(new MemoryStream(bytes));
var section = client.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
var hash = Convert.ToHexString(SHA256.HashData(bytes));
if (hash != "837B9E2893D45D22C1DDE3D1A134AF2749E0C9FFF6E2EF7246B84860F855E247")
    throw new InvalidDataException("Client executable has changed; re-audit required.");
Console.WriteLine("Client SHA256: " + hash);
foreach (var (typeName, methodName, offset, expected) in new[] {
    ("TargetSystem", "Instance", 3, 0x291A600), ("TargetSystem", "InteractWithObject", 1, 0x5F2D50), ("Loot", "Instance", 3, 0x294D538) })
{
    var type = md.TypeDefinitions.Select(md.GetTypeDefinition).Single(t => md.GetString(t.Name) == typeName);
    var method = type.GetMethods().Select(md.GetMethodDefinition).Single(m => md.GetString(m.Name) == methodName);
    var signatures = new List<string>();
    foreach (var handle in method.GetCustomAttributes())
    {
        var attribute = md.GetCustomAttribute(handle);
        var reader = md.GetBlobReader(attribute.Value);
        if (reader.ReadUInt16() != 1) continue;
        try
        {
            var value = reader.ReadSerializedString();
            if (value is not null && value.Contains("??") && value.Split(' ').All(s => s == "??" || s.Length == 2 && byte.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out _)))
                signatures.Add(value);
        }
        catch (BadImageFormatException) { }
    }
    var signature = signatures.Single();
    var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
    List<int> matches = [];
    for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
    {
        var match = true;
        for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } b && bytes[i + j] != b) { match = false; break; }
        if (match) matches.Add(i);
    }
    if (matches.Count != 1) throw new InvalidDataException($"{typeName}.{methodName}: expected 1 signature match, got {matches.Count}");
    var raw = matches[0]; var rva = raw - section.PointerToRawData + section.VirtualAddress;
    var target = rva + offset + 4 + BitConverter.ToInt32(bytes, raw + offset);
    if (expected != 0 && target != expected) throw new InvalidDataException($"Unexpected target: {target:X}");
    Console.WriteLine($"PASS {typeName}.{methodName}: SDK signature unique at {rva:X}, resolves {target:X}\n  {signature}");
}
