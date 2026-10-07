using System.Numerics;
using CrescentCompass.Core;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeTreasure = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace CrescentCompass;

public sealed partial class Plugin
{
    private readonly AutoChestOpener autoChestOpener = new();
    private bool autoChestFaulted;
    private ChestInteractionContext AutoChestContext => new(Client.TerritoryType, Client.Instance, PlayerState.ContentId,
        Active && PlayerState.IsLoaded && Objects.LocalPlayer?.EntityId == PlayerState.EntityId && !Client.IsGPosing,
        Conditions[ConditionFlag.InCombat], IsOccupied || Conditions[ConditionFlag.Occupied] ||
        Conditions[ConditionFlag.Occupied30] || Conditions[ConditionFlag.Occupied33] || Conditions[ConditionFlag.Occupied38] ||
        Conditions[ConditionFlag.Occupied39] || Conditions[ConditionFlag.OccupiedInQuestEvent] || Conditions[ConditionFlag.TradeOpen] ||
        Conditions[ConditionFlag.OccupiedSummoningBell] || Conditions[ConditionFlag.Casting] || Conditions[ConditionFlag.Casting87] ||
        Conditions[ConditionFlag.CarryingObject] || Conditions[ConditionFlag.Mounting] || Conditions[ConditionFlag.Mounting71],
        Conditions[ConditionFlag.Mounted],
        Objects.LocalPlayer is not { IsDead: false, CurrentHp: > 0 }, IsPaused,
        Conditions[ConditionFlag.InFlight], Conditions[ConditionFlag.RidingPillion], Conditions[ConditionFlag.Jumping]);

    internal string AutoChestDetail => !AutoChestEnabled ? "關閉；勾選後自動開啟 2 公尺內的寶箱。" :
        autoChestFaulted ? "互動介面發生錯誤，已暫停；請查看 Dalamud 記錄，關閉再開啟可重試。" :
        AutoChestContext.GetBlockReason(allowCombat: autoPatrol.Enabled) is { Length: > 0 } reason ? reason : autoChestOpener.Detail;

    internal void SetAutoOpenNearbyChests(bool enabled)
    {
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            Config.AutoOpenNearbyChests = enabled;
            autoChestOpener.Reset(); autoChestFaulted = false;
            PluginInterface.SavePluginConfig(Config);
        });
    }

    private void UpdateAutoChests(IReadOnlyList<Observation> observations, long now)
    {
        if (autoChestFaulted) return;
        try
        {
            var targets = !autoPatrol.Enabled && Config.AutoOpenNearbyChests ? observations : PatrolChest(observations) is { } chest ? new[] { chest } : [];
            autoChestOpener.Update(AutoChestEnabled, AutoChestContext, targets, Position, now, InteractWithChest,
                autoPatrol.Enabled ? AutoChestOpener.PatrolAttemptIntervalMs : AutoChestOpener.AttemptIntervalMs, allowCombat: autoPatrol.Enabled);
        }
        catch (Exception error)
        {
            autoChestFaulted = true;
            if (autoPatrol.Enabled) { autoPatrol.Fail("自動開箱發生錯誤，已停止自動巡查。"); PauseRoute(); }
            Log.Error(error, "CrescentCompass auto chest interaction failed; paused until explicitly re-enabled");
        }
    }

    private unsafe bool InteractWithChest(Observation observation)
    {
        // Re-resolve the loaded object on the framework thread. Never hold native pointers between updates.
        if (!AutoChestEnabled || AutoChestContext.GetBlockReason(allowCombat: autoPatrol.Enabled).Length != 0) return false;
        var obj = Objects.SearchById(observation.ObjectId);
        if (obj is null || !obj.IsValid() || obj.Address == 0 || obj.IsDead || !obj.IsTargetable || obj.BaseId != observation.DataId ||
            Vector3.DistanceSquared(obj.Position, observation.Position) > 1 ||
            !AutoChestOpener.Eligible(observation with { Position = obj.Position }, Position)) return false;
        if (obj.ObjectKind == ObjectKind.EventObj)
        {
            if (CofferKinds.EventObject(obj.BaseId) is not { } kind || !CofferKinds.IsCoffer(kind)) return false;
        }
        else if (obj.ObjectKind == ObjectKind.Treasure)
        {
            var treasure = (NativeTreasure*)obj.Address;
            if (treasure->State != NativeTreasure.TreasureState.Unopened ||
                (treasure->Flags & (NativeTreasure.TreasureFlags.Opened | NativeTreasure.TreasureFlags.FadedOut)) != 0) return false;
            var loot = Loot.Instance();
            if (loot == null) return false;
            foreach (var item in loot->Items)
                if (item.ItemId != 0 && item.ChestObjectId == obj.EntityId) return false;
        }
        else return false;
        var currentLoot = Loot.Instance();
        if (currentLoot != null)
            foreach (var item in currentLoot->Items)
                if (item.ItemId != 0 && item.ChestObjectId == obj.GameObjectId) return false;
        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null) throw new InvalidOperationException("TargetSystem 尚未就緒。");
        // Keep the game's line-of-sight and interaction checks. Its return value is not collection evidence.
        targetSystem->InteractWithObject((NativeGameObject*)obj.Address, true);
        if (CarrotMode) carrotGathering.RecordBunnyInteraction(observation.ObjectId);
        return true;
    }
}
