using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrescentCompass.Core;

// LogMessage 10965/10966 in the TC client. These are count snapshots, never spawn coordinates or ownership assertions.
public sealed partial record TreasureSurvey(int Silver, int Bronze, DateTimeOffset CapturedAt)
{
    [GeneratedRegex(@"\A在當前區域中感知到了(?<silver>[0-9]+)個銀寶箱、(?<bronze>[0-9]+)個銅寶箱\.{6}!\z", RegexOptions.CultureInvariant)]
    private static partial Regex CountsPattern();

    public static TreasureSurvey? Parse(string text, DateTimeOffset time)
    {
        text = text.Trim().Normalize(NormalizationForm.FormKC);
        // NFKC expands each ellipsis to three full stops.
        if (text == "目前區域現在似乎沒有寶箱......") return new(0, 0, time);
        var match = CountsPattern().Match(text);
        if (!match.Success || !int.TryParse(match.Groups["silver"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var silver) ||
            !int.TryParse(match.Groups["bronze"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bronze)) return null;
        return new(silver, bronze, time);
    }
}
