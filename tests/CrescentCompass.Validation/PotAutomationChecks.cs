using System.Numerics;
using CrescentCompass.Core;

internal static class PotAutomationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var pot = new PotSession();
        var automation = new PotFlagAutomation();
        var inbox = new PotHintQueue();
        var time = DateTimeOffset.UtcNow;
        PotCandidate[] pads = [new("near", 1252, 1976, false, new(40, 0, 0)),
            new("north", 1252, 1976, false, new(0, 0, -100)), new("bonus", 1252, 0, true, new(-60, 0, 0))];
        var coffer = new Observation(9, 2014741, SpotKind.PotGold, new(8, 0, 0));
        var flags = new List<Vector3>();
        var calls = 0;
        void Start()
        {
            pot.Reset(); automation.Reset(); inbox.Clear(); flags.Clear(); calls = 0;
            pot.UpdateBuff(true, 1252, Vector3.Zero, pads, []);
        }
        void Tick(long ms, bool enabled = true, bool canFlag = true, bool success = true) =>
            automation.Update(pot, enabled, canFlag, ms, () => { calls++; if (success) flags.Add(pot.Target!.Value); return success; });

        Start(); Tick(0);
        check(flags.SequenceEqual([pads[0].Position]) && automation.OwnsNavigation, "Pot acquisition automatically flags a candidate and owns navigation");
        Tick(500); Tick(2000); Tick(5000);
        check(calls == 1, "Unchanged acquisition does not keep opening the map");
        inbox.Enqueue(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, 1252, 1, time);
        inbox.ApplyTo(pot, 1252, 1, time);
        Tick(5500);
        check(flags.Last() == pads[1].Position && calls == 2, "New direction automatically moves flag without manual plugin action");
        pot.Apply(new(PotHintKind.Bonus), Vector3.Zero, time); Tick(6000);
        check(calls == 2, "Rapid hints are throttled instead of repeatedly opening the map");
        Tick(7000);
        check(flags.Last() == pads[2].Position && calls == 3, "Bonus switches to the new search pool and automatically flags it");
        pot.Apply(new(PotHintKind.Reveal), Vector3.Zero, time); Tick(9000);
        check(calls == 3 && automation.OwnsNavigation && pot.Target is null, "Reveal wait cancels stale candidate flag while retaining pot navigation priority");
        pot.ObserveReveal([coffer], time.AddSeconds(1)); Tick(9500);
        check(flags.Last() == coffer.Position && pot.Revealed, "Personal reveal plus unique nearby coffer flags observed position automatically");
        var shifted = coffer with { Position = new(9, 0, 0) };
        check(pot.ObserveReveal([shifted], time.AddSeconds(2)), "Observed position change creates a new pot update");
        Tick(11000);
        check(flags.Last() == shifted.Position, "Position refinement automatically moves the flag");
        var unchangedCalls = calls;
        pot.ObserveReveal([shifted], time.AddSeconds(3)); Tick(15000);
        check(calls == unchangedCalls, "Unchanged revealed coffer does not spam flags");
        pot.ObserveReveal([], time.AddSeconds(4)); Tick(20000);
        check(calls == unchangedCalls && pot.Target is null, "Unloaded coffer cannot schedule a stale target");
        pot.UpdateBuff(false, 1252, Vector3.Zero, pads, []); Tick(21000);
        check(!automation.OwnsNavigation && calls == unchangedCalls, "Buff loss releases general route navigation");

        Start(); Tick(0, enabled: false);
        pot.Apply(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, time); Tick(500, enabled: false);
        check(calls == 0 && pot.Target == pads[1].Position && !automation.OwnsNavigation, "Disabled flagging still tracks hints without taking over navigation");
        Tick(1000);
        check(flags.SequenceEqual([pads[1].Position]), "Enabling auto tracking flags the latest target immediately");
        Tick(1500, enabled: false); Tick(2000);
        check(calls == 2, "Disable/re-enable resynchronizes an already acknowledged revision");

        Start(); Tick(0, canFlag: false); Tick(500, canFlag: false);
        check(calls == 0 && automation.OwnsNavigation, "Casting or interaction delays flagging without losing the pending target");
        Tick(1000);
        check(calls == 1, "Flag automatically resumes after interaction ends");

        Start(); Tick(0, success: false); Tick(500, success: false); Tick(1500, success: false); Tick(3000, success: false);
        check(calls == 3 && flags.Count == 0, "Failed flags get three spaced initial attempts");
        Tick(4500); Tick(12500);
        check(calls == 3, "Persistent failure backs off for ten seconds");
        Tick(13000); Tick(24000);
        check(calls == 4 && flags.Count == 1, "Temporary map failure recovers automatically after the initial retries");

        Start(); Tick(0, success: false);
        pot.Apply(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, time);
        Tick(1500);
        check(flags.SequenceEqual([pads[1].Position]), "New hint replaces an old failed flag target");
        pot.Apply(new(PotHintKind.Direction, 5, "很近"), Vector3.Zero, time); Tick(3000);
        check(calls == 2 && pot.Target is null && automation.OwnsNavigation, "Conflicting hint cancels flag without guessing a replacement");
        pot.Apply(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, time); Tick(4500);
        check(flags.Last() == pads[1].Position && calls == 3, "Next valid hint resumes automatic flagging without manual reset");
        pot.Apply(new(PotHintKind.Expired), Vector3.Zero, time); Tick(6000);
        check(!automation.OwnsNavigation && calls == 3, "Expired treasure cancels automatic search and releases navigation");

        Start();
        try { automation.Update(pot, true, true, 0, () => { calls++; throw new InvalidOperationException("Test failure"); }); }
        catch (InvalidOperationException) { }
        Tick(500);
        check(calls == 1, "Map API exception still obeys retry throttle");
        Tick(1500);
        check(calls == 2 && flags.Count == 1, "Exception can recover on the next automatic attempt");

        Start(); pot.Reset();
        inbox.Enqueue(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, 1252, 1, time);
        inbox.ApplyTo(pot, 1252, 1, time);
        pot.UpdateBuff(true, 1252, new(200, 0, 0), pads, []);
        inbox.ApplyTo(pot, 1252, 1, time.AddMilliseconds(500)); Tick(500);
        check(pot.Target == pads[1].Position && flags.Count == 1, "Hint before buff scan is retained and uses captured origin rather than later player position");
        var revision = pot.Revision;
        inbox.Enqueue(new(PotHintKind.Direction, 1, "很遠"), new(200, 0, 0), 1252, 1, time.AddMilliseconds(100));
        inbox.ApplyTo(pot, 1252, 1, time.AddMilliseconds(500));
        check(pot.Revision == revision, "Duplicate chat/toast hint cannot filter again using a moved origin");
        inbox.Enqueue(new(PotHintKind.Direction, 1, "很遠"), Vector3.Zero, 1252, 1, time.AddSeconds(2));
        inbox.ApplyTo(pot, 1252, 1, time.AddSeconds(2));
        check(pot.Revision == revision + 1, "A later repeated real hint is accepted");

        Start(); revision = pot.Revision;
        inbox.Enqueue(new(PotHintKind.Bonus), Vector3.Zero, 1252, 2, time);
        inbox.Enqueue(new(PotHintKind.Bonus), Vector3.Zero, 1346, 1, time);
        inbox.Enqueue(new(PotHintKind.Bonus), Vector3.Zero, 1252, 1, time.AddSeconds(-4));
        inbox.ApplyTo(pot, 1252, 1, time);
        check(pot.Revision == revision, "Stale hints and hints from another territory or instance are discarded");
        inbox.Enqueue(new(PotHintKind.Bonus), new(float.NaN, 0, 0), 1252, 1, time);
        inbox.ApplyTo(pot, 1252, 1, time);
        check(pot.Revision == revision, "Non-finite hint origins cannot change the search");
        inbox.Enqueue(new(PotHintKind.Bonus), Vector3.Zero, 1252, 1, time);
        inbox.Clear(); inbox.ApplyTo(pot, 1252, 1, time);
        check(pot.Revision == revision, "Reset clears queued hints before entering another session");

        pot.Reset();
        pot.UpdateBuff(true, 1252, Vector3.Zero, pads, [coffer with { Targetable = false }]);
        pot.Apply(new(PotHintKind.Reveal), Vector3.Zero, time);
        check(pot.ObserveReveal([coffer], time.AddSeconds(1)) && pot.Revealed, "Previously untargetable placeholder can become the personally revealed coffer");
        check(PotHints.Parse("很想要聖靈藥。\n使用任務道具，探知財寶的氣息吧！")?.Kind == PotHintKind.Prompt, "Multiline notification prompt is parsed");
    }
}
