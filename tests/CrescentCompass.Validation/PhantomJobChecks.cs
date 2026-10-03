using CrescentCompass.Core;

internal static class PhantomJobChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(PhantomJobs.All.Count == 13 && PhantomJobs.All.Select(j => j.IconId).Distinct().Count() == 13, "All thirteen phantom jobs have distinct game icons");
        foreach (var job in PhantomJobs.All)
        {
            foreach (var alias in new[] { job.Id.ToString(), job.Name, job.ShortName, "幻影" + job.ShortName, job.EnglishName.ToUpperInvariant(), job.EnglishName[8..], $" \"{job.EnglishName}\" " })
                check(PhantomJobs.Resolve(alias) == job, $"Unambiguous phantom job alias: {alias}");
            check(job.Macro == $"/crescent job {job.Id}", "Copied macro includes exact job ID and no extra action");
        }
        foreach (var invalid in new[] { "", "13", "255", "-1", "+1", "kn", "騎", "騎士 extra", "1\n/crescent stop", "\"騎士", "白魔道士" })
            check(PhantomJobs.Resolve(invalid) is null, "Unknown or ambiguous job does not guess: " + invalid);

        var ready = new PhantomJobContext(true, 1252, 1, 123, 0);
        var switcher = new PhantomJobSwitcher(); var calls = 0;
        bool Change(byte id) { calls++; return true; }
        foreach (var blocked in new[] { ready with { Ready = false }, ready with { Territory = 1 }, ready with { Character = 0 },
            ready with { CurrentJob = null }, ready with { InCombat = true }, ready with { Occupied = true }, ready with { Dead = true } })
            check(switcher.Request(1, blocked, 0, Change) == blocked.BlockReason && calls == 0, "Phantom switch rejects unavailable game state");
        switcher.Request(13, ready, 0, Change); switcher.Request(0, ready, 0, Change);
        check(calls == 0, "Invalid or already selected job makes no native call, including freelancer ID zero");
        var result = switcher.Request(1, ready, 0, Change);
        check(calls == 1 && switcher.Busy && result.Contains("等待遊戲確認"), "A sent request is pending, not successful");
        switcher.Request(2, ready, 500, Change);
        check(calls == 1, "Pending request cannot stack another switch");
        check(switcher.Update(ready with { CurrentJob = 1 }, 600)?.Contains("已切換為輔助騎士") == true && !switcher.Busy, "Observed game state confirms requested job");
        switcher.Request(2, ready with { CurrentJob = 1 }, 700, Change);
        check(calls == 1, "Rapid macros are rate limited even after confirmation");
        switcher.Request(2, ready with { CurrentJob = 1 }, 1000, Change);
        check(calls == 2 && switcher.Busy, "Next deliberate request allowed after throttle");
        check(switcher.Update(ready with { CurrentJob = 3 }, 3999) is null, "Unrelated job change does not confirm request");
        check(switcher.Update(ready, 4000)?.Contains("尚未確認") == true && !switcher.Busy && calls == 2, "No server confirmation times out without auto-retrying");
        switcher.Request(1, ready, 5000, Change);
        check(switcher.Update(ready with { Instance = 2 }, 5001)?.Contains("取消") == true && !switcher.Busy, "Changing instance cancels pending confirmation");
        switcher.Request(1, ready, 5002, Change);
        check(switcher.Update(ready with { Ready = false }, 5003)?.Contains("取消") == true, "Loading interrupts pending confirmation");
        switcher.Request(1, ready, 6000, _ => { calls++; return false; });
        var rejectedCalls = calls;
        switcher.Request(1, ready, 6001, Change);
        check(!switcher.Busy && calls == rejectedCalls, "Rejected native request remains rate limited");
        switcher.Request(1, ready, 1, Change);
        check(switcher.Busy && calls == rejectedCalls + 1, "Clock rollback clears old request timing");
        switcher.Update(ready with { CurrentJob = 1 }, 2);
        switcher.Update(ready with { Character = 999 }, 3);
        check(!switcher.Detail.Contains("已切換"), "New character does not inherit the previous character's success message");
    }
}
