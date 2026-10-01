using System.Text.RegularExpressions;

namespace CrescentCompass.Core;

public enum PotHintKind { Direction, Reveal, Bonus, Expired, Prompt }
public sealed record PotHint(PotHintKind Kind, int Direction = 0, string Distance = "");

public static partial class PotHints
{
    // Match rendered TC LogMessages, including sheet substitutions and switch macro output.
    // BNpcName 13742 = 撒嬌甕, 712 = 魔法甕; EventItem 2003296 = 魔法聖靈藥.
    // Keep the caller's system/NPC channel gate; never accept arbitrary speaker prefixes.
    [GeneratedRegex(@"^財寶好像是在[「『\s]*(東北|東南|西南|西北|北東|南東|南西|北西|正北|正東|正南|正西|北|東|南|西)[」』\s]*方向(很近|不遠|稍遠|很遠)的地方[！!。]?$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectionPattern();

    public static PotHint? Parse(string text)
    {
        text = text.Trim();
        var namedText = text.StartsWith("撒嬌甕", StringComparison.Ordinal) || text.StartsWith("魔法甕", StringComparison.Ordinal)
            ? text[3..].TrimStart() : text;
        if (text == "發現了財寶！！") return new(PotHintKind.Reveal);
        if (namedText == "似乎能夠告知第二處財寶所在地！") return new(PotHintKind.Bonus);
        if (text == "時間過了太久，已經發現的財寶消失了。") return new(PotHintKind.Expired);
        if (namedText.Replace("\r", "").Replace("\n", "") is "很想要聖靈藥。使用任務道具魔法聖靈藥，探知財寶的氣息吧！"
            or "很想要聖靈藥。使用任務道具，探知財寶的氣息吧！") return new(PotHintKind.Prompt);
        var match = DirectionPattern().Match(text);
        if (!match.Success) return null;
        return new(PotHintKind.Direction, match.Groups[1].Value switch
        {
            "北" or "正北" => 1, "東北" or "北東" => 2, "東" or "正東" => 3, "東南" or "南東" => 4,
            "南" or "正南" => 5, "西南" or "南西" => 6, "西" or "正西" => 7, "西北" or "北西" => 8, _ => 0,
        }, match.Groups[2].Value);
    }
}
