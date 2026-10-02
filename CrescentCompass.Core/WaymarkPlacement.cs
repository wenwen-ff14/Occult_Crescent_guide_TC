using System.Numerics;

namespace CrescentCompass.Core;

public readonly record struct WaymarkContext(ushort Territory, uint Instance, ulong Character, bool Ready, bool InCombat);
public sealed record WaymarkOperation(int Index, SavedWaymark Marker, bool UseSavedCoordinates = false)
{
    public SavedWaymark ResolvePoint(Func<SavedWaymark, SavedWaymark> ground)
    {
        if (UseSavedCoordinates || !Marker.Active) return Marker;
        var checkedPoint = ground(Marker);
        if (Vector3.Distance(checkedPoint.Position, Marker.Position) > .05f)
            throw new InvalidOperationException("標點地形已變更，請重新套用預設。");
        return checkedPoint;
    }
}

// Framework-driven: one operation at a time, followed by observation of the actual
// marker state. A successful request alone is not reported as a completed placement.
public sealed class WaymarkPlacement
{
    private readonly Queue<WaymarkOperation> pending = new();
    private WaymarkContext context;
    private WaymarkOperation? waiting;
    private long next, deadline;
    private int total, complete;
    public bool Active => waiting is not null || pending.Count > 0;
    public bool Failed { get; private set; }
    public string Detail { get; private set; } = "尚未套用標點。";

    public static WaymarkPreset Prepare(WaymarkPreset preset, SavedWaymark[] current, Func<int, SavedWaymark, SavedWaymark> ground,
        bool useSavedCoordinates = false)
    {
        preset = WaymarkPreset.Validate(preset);
        if (current.Length != 8) throw new InvalidDataException("無法取得目前八個標點。");
        // Explicit distant-placement mode preserves saved/imported XYZ, including
        // height. Distant collision data need not be streamed into this client.
        if (useSavedCoordinates) return preset;
        // Matching slots do not need a placement or terrain query. Preflight every
        // changed active slot before Start can schedule any native mutation.
        var prepared = preset.Markers.Select((marker, i) => marker.Active && !Matches(current[i], marker) ? ground(i, marker) : marker).ToArray();
        return WaymarkPreset.Validate(preset with { Markers = prepared });
    }
    public void Start(WaymarkPreset preset, SavedWaymark[] current, WaymarkContext state, long now, bool useSavedCoordinates = false)
    {
        if (Active) throw new InvalidOperationException("請先取消目前的標點放置。");
        WaymarkPreset.Validate(preset);
        if (!state.Ready || state.InCombat || state.Character == 0 || state.Territory != preset.Territory)
            throw new InvalidOperationException("請在新月島南部、非戰鬥且角色可操作時放置標點。");
        if (current.Length != 8) throw new InvalidDataException("無法取得目前八個標點。");
        context = state; next = now; deadline = 0; complete = 0; Failed = false;
        // Only clear the slots that are inactive in the chosen preset; never clear-all.
        for (int i = 0; i < 8; i++) if (!Matches(current[i], preset.Markers[i])) pending.Enqueue(new(i, preset.Markers[i], useSavedCoordinates));
        total = pending.Count;
        Detail = total == 0 ? "標點已在儲存位置，無須重複放置。可先移動或清除現場標點，再按「放置標點」測試。" : $"準備放置／清除 {total} 個標點；原有同名標點會被取代。";
    }
    public void Tick(WaymarkContext state, SavedWaymark[] current, long now, Func<WaymarkOperation, byte> apply)
    {
        if (!Active) return;
        if (!state.Ready || state.InCombat || state.Territory != context.Territory || state.Instance != context.Instance || state.Character != context.Character)
        { Cancel("狀態變更，已停止放置；已放置的標點保留。"); return; }
        if (current.Length != 8) { Cancel("無法讀取標點，已停止放置。", true); return; }
        if (waiting is { } operation)
        {
            if (Matches(current[operation.Index], operation.Marker))
            {
                waiting = null; complete++; next = now + 600; deadline = 0;
                Detail = $"已更新 {complete}／{total}" + (pending.Count == 0 ? " 個標點；本機狀態已符合預設，請確認現場顯示。" : "，等待下一個。");
            }
            else if (now >= deadline) Cancel($"標點 {WaymarkPreset.Label(operation.Index)} 未確認成功，已停止；可檢查現場後重試。", true);
            return;
        }
        if (now < next || pending.Count == 0) return;
        var item = pending.Peek();
        byte result = apply(item);
        if (result is 2 or 3)
        {
            if (deadline == 0) deadline = now + 5000;
            if (now >= deadline) Cancel("遊戲標點操作持續忙碌，已停止放置。", true);
            else { next = now + 600; Detail = "等待遊戲標點操作鎖定解除。"; }
            return;
        }
        if (result != 0) { Cancel($"遊戲拒絕標點 {WaymarkPreset.Label(item.Index)}（代碼 {result}），已停止放置。", true); return; }
        pending.Dequeue(); waiting = item; deadline = now + 5000;
        Detail = $"正在確認標點 {WaymarkPreset.Label(item.Index)} · {complete}／{total}";
    }
    public void Cancel(string message = "已取消放置；已放置的標點保留。", bool failed = false)
    { pending.Clear(); waiting = null; deadline = 0; Failed = failed; Detail = message; }
    public static bool Matches(SavedWaymark a, SavedWaymark b) => a.Active == b.Active && (!a.Active || Vector3.Distance(a.Position, b.Position) <= .02f);
    public static bool WithinRange(SavedWaymark point, Vector3 player) => !point.Active ||
        Coordinates.IsFinite(player) && Vector3.Distance(point.Position, player) <= 200;
}
