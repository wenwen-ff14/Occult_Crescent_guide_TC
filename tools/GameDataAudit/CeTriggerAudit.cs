using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;

internal static class CeTriggerAudit
{
    internal static void Run(string path)
    {
        using var game = new GameData(path, new LuminaOptions { DefaultExcelLanguage = Language.TraditionalChinese });
        var versionPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, "ffxivgame.ver");
        Console.WriteLine($"CLIENT {File.ReadAllText(versionPath).Trim()}");
        Console.WriteLine("BNpcName: local TC client names; trigger relationships are separately sourced community data.");
        foreach (var row in game.GetExcelSheet<BNpcName>()!.Where(r => r.RowId is 13879 or 13875 or 13895 or 13913 or 13876 or 13884))
            Console.WriteLine($"BNpcName {row.RowId}: {row.Singular}");
    }
}
