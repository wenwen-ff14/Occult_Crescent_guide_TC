using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Iced.Intel;

// Read-only offline audit. Never attach to, patch, or call the game process.
if (args.Length != 2) throw new ArgumentException("Usage: WaymarkAudit <ffxiv_dx11.exe> <profile.json>");
var bytes = File.ReadAllBytes(args[0]);
const string expectedHash = "837B9E2893D45D22C1DDE3D1A134AF2749E0C9FFF6E2EF7246B84860F855E247";
if (Convert.ToHexString(SHA256.HashData(bytes)) != expectedHash) throw new InvalidDataException("Client build not audited.");
using var pe = new PEReader(new MemoryStream(bytes));
int Raw(int rva) { var s = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + s.SizeOfRawData); return rva - s.VirtualAddress + s.PointerToRawData; }
Instruction At(int rva) { var d = Decoder.Create(64, new ByteArrayCodeReader(bytes, Raw(rva), 16)); d.IP = (ulong)rva; d.Decode(out var i); return i; }
void Check(bool condition, string label) { if (!condition) throw new InvalidDataException(label); Console.WriteLine("PASS " + label); }
foreach (var (call, target) in new[] { (0x897A25, 0xA0D480), (0x89A66B, 0xA0D640), (0xC97CB6, 0x5C0080) })
    Check(At(call).Mnemonic == Mnemonic.Call && At(call).NearBranchTarget == (ulong)target, $"call {call:X} -> {target:X}");
Check(At(0xA79DF1).Mnemonic == Mnemonic.Lea && At(0xA79DF1).IPRelativeMemoryAddress == 0x29594F0, "MarkingController singleton");
Check(At(0xC97C4C).Mnemonic == Mnemonic.Mov && At(0xC97C4C).IPRelativeMemoryAddress == 0x2778F80, "Framework singleton pointer");
Check(At(0xC97CA5).MemoryDisplacement64 == 0x2B58, "Framework BGCollisionModule offset");
var targets = new Dictionary<string, int> {
    ["Place"] = 0xA0D480, ["Clear"] = 0xA0D640, ["Raycast"] = 0x5C0080,
    ["MarkingReference"] = 0xA79DF1, ["FrameworkReference"] = 0xC97C4C, ["CollisionReference"] = 0xC97CA5,
};
var profile = new {
    GameVersion = "2026.09.14.0000.0000", Sha256 = expectedHash,
    MarkingRva = 0x29594F0, FrameworkPointerRva = 0x2778F80, CollisionOffset = 0x2B58,
    MarkerOffset = 0x1E0, MarkerStride = 0x20,
    Targets = targets.ToDictionary(x => x.Key, x => new { Rva = x.Value, Bytes = Convert.ToHexString(bytes.AsSpan(Raw(x.Value), 16)) })
};
var serialized = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
if (File.Exists(args[1])) Check(File.ReadAllText(args[1]).Trim() == serialized, "committed native profile matches audited executable");
else File.WriteAllText(args[1], serialized);
