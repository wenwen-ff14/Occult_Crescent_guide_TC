using System.Reflection;
using System.Reflection.PortableExecutable;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;

internal static class CarrotAudit
{
    internal static void Run(string clientPath, string sqpackPath)
    {
        var method = typeof(AgentInventoryContext).GetMethods().Single(m => m.Name == "UseItem");
        Console.WriteLine("SDK " + method);
        var signature = (string)method.CustomAttributes.Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value!;
        var pattern = signature.Split(' ').Select(s => s == "??" ? (byte?)null : Convert.ToByte(s, 16)).ToArray();
        var bytes = File.ReadAllBytes(clientPath);
        using var client = new PEReader(new MemoryStream(bytes));
        var section = client.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
        var count = 0;
        for (var i = section.PointerToRawData; i <= section.PointerToRawData + section.SizeOfRawData - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++) if (pattern[j] is { } b && bytes[i + j] != b) { match = false; break; }
            if (match) count++;
        }
        if (count != 1) throw new InvalidDataException($"UseItem signature matches: {count}");
        Console.WriteLine("PASS UseItem SDK signature has exactly one TC client match");
        using var game = new GameData(sqpackPath, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var item = (game.GetExcelSheet<Item>() ?? throw new InvalidDataException("Missing Item sheet")).GetRow(48096);
        Console.WriteLine($"ITEM {item.RowId}: {item.Name}; action={item.ItemAction.RowId}");
        var objects = game.GetExcelSheet<EObjName>() ?? throw new InvalidDataException("Missing EObjName sheet");
        foreach (var id in new uint[] { 2010139, 2012936 }) Console.WriteLine($"EOBJ {id}: {objects.GetRow(id).Singular}");
    }
}
