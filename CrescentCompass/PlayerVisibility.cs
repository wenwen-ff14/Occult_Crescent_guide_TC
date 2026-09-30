using CrescentCompass.Core;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using VisibilityFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags;

namespace CrescentCompass;

internal sealed class PlayerVisibility
{
    private readonly PlayerVisibilityFilter filter = new((ulong)VisibilityFlags.Model);
    private bool faulted;
    internal int HiddenCount => filter.HiddenCount;
    internal bool Faulted => faulted;

    internal void Retry() => faulted = false;

    // All object reads and writes run on Framework.Update, including restoration on unload.
    internal void Update(bool enabled)
    {
        if (!enabled && HiddenCount == 0) return;
        try { Apply(enabled && !faulted); }
        catch (Exception error)
        {
            if (!faulted) Plugin.Log.Warning(error, "CrescentCompass player visibility failed; pausing and restoring on the next update");
            faulted = true;
        }
    }

    internal void Restore() => Apply(false);

    private unsafe void Apply(bool enabled)
    {
        var snapshot = new List<PlayerVisibilitySample>();
        var self = Plugin.Objects.LocalPlayer;
        var party = enabled ? Plugin.Party.Select(p => p.EntityId).ToHashSet() : [];
        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IPlayerCharacter player || player.Address == 0) continue;
            var native = (NativeGameObject*)player.Address;
            if (native->ObjectKind != NativeObjectKind.Pc || native->EntityId != player.EntityId) continue;
            var key = new PlayerVisibilityKey(player.GameObjectId, player.Address, player.EntityId, native->ObjectIndex);
            // Relationship/death reads are unnecessary for restoration and must never prevent turning the feature off.
            var flags = enabled ? player.StatusFlags : StatusFlags.None;
            snapshot.Add(new(key, (uint)native->RenderFlags, true,
                self is null || player.Address == self.Address || player.GameObjectId == self.GameObjectId,
                (flags & StatusFlags.PartyMember) != 0 || party.Contains(player.EntityId),
                (flags & StatusFlags.Friend) != 0,
                enabled && (player.IsDead || player.CurrentHp == 0)));
        }
        filter.Update(snapshot, enabled, WriteCurrent);
    }

    private static unsafe bool WriteCurrent(PlayerVisibilityChange change)
    {
        // Resolve the currently loaded object again, never write through a saved pointer.
        if (Plugin.Objects.SearchById(change.Key.ObjectId) is not IPlayerCharacter player || player.Address != change.Key.Address) return false;
        var native = (NativeGameObject*)player.Address;
        if (native->ObjectKind != NativeObjectKind.Pc || native->EntityId != change.Key.EntityId || native->ObjectIndex != change.Key.ObjectIndex) return false;
        // Only the model bit belongs to this feature. Preserve current unrelated rendering flags.
        const int mask = (int)VisibilityFlags.Model;
        native->RenderFlags = (native->RenderFlags & ~mask) | ((int)change.RenderFlags & mask);
        return true;
    }
}
