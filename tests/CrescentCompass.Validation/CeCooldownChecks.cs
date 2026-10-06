using CrescentCompass.Core;

internal static class CeCooldownChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(CeMapCatalog.All.Select(p => p.Id).Order().SequenceEqual(CeCooldownTracker.Definitions.Select(d => d.Id).Order()), "Map contains all and only South Horn CE IDs");
        check(CeMapCatalog.All.Count(p => p.TriggerMapPosition is not null) == 6, "Map has six distinct trigger destinations");
        foreach (var definition in CeCooldownTracker.Definitions)
            check((CeMapCatalog.FlagPosition(definition.Id, true) is not null) == definition.MobTriggered, $"CE {definition.Id}: only mob triggers have a trigger flag");
        check(System.Numerics.Vector2.Distance(CeMapCatalog.ToMap(new(300, 730)), new(27.48f, 36.08f)) < 0.001f, "Audited CE world position projects to map 967 coordinates");
        check(CeMapCatalog.FlagPosition(33, false) == new System.Numerics.Vector2(300, 730), "Boss flag preserves audited world X/Z");
        check(System.Numerics.Vector2.Distance(CeMapCatalog.FlagPosition(33, true)!.Value, new(226, 576)) < 0.01f, "Crescent Monk flag uses trigger coordinates, not the CE arena");
        check(System.Numerics.Vector2.Distance(CeMapCatalog.FlagPosition(39, true)!.Value, new(-824, 176)) < 0.01f, "Western trigger map link preserves negative world coordinates");
        check(CeMapCatalog.FlagPosition(48, false) is null && CeMapCatalog.FlagPosition(1976, true) is null, "Tower and pot cannot become CE flag destinations");
        var time = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var tracker = new CeCooldownTracker();
        CeObservation Row(ushort id = 33, CePhase phase = CePhase.Battle, int progress = 0) => new(id, phase, progress);
        void Scan(double seconds, CeObservation[] rows, uint instance = 1, ushort territory = 1252) => tracker.Update(territory, instance, rows, time.AddSeconds(seconds));
        CeCooldownEntry Entry(double seconds, ushort id = 33) => tracker.Snapshot(time.AddSeconds(seconds)).Entries.Single(e => e.Definition.Id == id);
        check(CeCooldownTracker.Definitions.Count == 15 && CeCooldownTracker.Definitions.Select(d => d.Id).Distinct().Count() == 15, "South Horn has 15 distinct CE IDs; pots and tower excluded");
        check(CeCooldownTracker.Definitions.Count(d => d.MobTriggered) == 6 && CeCooldownTracker.Definitions.Count(d => !d.MobTriggered) == 9, "Six mob-triggered and nine automatic CE cooldown categories");
        check(CeCooldownTracker.Find(33)?.Cooldown == TimeSpan.FromHours(1) && CeCooldownTracker.Find(40)?.Cooldown == TimeSpan.FromHours(2), "Community cooldown estimates assigned by trigger type");
        check(CeCooldownTracker.Find(1976) is null && CeCooldownTracker.Find(48) is null, "Ordinary FATEs and tower do not enter CE timers");
        Scan(0, [Row(phase: CePhase.Inactive)]);
        check(Entry(0) is { Status: CeStatus.Unknown, EndedAt: null, EligibleAt: null }, "Joining an inactive island never invents a cooldown");
        Scan(1, [Row(phase: CePhase.Register)]);
        check(Entry(1).Status == CeStatus.Register, "Registration is displayed without starting cooldown");
        Scan(2, [Row(phase: CePhase.Warmup)]);
        check(Entry(2).Status == CeStatus.Warmup, "Warmup distinct from battle");
        Scan(3, [Row(progress: 100)]);
        check(Entry(3) is { Status: CeStatus.Battle, EligibleAt: null }, "100 percent alone cannot start cooldown");
        Scan(4, [Row(phase: CePhase.Inactive)]);
        check(Entry(4).Status == CeStatus.ConfirmingEnd, "Inactive transition waits for confirmation");
        Scan(5, [Row(phase: CePhase.Inactive)]);
        check(Entry(5).EndedAt is null, "One second of inactivity insufficient");
        Scan(6, [Row(phase: CePhase.Inactive)]);
        check(Entry(6).EndedAt == time.AddSeconds(4) && Entry(6).EligibleAt == time.AddSeconds(3604), "Confirmed end anchors at first inactive sample, not delayed confirmation");
        Scan(7, [Row(phase: CePhase.Inactive)]);
        check(Entry(7).EligibleAt == time.AddSeconds(3604), "Polling does not restart cooldown");
        check(Entry(3604).Status == CeStatus.Eligible && Entry(7200).EligibleAt == time.AddSeconds(3604), "Expired cooldown stays eligible; no fabricated event or repeated timer");
        tracker.Suspend();
        check(Entry(10).EligibleAt == time.AddSeconds(3604) && !tracker.Snapshot(time.AddSeconds(10)).ScanFresh, "Loading preserves known cooldown while flagging stale scans");
        Scan(12, [Row(phase: CePhase.Register)]);
        check(Entry(12).EligibleAt is null && Entry(12).EndedAt is null, "A real new occurrence replaces the old cooldown immediately");
        Scan(13, [Row(phase: CePhase.Inactive)]);
        check(Entry(13) is { Status: CeStatus.EndUnobserved, EligibleAt: null }, "Cancelled registration without battle is not assumed to have normal cooldown");

        tracker.Reset();
        Scan(0, [Row(40, CePhase.Battle, 30)]);
        Scan(1, [Row(40, CePhase.Inactive)]); Scan(3, [Row(40, CePhase.Inactive)]);
        check(Entry(3, 40).EligibleAt == time.AddSeconds(7201), "Automatic CE receives two-hour estimate");

        tracker.Reset();
        Scan(0, [Row()]); Scan(1, [Row(phase: CePhase.Inactive)]); Scan(2, [Row()]);
        check(Entry(2) is { Status: CeStatus.Battle, EndedAt: null }, "Transient inactive frame cancels pending finish");
        Scan(3, [Row(phase: CePhase.Inactive)]); Scan(5, [Row(phase: CePhase.Inactive)]);
        check(Entry(5).EndedAt == time.AddSeconds(3), "Later stable inactive transition uses the correct end");

        foreach (var interruption in new[] { "suspend", "gap", "empty", "missing", "invalid", "duplicate", "clock" })
        {
            tracker.Reset(); Scan(0, [Row()]);
            switch (interruption)
            {
                case "suspend": tracker.Suspend(); break;
                case "gap": Scan(30, [Row(phase: CePhase.Inactive)]); break;
                case "empty": Scan(1, []); break;
                case "missing": Scan(1, [Row(40, CePhase.Inactive)]); break;
                case "invalid": Scan(1, [Row(phase: (CePhase)99)]); break;
                case "duplicate": Scan(1, [Row(), Row(phase: CePhase.Inactive)]); break;
                case "clock": Scan(-1, [Row(phase: CePhase.Inactive)]); break;
            }
            Scan(31, [Row(phase: CePhase.Inactive)]); Scan(33, [Row(phase: CePhase.Inactive)]);
            check(Entry(33) is { Status: CeStatus.EndUnobserved, EligibleAt: null }, $"{interruption}: lost observations cannot fabricate finish time");
            Scan(34, [Row()]); Scan(35, [Row(phase: CePhase.Inactive)]); Scan(37, [Row(phase: CePhase.Inactive)]);
            check(Entry(37).EndedAt == time.AddSeconds(35), $"{interruption}: active re-observation restores reliable end tracking");
        }
        tracker.Reset(); Scan(0, [Row()]); Scan(1, [Row(phase: CePhase.Inactive)]); tracker.Suspend();
        Scan(2, [Row(phase: CePhase.Inactive)]); Scan(4, [Row(phase: CePhase.Inactive)]);
        check(Entry(4).EligibleAt is null, "Loading during finish confirmation cancels pending evidence");
        tracker.Reset(); Scan(0, [Row()]);
        check(Entry(4).Status == CeStatus.EndUnobserved && !tracker.Snapshot(time.AddSeconds(4)).ScanFresh, "Stale live state is never advertised as currently active");
        Scan(1, [Row(phase: CePhase.Inactive)], instance: 2);
        check(Entry(1) is { Status: CeStatus.Unknown, LastSeen: null }, "Changing instances clears CE history");
        Scan(2, [Row()], instance: 2); Scan(3, [Row()], territory: 1346);
        check(Entry(3).Status == CeStatus.Unknown, "North Horn never reuses South Horn cooldowns");
        Scan(4, [Row()]); tracker.Reset();
        check(Entry(4) is { Status: CeStatus.Unknown, LastSeen: null, EndedAt: null }, "Logout/clear/reload reset all CE records");
        Scan(5, [Row(progress: 101)]);
        check(!tracker.Snapshot(time.AddSeconds(5)).ScanFresh && Entry(5).EligibleAt is null, "Malformed progress cannot supply evidence");
    }
}
