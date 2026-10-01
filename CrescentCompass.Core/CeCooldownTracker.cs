namespace CrescentCompass.Core;

public enum CePhase { Inactive, Register, Warmup, Battle }
public enum CeStatus { Unknown, Register, Warmup, Battle, ConfirmingEnd, EndUnobserved, Cooldown, Eligible }
public sealed record CeDefinition(ushort Id, string Name, bool MobTriggered, string? TriggerMob = null, string? TriggerArea = null)
{
    public TimeSpan Cooldown => TimeSpan.FromMinutes(MobTriggered ? 60 : 120);
    public string TriggerCondition => MobTriggered ? $"擊倒 {TriggerMob} · 約 {TriggerArea}" : "隨時間自動出現，等待系統刷新";
}
public sealed record CeObservation(ushort Id, CePhase Phase, int Progress = 0);
public sealed record CeCooldownEntry(CeDefinition Definition, CeStatus Status, int Progress,
    DateTimeOffset? LastSeen, DateTimeOffset? EndedAt, DateTimeOffset? EligibleAt);
public sealed record CeCooldownSnapshot(bool ScanFresh, IReadOnlyList<CeCooldownEntry> Entries);

/// <summary>Observed battle-to-inactive transitions, never server cooldowns or spawn predictions.</summary>
public sealed class CeCooldownTracker
{
    public const ushort Territory = 1252;
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan EndConfirmation = TimeSpan.FromSeconds(2);
    // DynamicEvent 33–47; names checked against the TC client's EXH column 11 (string at offset 0).
    // 60/120 minutes are community estimates, measured here from the observed end.
    public static IReadOnlyList<CeDefinition> Definitions { get; } = Array.AsReadOnly<CeDefinition>([
        new(33, "腦髓愛好者——奪心魔", true, "新月鬼魚", "X 26 / Y 33"),
        new(34, "黑色連隊", false),
        new(35, "憤怒的人造人——新月狂戰士", false),
        new(36, "潛影撕裂者——死亡厲爪", false),
        new(37, "掙脫封印的大妖異——回廊惡魔", true, "新月墨漬", "X 14 / Y 35"),
        new(38, "擬造使魔——水晶龍", false),
        new(39, "雙極的造物——神秘土偶", true, "新月比布羅斯", "X 5 / Y 25"),
        new(40, "石製騎士團", false),
        new(41, "傳說中的鯊魚——尼姆瓣齒鯊", true, "新月小瓣齒鯊", "X 19 / Y 6"),
        new(42, "雙足獅人——躍立獅", true, "新月風扇", "X 35 / Y 21"),
        new(43, "防衛指令", false),
        new(44, "厭鳥巨獸——進化加魯拉", true, "新月加魯拉", "X 31 / Y 8"),
        new(45, "販賣詛咒的商販——金錢龜", false),
        new(46, "城塞守衛——復原獅像", false),
        new(47, "昏暗妖魂——鬼火苗", false),
    ]);

    private sealed class Record
    {
        public CeStatus Status;
        public int Progress;
        public DateTimeOffset? LastSeen, EndedAt, PendingEnd;
        public bool SawBattle;
    }
    private readonly Dictionary<ushort, Record> records = [];
    private ushort territory;
    private uint instance;
    private DateTimeOffset? lastScan;
    public static CeDefinition? Find(ushort id) => Definitions.FirstOrDefault(d => d.Id == id);

    public void Reset()
    { records.Clear(); territory = 0; instance = 0; lastScan = null; }

    public void Suspend()
    {
        lastScan = null;
        foreach (var record in records.Values.Where(r => IsLive(r.Status) || r.PendingEnd is not null))
        {
            record.Status = CeStatus.EndUnobserved;
            record.PendingEnd = null;
            record.SawBattle = false;
        }
    }

    public void Update(ushort currentTerritory, uint currentInstance, IReadOnlyList<CeObservation> observations, DateTimeOffset now)
    {
        if (currentTerritory != territory || currentInstance != instance)
        { Reset(); territory = currentTerritory; instance = currentInstance; }
        if (territory != Territory) { Reset(); return; }
        // An empty/uninitialised container, duplicate IDs or invalid state cannot prove an event ended.
        var rows = observations.Where(o => Find(o.Id) is not null).ToArray();
        if (rows.Length == 0 || rows.Any(o => !Enum.IsDefined(o.Phase) || o.Progress is < 0 or > 100) ||
            rows.Select(o => o.Id).Distinct().Count() != rows.Length)
        { Suspend(); return; }
        if (lastScan is { } scanned && (now < scanned || now - scanned > Freshness)) Suspend();
        var seen = rows.Select(o => o.Id).ToHashSet();
        foreach (var missing in records.Where(r => !seen.Contains(r.Key)).Select(r => r.Value))
            if (IsLive(missing.Status) || missing.PendingEnd is not null)
            { missing.Status = CeStatus.EndUnobserved; missing.PendingEnd = null; missing.SawBattle = false; }

        foreach (var row in rows)
        {
            if (!records.TryGetValue(row.Id, out var record)) records[row.Id] = record = new();
            if (row.Phase != CePhase.Inactive)
            {
                // A new registration invalidates the prior occurrence, including any old cooldown.
                if (row.Phase == CePhase.Register || !IsLive(record.Status)) record.SawBattle = false;
                record.Status = row.Phase switch { CePhase.Register => CeStatus.Register, CePhase.Warmup => CeStatus.Warmup, _ => CeStatus.Battle };
                record.SawBattle |= row.Phase == CePhase.Battle;
                record.Progress = row.Progress; record.LastSeen = now;
                record.EndedAt = null; record.PendingEnd = null;
                continue;
            }
            if (!record.SawBattle)
            {
                if (IsLive(record.Status)) record.Status = CeStatus.EndUnobserved;
                continue;
            }
            record.PendingEnd ??= now;
            record.Status = CeStatus.ConfirmingEnd;
            if (now - record.PendingEnd.Value < EndConfirmation) continue;
            record.EndedAt = record.PendingEnd;
            record.PendingEnd = null; record.SawBattle = false;
            record.Status = CeStatus.Cooldown;
        }
        lastScan = now;
    }

    public CeCooldownSnapshot Snapshot(DateTimeOffset now)
    {
        var fresh = lastScan is { } scanned && now >= scanned && now - scanned <= Freshness;
        return new(fresh, Definitions.Select(d =>
        {
            records.TryGetValue(d.Id, out var record);
            var eligible = record?.EndedAt + d.Cooldown;
            var status = record?.Status ?? CeStatus.Unknown;
            if (!fresh && (IsLive(status) || status == CeStatus.ConfirmingEnd)) status = CeStatus.EndUnobserved;
            if (eligible is { } at) status = now >= at ? CeStatus.Eligible : CeStatus.Cooldown;
            return new CeCooldownEntry(d, status, record?.Progress ?? 0, record?.LastSeen, record?.EndedAt, eligible);
        }).ToArray());
    }

    private static bool IsLive(CeStatus status) => status is CeStatus.Register or CeStatus.Warmup or CeStatus.Battle;
}
