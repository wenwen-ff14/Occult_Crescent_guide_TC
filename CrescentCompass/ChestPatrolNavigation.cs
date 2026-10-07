using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CrescentCompass;

/// <summary>Framework-thread movement IPC. Only stops a remaining suffix of the path we submitted.</summary>
internal sealed class ChestPatrolNavigation : IChestPatrolNavigation
{
    private IReadOnlyList<Vector3>? submitted;
    public bool Ready => GroundNavigation.Status() == "地形導航已就緒" &&
        Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").HasAction &&
        Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").HasAction &&
        Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints").HasFunction &&
        Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").HasFunction &&
        Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed").HasFunction &&
        Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed").InvokeFunc();

    public PatrolMovement Movement
    {
        get
        {
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc()) return PatrolMovement.External;
            var path = Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints").InvokeFunc();
            return path.Count == 0 ? PatrolMovement.Idle : AutoChestPatrol.OwnsPath(submitted, path) ? PatrolMovement.Owned : PatrolMovement.External;
        }
    }

    public IReadOnlyList<Vector3> RemainingPath => Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints").InvokeFunc();
    public Task<IReadOnlyList<Vector3>> FindPath(Vector3 from, Vector3 to, CancellationToken cancellation) => GroundNavigation.FindPatrolPath(from, to, cancellation);
    public Task<IReadOnlyList<Vector3>> RecoverPath(Vector3 from, Vector3 to, IReadOnlyList<Vector3> failedPath,
        int attempt, CancellationToken cancellation)
    {
        GroundNavigation.PatrolPaths.Forget(to);
        return PatrolRecoveryPlanner.FindPath(from, to, failedPath, attempt, GroundNavigation.FindPath, cancellation,
            GroundNavigation.SnapRecoveryAnchor);
    }

    public unsafe bool TryJump()
    {
        if (!Ready || Movement != PatrolMovement.Owned || Plugin.Client.TerritoryType != ChestChart.Territory ||
            Plugin.Client.IsGPosing || Plugin.Objects.LocalPlayer is not { IsDead: false, CurrentHp: > 0, IsCasting: false } ||
            Plugin.Conditions[ConditionFlag.Jumping] || Plugin.Conditions[ConditionFlag.InFlight] ||
            Plugin.Conditions[ConditionFlag.Diving] || Plugin.Conditions[ConditionFlag.RidingPillion]) return false;
        var actions = ActionManager.Instance();
        return actions != null && actions->UseAction(ActionType.GeneralAction, 2);
    }

    public void Move(IReadOnlyList<Vector3> path)
    {
        if (!Ready || Movement != PatrolMovement.Idle) throw new InvalidOperationException("導航已被其他工作使用或尚未就緒。");
        submitted = path.ToArray();
        Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(path.ToList(), false);
    }

    public void Stop()
    {
        if (submitted is not null)
        {
            var list = Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
            var stop = Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
            if (list.HasFunction && AutoChestPatrol.OwnsPath(submitted, list.InvokeFunc()))
            {
                if (!stop.HasAction) throw new InvalidOperationException("導航停止介面不可用，等待停止。");
                stop.InvokeAction();
            }
            submitted = null;
        }
    }
}
