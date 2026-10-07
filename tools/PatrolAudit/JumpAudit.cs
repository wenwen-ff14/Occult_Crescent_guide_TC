using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;

internal static class JumpAudit
{
    // Offline signature/data audit only. No native function is invoked and no game process is attached.
    internal static void Run(string clientPath, string sqpackPath)
    {
        var bytes = File.ReadAllBytes(clientPath);
        using var client = new PEReader(new MemoryStream(bytes));
        var section = client.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
        Console.WriteLine("CLIENT SHA256 " + Convert.ToHexString(SHA256.HashData(bytes)));
        foreach (var name in new[] { "Instance", "UseAction" })
        {
            var method = typeof(ActionManager).GetMethods().Single(m => m.Name == name);
            var attribute = method.CustomAttributes.Single(a => a.AttributeType.Name is "StaticAddressAttribute" or "MemberFunctionAttribute");
            var signature = (string)attribute.ConstructorArguments[0].Value!;
            var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
            var matches = new List<int>();
            for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
            {
                var found = true;
                for (var j = 0; j < pattern.Length; j++)
                    if (pattern[j] is { } b && bytes[i + j] != b) { found = false; break; }
                if (found) matches.Add(i);
            }
            if (matches.Count != 1) throw new InvalidDataException($"ActionManager.{name}: expected one SDK signature match, got {matches.Count}");
            Console.WriteLine($"PASS ActionManager.{name} SDK signature unique at RVA {matches[0] - section.PointerToRawData + section.VirtualAddress:X}");
            Console.WriteLine("SIGNATURE " + signature);
        }
        using var data = new GameData(sqpackPath, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var jump = (data.GetExcelSheet<GeneralAction>() ?? throw new InvalidDataException("Missing GeneralAction sheet")).GetRow(2);
        var jumpName = jump.Name.ExtractText();
        if (jumpName != "跳躍") throw new InvalidDataException($"GeneralAction 2 is not the expected TC jump: {jumpName}");
        Console.WriteLine($"PASS GeneralAction 2 = {jumpName}; ActionType.GeneralAction = {(uint)ActionType.GeneralAction}");
    }
}
