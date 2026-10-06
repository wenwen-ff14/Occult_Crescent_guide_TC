using System.Numerics;
using CrescentCompass.Core;

internal static class PotFateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var tracker = new PotFateTracker();
        PotFateObservation Fate(ushort id = 1976, ushort territory = 1252, long? epoch = null,
            PotFatePhase phase = PotFatePhase.Running, int progress = 0) =>
            new(id, territory, phase, epoch ?? start.ToUnixTimeSeconds(), 900, progress, new(200, 111, -215));
        IReadOnlyList<PotFateLive> Scan(int seconds, PotFateObservation[]? rows = null, bool notify = true, ushort territory = 1252, uint instance = 1) =>
            tracker.Update(territory, instance, rows ?? [], start.AddSeconds(seconds), notify);

        check(PotFateTracker.Definitions.Count == 4 && PotFateTracker.Definitions.All(d => PotFateTracker.Find(d.NextId, d.Territory)?.NextId == d.Id),
            "All four pot FATE IDs alternate only within their own Horn");
        check(PotFateTracker.Find(1976, 1252)?.Name == "幸福的魔法甕" && PotFateTracker.Find(1977, 1252)?.Name == "瑟瑟發抖的魔法甕",
            "South pot FATE names match the local TC client audit");
        check(PotFateTracker.Find(1976, 1346) is null && PotFateTracker.Find(1963, 1252) is null, "Wrong zone and non-pot IDs are excluded");
        Scan(0);
        check(tracker.Snapshot(start) is { ExpectedAt: null, Next: null, Active.Count: 0 }, "An empty new instance has no invented countdown");
        check(Scan(0, [Fate(1963), Fate(1977, 1346)]).Count == 0 && tracker.Snapshot(start).ExpectedAt is null,
            "Ordinary FATEs and mismatched territories cannot start a pot timer");

        var notices = Scan(0, [Fate()]);
        var snapshot = tracker.Snapshot(start);
        check(notices.Count == 1 && notices[0].Name == "幸福的魔法甕", "First observed pot FATE produces one notification");
        check(snapshot.Active.Count == 1 && snapshot.Next?.Id == 1977 && snapshot.ExpectedAt == start.AddMinutes(30) && snapshot.UsesGameStart,
            "Game start time anchors a 30-minute estimate for the opposite side");
        check(snapshot.Active[0].EndsAt == start.AddMinutes(15), "Current event end time remains separate from next spawn estimate");
        check(Scan(1, [Fate(progress: 20)]).Count == 0 && tracker.Snapshot(start.AddSeconds(1)).Active[0].Progress == 20,
            "Repeated scan updates progress without duplicate spawn notification");
        check(Scan(2, [Fate(), Fate()]).Count == 0 && tracker.Snapshot(start.AddSeconds(2)).Active.Count == 1,
            "Duplicate rows cannot duplicate an event or notification");
        Scan(3);
        check(tracker.Snapshot(start.AddSeconds(3)).Active.Count == 0 && tracker.Snapshot(start.AddSeconds(3)).ExpectedAt == start.AddMinutes(30),
            "Temporary table absence does not discard the observed timer");
        check(Scan(4, [Fate()]).Count == 0, "Reappearing table row with the same start is not a new FATE");
        check(Scan(10, [Fate(phase: PotFatePhase.Finished)]).Count == 0 && tracker.Snapshot(start.AddSeconds(10)).Active.Count == 0,
            "Ended or failed FATE is never advertised as active");
        check(tracker.Snapshot(start.AddSeconds(10)).ExpectedAt == start.AddMinutes(30), "Completion time does not reset a spawn-based countdown");
        Scan(1800);
        snapshot = tracker.Snapshot(start.AddMinutes(30));
        check(snapshot.Next?.Id == 1977 && snapshot.ExpectedAt == start.AddMinutes(30), "Reaching zero retains overdue estimate instead of inventing another cycle");
        check(Scan(5400).Count == 0 && tracker.Snapshot(start.AddMinutes(90)).ExpectedAt == start.AddMinutes(30),
            "Unobserved predicted spawns never trigger notifications or extend the timer");

        tracker.Reset();
        Scan(0, [Fate()]);
        notices = Scan(1805, [Fate(1977, epoch: start.AddSeconds(1805).ToUnixTimeSeconds())]);
        snapshot = tracker.Snapshot(start.AddSeconds(1805));
        check(notices.Count == 1 && snapshot.Next?.Id == 1976 && snapshot.ExpectedAt == start.AddSeconds(3605),
            "Actual opposite-side spawn recalibrates both next side and countdown");
        notices = Scan(3610, [Fate(epoch: start.AddSeconds(3610).ToUnixTimeSeconds())]);
        check(notices.Count == 1 && tracker.Snapshot(start.AddSeconds(3610)).Next?.Id == 1977,
            "Same ID on a later cycle produces a fresh notification");
        check(Scan(3611, [Fate()]).Count == 0, "Older row cannot rewind an already observed later generation");

        tracker.Reset();
        notices = Scan(120, [Fate(progress: 45)]);
        snapshot = tracker.Snapshot(start.AddSeconds(120));
        check(notices.Count == 1 && snapshot.ExpectedAt == start.AddMinutes(30), "Joining an active FATE uses its real start, not join time plus 30 minutes");
        tracker.Reset();
        check(Scan(120, [Fate(phase: PotFatePhase.Finished)]).Count == 0 && tracker.Snapshot(start.AddSeconds(120)).ExpectedAt == start.AddMinutes(30),
            "A finished row may supply a valid timing anchor without an appearance notification");
        tracker.Reset();
        check(Scan(120, [Fate(progress: 100)]).Count == 0 && tracker.Snapshot(start.AddSeconds(120)).Active.Count == 0,
            "Stale Running state at 100 percent is not announced");
        tracker.Reset();
        check(Scan(901, [Fate()]).Count == 0 && tracker.Snapshot(start.AddSeconds(901)).Active.Count == 0,
            "Expired event duration is not announced as active");

        tracker.Reset();
        notices = Scan(120, [Fate(epoch: 0, phase: PotFatePhase.Preparation)]);
        snapshot = tracker.Snapshot(start.AddSeconds(120));
        check(notices.Count == 1 && snapshot.Active[0].Preparing && !snapshot.UsesGameStart && snapshot.ExpectedAt == start.AddSeconds(1920),
            "Missing game timestamp uses explicitly approximate first observation time");
        check(Scan(121, [Fate(epoch: 0, phase: PotFatePhase.Preparation)]).Count == 0,
            "Preparation without timestamp does not notify every scan");
        check(Scan(122, [Fate()]).Count == 0 && tracker.Snapshot(start.AddSeconds(122)).UsesGameStart &&
            tracker.Snapshot(start.AddSeconds(122)).ExpectedAt == start.AddMinutes(30),
            "A later valid game timestamp refines the estimate without duplicate notification");
        tracker.Reset();
        Scan(0, [Fate(epoch: 0)]);
        check(Scan(3600, [Fate(epoch: 0)]).Count == 1, "Timestamp-less same-side observation can identify a new hourly cycle");
        tracker.Reset();
        check(Scan(0, [Fate(epoch: 0, phase: PotFatePhase.Finished)]).Count == 0 && tracker.Snapshot(start).ExpectedAt is null,
            "Finished row with unknown start cannot fabricate a timer");
        tracker.Reset();
        Scan(0, [Fate(epoch: long.MaxValue)]);
        check(!tracker.Snapshot(start).UsesGameStart && tracker.Snapshot(start).ExpectedAt == start.AddMinutes(30),
            "Corrupted or future epoch is rejected before DateTime conversion");

        tracker.Reset();
        check(Scan(0, [Fate()], notify: false).Count == 0 && tracker.Snapshot(start).ExpectedAt is not null,
            "Disabling notifications preserves countdown tracking");
        check(Scan(1, [Fate()], notify: true).Count == 0, "Enabling notifications does not replay an already observed event");
        check(Scan(1800, [Fate(1977, epoch: start.AddMinutes(30).ToUnixTimeSeconds())]).Count == 1,
            "Future spawns notify after re-enabling");
        Scan(1801, instance: 2);
        check(tracker.Snapshot(start.AddSeconds(1801)).ExpectedAt is null, "Changing instance discards prior timer");
        notices = Scan(1802, [Fate(epoch: start.AddSeconds(1802).ToUnixTimeSeconds())], instance: 2);
        check(notices.Count == 1, "New instance can announce the same FATE ID again");
        Scan(1803, territory: 1346, instance: 2);
        check(tracker.Snapshot(start.AddSeconds(1803)).Next is null, "Changing Horn clears the previous Horn's prediction");
        Scan(1804, [Fate(2072, 1346, start.AddSeconds(1804).ToUnixTimeSeconds())], territory: 1346, instance: 2);
        check(tracker.Snapshot(start.AddSeconds(1804)).Next?.Id == 2073, "North Horn alternates its own pair of IDs");
        Scan(1805, territory: 1);
        check(tracker.Snapshot(start.AddSeconds(1805)).ExpectedAt is null, "Leaving supported areas clears the timer");

        tracker.Reset(); Scan(0, [Fate()]);
        snapshot = tracker.Snapshot(start.AddSeconds(4));
        check(!snapshot.ScanFresh && snapshot.Active.Count == 0 && snapshot.ExpectedAt == start.AddMinutes(30),
            "Loading or scan interruption hides stale live data but retains same-instance estimate");
        check(Scan(5, [Fate()]).Count == 0 && tracker.Snapshot(start.AddSeconds(5)).Active.Count == 1,
            "Resuming same-instance scanning does not repeat notifications");
        tracker.Reset();
        check(tracker.Snapshot(start.AddSeconds(6)).ExpectedAt is null, "Explicit territory transition reset prevents reuse when instance number is recycled");
        check(Scan(7, [Fate(phase: (PotFatePhase)99)]).Count == 0 && tracker.Snapshot(start.AddSeconds(7)).Next is null,
            "Unknown FATE phase cannot create a false active event");

        tracker.Reset(); Scan(0);
        check(tracker.TakeUpcomingReminder(start, true) is null, "Unknown countdown cannot produce a five-minute reminder");
        Scan(0, [Fate()]); Scan(1499);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1499), true) is null, "Five-minute reminder does not fire one second early");
        Scan(1500);
        var reminder = tracker.TakeUpcomingReminder(start.AddSeconds(1500), true);
        check(reminder?.Definition.Id == 1977 && reminder.ExpectedAt == start.AddMinutes(30),
            "Exactly five minutes remaining reminds for the next south pot, not the observed north pot");
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true) is null, "Repeated reminder polling cannot duplicate the notice");
        Scan(1501);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1501), true) is null, "Fresh scans in the same reminder window cannot repeat the notice");
        check(Scan(1800, [Fate(1977, epoch: start.AddMinutes(30).ToUnixTimeSeconds())]).Count == 1,
            "The earlier reminder does not suppress the separate actual-spawn notification");
        check(tracker.TakeUpcomingReminder(start.AddMinutes(30), true) is null, "Observing the next spawn replaces the old reminder deadline");
        Scan(3300);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(3300), true)?.Definition.Id == 1976, "A new observed cycle can remind for the north pot");
        Scan(3600, [Fate(epoch: start.AddHours(1).ToUnixTimeSeconds())]); Scan(5100);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(5100), true)?.Definition.Id == 1977, "The same FATE ID can remind again in a later cycle");

        tracker.Reset(); Scan(0, [Fate()], notify: false); Scan(1500, notify: false);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true) is not null, "Five-minute reminders work with spawn notifications disabled");
        tracker.Reset(); Scan(0, [Fate()]); Scan(1500);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), false) is null, "Muted reminder does not emit a notification");
        Scan(1501);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1501), true) is null, "Re-enabling reminders does not replay a muted cycle");
        check(tracker.Snapshot(start.AddSeconds(1501)).ExpectedAt == start.AddMinutes(30), "Muting a reminder does not discard the countdown");

        tracker.Reset(); Scan(0, [Fate()]);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true) is null, "Loading or failed scans cannot announce from stale state");
        Scan(1560);
        reminder = tracker.TakeUpcomingReminder(start.AddSeconds(1560), true);
        check(reminder?.ExpectedAt - start.AddSeconds(1560) == TimeSpan.FromMinutes(4), "Resuming inside the window emits once with the actual remaining time");
        tracker.Reset(); Scan(0, [Fate()]); Scan(1800);
        check(tracker.TakeUpcomingReminder(start.AddMinutes(30), true) is null, "No five-minute reminder at the estimated spawn deadline");
        Scan(5400);
        check(tracker.TakeUpcomingReminder(start.AddMinutes(90), true) is null, "Overdue estimates do not invent reminders for later cycles");

        tracker.Reset(); Scan(120, [Fate(epoch: 0)]); Scan(1620);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1620), true)?.ExpectedAt == start.AddSeconds(1920),
            "First-observation estimates also support five-minute reminders");
        Scan(1621, [Fate()]);
        check(tracker.Snapshot(start.AddSeconds(1621)).ExpectedAt == start.AddMinutes(30) &&
            tracker.TakeUpcomingReminder(start.AddSeconds(1621), true) is null, "Correcting a previously reminded cycle cannot replay its notice");
        tracker.Reset(); Scan(120, [Fate(epoch: 0)]); Scan(1500, [Fate()]);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true)?.ExpectedAt == start.AddMinutes(30),
            "A newly corrected deadline inside the window can emit its first reminder");

        tracker.Reset(); Scan(0, [Fate()]); Scan(1500, instance: 2);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true) is null, "Changing island instance clears the previous countdown reminder");
        Scan(1501, [Fate(epoch: start.AddSeconds(1501).ToUnixTimeSeconds())], instance: 2); Scan(3001, instance: 2);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(3001), true) is not null, "A newly observed island instance gets its own reminder");
        tracker.Reset();
        check(tracker.TakeUpcomingReminder(start.AddSeconds(3001), true) is null, "Logout or explicit reset clears pending reminders");
        Scan(0, [Fate(2072, 1346)], territory: 1346); Scan(1500, territory: 1346);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1499), true) is null, "A backward clock cannot emit before the latest observation");
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1500), true)?.Definition.Id == 2073, "North Horn reminders use its own pot definitions");
        Scan(1501, territory: 1);
        check(tracker.TakeUpcomingReminder(start.AddSeconds(1501), true) is null, "Leaving supported areas clears reminder state");
    }
}
