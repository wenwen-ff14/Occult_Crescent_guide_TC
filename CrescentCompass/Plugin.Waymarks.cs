using CrescentCompass.Core;
using CrescentCompass.Ui;
using Dalamud.Game.ClientState.Conditions;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private WaymarkLibrary waymarks = null!;
    private NativeWaymarks? nativeWaymarks;
    private readonly WaymarkPlacement waymarkPlacement = new();
    private string waymarkMessage = "儲存現場 A–D／1–4，或貼上 Waymark Preset JSON 匯入。";
    private bool waymarkFailed;
    private Guid waymarkSelectionRequest;
    private int waymarkSelectionRevision;
    private WaymarkContext WaymarkContext => new(Client.TerritoryType, Client.Instance, PlayerState.ContentId,
        !disposed && Client.IsLoggedIn && PlayerState.IsLoaded && PlayerState.ContentId != 0 && Objects.LocalPlayer is { IsDead: false } &&
        Client.TerritoryType == WaymarkPreset.SouthHorn && !IsLoading && !Client.IsGPosing &&
        !Conditions[ConditionFlag.WatchingCutscene] && !Conditions[ConditionFlag.WatchingCutscene78] && !Conditions[ConditionFlag.OccupiedInCutSceneEvent],
        Conditions[ConditionFlag.InCombat]);
    internal CompassWaymarkState WaymarkState()
    {
        var context = WaymarkContext;
        return new(waymarks.Presets, context.Ready && !waymarkPlacement.Active && waymarks.LoadError.Length == 0,
            context.Ready && !context.InCombat && !waymarkPlacement.Active, waymarkPlacement.Active,
            waymarkMessage, waymarks.LoadError, waymarkFailed,
            !context.Ready ? "請在新月島南部、角色可操作且未傳送時放置。" : context.InCombat ? "目前在戰鬥中，結束戰鬥後才能放置標點。" :
                waymarkPlacement.Active ? "正在放置標點，可按「取消放置」停止後續步驟。" : "", waymarkSelectionRequest, waymarkSelectionRevision,
            Config.IgnoreWaymarkDistance);
    }
    internal CompassWaymarkActions WaymarkActions => new(SaveWaymarks, ImportWaymarks, PlaceWaymarks,
        id => RunWaymarkAction(() => { waymarks.Delete(id); waymarkMessage = "已刪除所選預設；現場標點不受影響。"; }),
        (id, name) => RunWaymarkAction(() => { waymarks.Rename(id, name); waymarkMessage = "已更新預設名稱。"; }),
        ExportWaymarks, () => RunWaymarkAction(() => CancelWaymarks("已取消放置；已放置的標點保留。")), SetIgnoreWaymarkDistance);

    private void SetIgnoreWaymarkDistance(bool enabled) => RunWaymarkAction(() =>
    {
        if (waymarkPlacement.Active) throw new InvalidOperationException("請先完成或取消目前的放置，再切換距離設定。");
        Config.IgnoreWaymarkDistance = enabled;
        PluginInterface.SavePluginConfig(Config);
        waymarkMessage = enabled ? "已啟用忽略距離；依預設 XYZ 放置，不校正地面高度。" : "已恢復 200 公尺距離與地面檢查。";
    });

    private void RunWaymarkAction(Action action)
    {
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            try { waymarkFailed = false; action(); }
            catch (Exception ex) { waymarkFailed = true; waymarkMessage = ex.Message; Log.Warning(ex, "CrescentCompass waymark operation failed"); }
        });
    }
    private void SaveWaymarks(string name) => RunWaymarkAction(() =>
    {
        if (!WaymarkContext.Ready || waymarkPlacement.Active) throw new InvalidOperationException("請在新月島南部、角色可操作且未放置標點時儲存。");
        var preset = new WaymarkPreset(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? $"新月島 {DateTime.Now:MM-dd HH:mm:ss}" : name,
            WaymarkPreset.SouthHorn, DateTimeOffset.UtcNow, (nativeWaymarks ??= new()).Read());
        var id = waymarks.Add(preset);
        waymarkSelectionRequest = id; waymarkSelectionRevision++;
        waymarkMessage = $"已儲存「{waymarks.Get(id).Name}」 · {preset.Markers.Count(p => p.Active)} 個標點。";
    });
    private void ImportWaymarks(string json, bool assignUnknownMap) => RunWaymarkAction(() =>
    {
        var id = waymarks.Add(WaymarkPreset.Import(json, assignUnknownMap));
        waymarkSelectionRequest = id; waymarkSelectionRevision++;
        waymarkMessage = $"已匯入「{waymarks.Get(id).Name}」；選取預設後按「放置標點」。";
    });
    private string? ExportWaymarks(Guid id)
    {
        try { return waymarks.Get(id).Export(); }
        catch (Exception ex) { waymarkFailed = true; waymarkMessage = ex.Message; return null; }
    }
    private void PlaceWaymarks(Guid id) => RunWaymarkAction(() =>
    {
        var context = WaymarkContext;
        if (!context.Ready || context.InCombat || waymarkPlacement.Active) throw new InvalidOperationException("請在新月島南部、非戰鬥且角色可操作時放置標點。");
        var preset = waymarks.Get(id);
        // Snapshot the mode for the whole batch, including delayed operations.
        var useSavedCoordinates = Config.IgnoreWaymarkDistance;
        nativeWaymarks ??= new();
        waymarkMessage = useSavedCoordinates ? $"準備依「{preset.Name}」的儲存座標放置。" : $"正在檢查「{preset.Name}」的現場標點與地面。";
        var current = nativeWaymarks.Read();
        var prepared = WaymarkPlacement.Prepare(preset, current, (i, p) =>
        {
            try { return nativeWaymarks.GroundPoint(p, Position); }
            catch (Exception ex) { throw new InvalidOperationException($"標點 {WaymarkPreset.Label(i)}：{ex.Message}", ex); }
        }, useSavedCoordinates);
        waymarkPlacement.Start(prepared, current, context, Environment.TickCount64, useSavedCoordinates);
        waymarkMessage = waymarkPlacement.Detail;
    });
    private void UpdateWaymarks()
    {
        if (!waymarkPlacement.Active) return;
        try
        {
            var context = WaymarkContext;
            // Loading/logging out must cancel before dereferencing native state.
            var current = context.Ready && !context.InCombat ? nativeWaymarks!.Read() : [];
            waymarkPlacement.Tick(context, current, Environment.TickCount64, op => nativeWaymarks!.Apply(op, Position));
            waymarkMessage = waymarkPlacement.Detail;
            waymarkFailed = waymarkPlacement.Failed;
            if (waymarkFailed) Log.Warning("CrescentCompass waymark placement: {Detail}", waymarkMessage);
        }
        catch (Exception ex)
        { CancelWaymarks("標點放置已停止：" + ex.Message, true); Log.Warning(ex, "CrescentCompass waymark placement stopped"); }
    }
    private void CancelWaymarks(string message, bool failed = false)
    {
        if (!waymarkPlacement.Active) return;
        waymarkPlacement.Cancel(message, failed); waymarkMessage = message; waymarkFailed = failed;
    }
}
