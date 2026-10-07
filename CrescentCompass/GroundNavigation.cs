using System.Numerics;
using CrescentCompass.Core;

namespace CrescentCompass;

/// <summary>Read-only vnavmesh IPC. All IPC invocations enter through the framework thread.</summary>
internal sealed class GroundNavigation
{
    internal static readonly PatrolPathCache PatrolPaths = new();
    internal static string Status()
    {
        try
        {
            var ready = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
            if (!ready.HasFunction) { PatrolPaths.Clear(); return "需要啟用 vnavmesh，才能計算地形步行路線。"; }
            if (ready.InvokeFunc()) return "地形導航已就緒";
            PatrolPaths.Clear(); return "vnavmesh 正在準備地圖，請就緒後再規劃。";
        }
        catch { PatrolPaths.Clear(); return "vnavmesh 暫時無法連線，請確認插件已載入。"; }
    }

    internal static Task<IReadOnlyList<Vector3>> FindPatrolPath(Vector3 from, Vector3 to, CancellationToken cancellation) =>
        PatrolPaths.FindPath(from, to, FindPath, cancellation);

    internal static async Task<Vector3?> SnapRecoveryAnchor(Vector3 anchor, CancellationToken cancellation) =>
        await Plugin.Framework.RunOnFrameworkThread<Vector3?>(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            var nearest = Plugin.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
            var floor = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
            // Older providers still validate raw anchors through Pathfind. Never move straight to a guessed point.
            if (!nearest.HasFunction && !floor.HasFunction) return anchor;
            var point = nearest.HasFunction ? nearest.InvokeFunc(anchor, 3, 3) : null;
            if (point is { } p && PatrolRecoveryPlanner.ValidAnchor(anchor, p)) return p;
            point = floor.HasFunction ? floor.InvokeFunc(anchor, false, 3) : null;
            return point is { } q && PatrolRecoveryPlanner.ValidAnchor(anchor, q) ? q : null;
        }).WaitAsync(cancellation).ConfigureAwait(false);

    internal static async Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        try
        {
            return await Plugin.Framework.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (Status() != "地形導航已就緒") throw new InvalidOperationException(Status());
                var cancelable = Plugin.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
                var task = cancelable.HasFunction ? cancelable.InvokeFunc(from, to, false, token)
                    : Plugin.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind").InvokeFunc(from, to, false);
                // Legacy providers may finish after cancellation. Observe their errors without cancelling other plugins' queries.
                _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return (IReadOnlyList<Vector3>)await task.WaitAsync(token).ConfigureAwait(false);
            }, token).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("地形尋路逾時或導航已重建，請稍後重新規劃。"); }
    }
}
