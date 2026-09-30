using System.Numerics;
using CrescentCompass.Core;

namespace CrescentCompass;

/// <summary>Read-only vnavmesh IPC. All IPC invocations enter through the framework thread.</summary>
internal sealed class GroundNavigation
{
    internal static string Status()
    {
        try
        {
            var ready = Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
            if (!ready.HasFunction) return "需要啟用 vnavmesh，才能計算地形步行路線。";
            return ready.InvokeFunc() ? "地形導航已就緒" : "vnavmesh 正在準備地圖，請就緒後再規劃。";
        }
        catch { return "vnavmesh 暫時無法連線，請確認插件已載入。"; }
    }

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
                return await task.WaitAsync(token).ConfigureAwait(false);
            }, token).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("地形尋路逾時或導航已重建，請稍後重新規劃。"); }
    }
}
