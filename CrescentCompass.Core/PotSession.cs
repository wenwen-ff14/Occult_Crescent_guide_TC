using System.Numerics;

namespace CrescentCompass.Core;

/// <summary>Candidate search, never an assertion of a hidden chest's actual location.</summary>
public sealed class PotSession
{
    public const uint StatusId = 1531;
    private List<PotCandidate> all = [];
    private readonly HashSet<ulong> baselineObjects = [];
    private DateTimeOffset? revealAt;
    private Vector3 revealOrigin;
    private bool suppressed;
    private bool buff;
    private ulong revealedObjectId;
    public bool Active { get; private set; }
    public bool Searching => Active && !suppressed;
    public ushort Territory { get; private set; }
    public IReadOnlyList<PotCandidate> Candidates { get; private set; } = [];
    public Vector3? Target { get; private set; }
    public bool Revealed { get; private set; }
    public string Detail { get; private set; } = "取得魔法罐後自動開始搜尋。";
    public int Revision { get; private set; }

    public void Reset()
    {
        Active = buff = suppressed = Revealed = false;
        Territory = 0; all.Clear(); baselineObjects.Clear(); Candidates = []; Target = null; revealAt = null; revealedObjectId = 0;
        Detail = "取得魔法罐後自動開始搜尋。"; Revision++;
    }

    public bool UpdateBuff(bool present, ushort territory, Vector3 position, IEnumerable<PotCandidate> catalog, IEnumerable<Observation> seen)
    {
        if (!present || !SpotCatalog.IsSupported(territory))
        {
            if (buff || Active) Reset();
            return false;
        }
        if (buff && Territory == territory) return false;
        Reset(); buff = Active = true; Territory = territory;
        all = catalog.Where(c => c.Territory == territory).ToList();
        baselineObjects.UnionWith(seen.Where(o => CofferKinds.IsPot(o.Kind) && o.Available && o.Targetable).Select(o => o.ObjectId));
        // We may load the plugin mid-session; start with both primary and bonus pads.
        Candidates = all.DistinctBy(c => c.Position).ToArray(); SelectCandidate(position);
        Detail = "已取得尋寶狀態。先標搜尋候選點；使用聖靈藥後會依方向縮小範圍。";
        Revision++;
        return true;
    }

    private void SelectCandidate(Vector3 position)
    {
        Target = Candidates.OrderBy(c => Vector3.DistanceSquared(position, c.Position)).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault()?.Position;
        Revealed = false;
    }

    public bool Apply(PotHint hint, Vector3 origin, DateTimeOffset now)
    {
        if (!Active || !Coordinates.IsFinite(origin)) return false;
        switch (hint.Kind)
        {
            case PotHintKind.Expired:
                Target = null; Candidates = []; revealAt = null; suppressed = true; Revealed = false;
                Detail = "財寶已消失。等待新的提示或下一次取得魔法罐。";
                break;
            case PotHintKind.Bonus:
                Candidates = all.Where(c => c.Bonus).DistinctBy(c => c.Position).ToArray(); revealAt = null; suppressed = false;
                SelectCandidate(origin); Detail = "第二處財寶：已切換加碼候選點。請再次使用聖靈藥。";
                break;
            case PotHintKind.Reveal:
                revealAt = now; revealOrigin = origin; Target = null; Revealed = false; suppressed = false;
                Detail = "收到發現財寶提示，正在確認附近可互動的寶箱。";
                break;
            case PotHintKind.Prompt:
                return false;
            case PotHintKind.Direction:
                if (hint.Direction is < 1 or > 8) return false;
                var pool = suppressed ? all : Candidates;
                var narrowed = pool.Where(c => MatchesDirection(origin, c.Position, hint.Direction)).ToArray();
                // Conflicts invalidate the target; a later valid hint can recover without a plugin button.
                Target = null; Revealed = false; revealAt = null;
                if (narrowed.Length == 0)
                {
                    Detail = "提示與現有候選點不符；等待下一次有效方向提示，再自動更新。";
                    break;
                }
                Candidates = narrowed; suppressed = false; SelectCandidate(origin);
                Detail = $"方向 {DirectionName(hint.Direction)} · {hint.Distance} · 剩餘 {Candidates.Count} 個候選，尚未確認寶箱。";
                break;
        }
        Revision++; return true;
    }

    public bool ObserveReveal(IEnumerable<Observation> observations, DateTimeOffset now)
    {
        if (!Active || suppressed) return false;
        var seen = observations.ToArray();
        if (Revealed)
        {
            var current = seen.FirstOrDefault(o => o.ObjectId == revealedObjectId && CofferKinds.IsPot(o.Kind) && o.Available && o.Targetable && Coordinates.IsFinite(o.Position));
            if (current is not null)
            {
                if (Target == current.Position) return false;
                Target = current.Position; Revision++; return true;
            }
            Target = null; Revealed = false;
            Detail = "寶箱已離開載入範圍或被取走；等待下一次提示，不會視為自動完成。";
            Revision++; return true;
        }
        if (revealAt is not { } at) return false;
        if (now - at > TimeSpan.FromSeconds(8))
        {
            revealAt = null; Detail = "尚未找到可確認的寶箱；再次使用聖靈藥後會自動辨識新提示。"; Revision++;
            return false;
        }
        var matches = seen.Where(o => CofferKinds.IsPot(o.Kind) && o.Available && o.Targetable && Coordinates.IsFinite(o.Position) &&
            !baselineObjects.Contains(o.ObjectId) && Vector3.DistanceSquared(revealOrigin, o.Position) <= 30 * 30).ToArray();
        if (matches.Length != 1) return false;
        Target = matches[0].Position; Revealed = true; revealedObjectId = matches[0].ObjectId; baselineObjects.Add(revealedObjectId); revealAt = null;
        Detail = "財寶提示後偵測到附近寶箱，已改用物件實際座標。"; Revision++; return true;
    }

    public void Restart(Vector3 position)
    {
        if (!Active) return;
        Candidates = all.DistinctBy(c => c.Position).ToArray(); revealAt = null; suppressed = false; SelectCandidate(position);
        Detail = "已重設全部候選點。請在目前位置重新使用聖靈藥。"; Revision++;
    }

    public static string DirectionName(int direction) => direction switch
    { 1 => "北", 2 => "東北", 3 => "東", 4 => "東南", 5 => "南", 6 => "西南", 7 => "西", 8 => "西北", _ => "未知" };

    public static bool MatchesDirection(Vector3 origin, Vector3 target, int direction)
    {
        if (direction is < 1 or > 8 || !Coordinates.IsFinite(origin) || !Coordinates.IsFinite(target)) return false;
        var delta = new Vector2(target.X - origin.X, target.Z - origin.Z);
        // A small tolerance avoids discarding a pad due to coordinate rounding / slight player movement.
        if (delta.Length() <= 3) return true;
        var bearing = (MathF.Atan2(delta.X, -delta.Y) * 180 / MathF.PI + 360) % 360;
        var angle = MathF.Abs(bearing - (direction - 1) * 45);
        angle = MathF.Min(angle, 360 - angle);
        return angle <= 22.5f + MathF.Asin(MathF.Min(1, 3 / delta.Length())) * 180 / MathF.PI;
    }
}
