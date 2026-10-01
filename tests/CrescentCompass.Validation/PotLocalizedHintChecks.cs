using System.Numerics;
using CrescentCompass.Core;

internal static class PotLocalizedHintChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string bonusText = "撒嬌甕似乎能夠告知第二處財寶所在地！";
        // Rendered TC LogMessage 10994 includes BNpcName, which plain sheet text omits.
        check(PotHints.Parse(bonusText)?.Kind == PotHintKind.Bonus, "Rendered TC second-treasure message includes the pot name");
        check(PotHints.Parse("魔法甕似乎能夠告知第二處財寶所在地！")?.Kind == PotHintKind.Bonus, "Known alternate pot name is accepted");
        check(PotHints.Parse("  撒嬌甕似乎能夠告知第二處財寶所在地！\n")?.Kind == PotHintKind.Bonus, "Outer whitespace does not hide the bonus message");
        foreach (var invalid in new[] { "玩家說：" + bonusText, "有人提到" + bonusText,
            "不明角色似乎能夠告知第二處財寶所在地！", bonusText + "（引用）", "撒嬌甕發現了財寶！！" })
            check(PotHints.Parse(invalid) is null, "Arbitrary names, quotes and extra text are not accepted as game hints");

        string[] directions = ["正北", "東北", "正東", "東南", "正南", "西南", "正西", "西北"];
        foreach (var direction in Enumerable.Range(1, 8))
        foreach (var distance in new[] { "很近", "不遠", "稍遠", "很遠" })
        {
            var hint = PotHints.Parse($"財寶好像是在{directions[direction - 1]}方向{distance}的地方！");
            check(hint is { Kind: PotHintKind.Direction } && hint.Direction == direction && hint.Distance == distance,
                "Rendered TC cardinal/intercardinal direction and distance map correctly");
        }
        check(PotHints.Parse("財寶好像是在「正西」方向不遠的地方！")?.Direction == 7, "Quoted TC cardinal direction maps correctly");
        foreach (var name in new[] { "撒嬌甕", "魔法甕" })
            check(PotHints.Parse($"{name}很想要聖靈藥。\n使用任務道具魔法聖靈藥，\n探知財寶的氣息吧！")?.Kind == PotHintKind.Prompt,
                "Rendered TC elixir prompt includes both NPC and event item names");
        check(PotHints.Parse("玩家說：撒嬌甕很想要聖靈藥。使用任務道具魔法聖靈藥，探知財寶的氣息吧！") is null,
            "Quoted elixir prompt is rejected");

        var pot = new PotSession();
        var inbox = new PotHintQueue();
        var automation = new PotFlagAutomation();
        var time = DateTimeOffset.UtcNow;
        PotCandidate[] pads = [new("primary", 1252, 1976, false, new(20, 0, 0)),
            new("bonus-west", 1252, 0, true, new(-60, 0, 0)), new("bonus-south", 1252, 0, true, new(0, 0, 90))];
        var first = new Observation(100, 2014741, SpotKind.PotGold, new(6, 0, 0));
        var second = new Observation(101, 2014742, SpotKind.PotSilver, new(-58, 0, 0));
        var flags = new List<Vector3>();
        void Tick(long ms, bool canFlag = true) => automation.Update(pot, true, canFlag, ms,
            () => { flags.Add(pot.Target!.Value); return true; });
        void Queue(string message, Vector3 origin, DateTimeOffset at) =>
            inbox.Enqueue(PotHints.Parse(message)!, origin, 1252, 1, at);

        pot.UpdateBuff(true, 1252, Vector3.Zero, pads, []);
        Queue("發現了財寶！！", Vector3.Zero, time);
        inbox.ApplyTo(pot, 1252, 1, time);
        pot.ObserveReveal([first], time); Tick(0);
        check(pot.Revealed && flags.Last() == first.Position, "First treasure is tracked before the bonus offer");
        Queue(bonusText, Vector3.Zero, time.AddSeconds(2));
        Queue(bonusText, new(200, 0, 0), time.AddMilliseconds(2050));
        inbox.ApplyTo(pot, 1252, 1, time.AddMilliseconds(2500));
        Tick(2500, canFlag: false);
        check(!pot.Revealed && pot.Candidates.Count == 2 && pot.Candidates.All(p => p.Bonus) && flags.Count == 1,
            "Actual bonus text switches away from first treasure while interaction delays flagging");
        var revision = pot.Revision;
        inbox.ApplyTo(pot, 1252, 1, time.AddSeconds(3)); Tick(3000);
        check(pot.Revision == revision && flags.Count == 2 && flags.Last() == pads[1].Position,
            "Duplicate chat/toast delivery switches pool once and flags bonus after interaction");
        Queue("財寶好像是在正西方向不遠的地方！", Vector3.Zero, time.AddSeconds(5));
        inbox.ApplyTo(pot, 1252, 1, time.AddSeconds(5)); Tick(5000);
        check(pot.Candidates.Count == 1 && pot.Target == pads[1].Position,
            "Real cardinal hint narrows the second-treasure pool without manual restart");
        Queue("發現了財寶！！", second.Position, time.AddSeconds(7));
        inbox.ApplyTo(pot, 1252, 1, time.AddSeconds(7));
        check(pot.ObserveReveal([first, second], time.AddSeconds(7)) && pot.Revealed && pot.Target == second.Position,
            "Second revealed coffer replaces the first coffer target");
        Tick(7000);
        check(flags.Last() == second.Position && automation.OwnsNavigation, "Second coffer actual coordinates automatically replace the flag");
    }
}
