using CrescentCompass.Core;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using VisibilityFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags;

namespace CrescentCompass;

internal sealed class PlayerVisibility
{
    private readonly PlayerVisibilityFilter filter = new((ulong)VisibilityFlags.Model);
    private readonly PlayerFriendRoster friends = new();
    private ulong friendOwner;
    private long nextFriendScan;
    private bool friendReadFailed;
    private bool faulted;
    internal int HiddenCount => filter.HiddenCount;
    internal bool Faulted => faulted;
    internal bool HasFriendRoster => friends.HasSnapshot;
    internal int FriendCount => friends.Count;
    internal string FriendDetail => HasFriendRoster ? $"好友快取 {FriendCount} 人。" : "尚無好友快取；請在島外開啟好友名單約 2 秒。";

    internal void Retry() => faulted = false;

    // All object reads and writes run on Framework.Update, including restoration on unload.
    internal void Update(bool enabled, Configuration config)
    {
        // Capture outside the island too, including when hiding is disabled. The friend UI
        // cannot be opened on the island, so keep a per-character snapshot in plugin config.
        RefreshFriends(config);
        if (!enabled && HiddenCount == 0) return;
        try { Apply(enabled && !faulted && friends.HasSnapshot); }
        catch (Exception error)
        {
            if (!faulted) Plugin.Log.Warning(error, "CrescentCompass player visibility failed; pausing and restoring on the next update");
            faulted = true;
        }
    }

    internal void Restore() => Apply(false);

    private unsafe void RefreshFriends(Configuration config)
    {
        var owner = Plugin.Client.IsLoggedIn && Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ContentId : 0;
        if (owner != friendOwner)
        {
            friendOwner = owner; nextFriendScan = 0; friends.Clear();
            config.PlayerVisibilityFriends ??= [];
            if (owner != 0 && config.PlayerVisibilityFriends.TryGetValue($"{owner:X16}", out var saved) && saved is not null)
                friends.Replace(owner, saved);
        }
        if (owner == 0) return;
        var now = Environment.TickCount64;
        if (now < nextFriendScan) return;
        nextFriendScan = now + 1000;
        var inIsland = SpotCatalog.IsSupported(Plugin.Client.TerritoryType);
        if (inIsland && friends.HasSnapshot) return;
        try
        {
            var info = InfoModule.Instance();
            var list = info == null || info->GetLocalContentId() != owner ? null : info->GetInfoProxyFriendList();
            FriendIdentity[]? entries = null;
            if (list != null && list->EntryCount <= 200 &&
                (list->EntryCount == 0 || list->CharData != null))
            {
                var loaded = new List<FriendIdentity>();
                foreach (ref readonly var entry in list->CharDataSpan)
                {
                    if ((entry.State & InfoProxyCommonList.CharacterData.OnlineStatus.WaitingForFriendListApproval) != 0) continue;
                    loaded.Add(new(entry.ContentId, entry.NameString, entry.HomeWorld));
                }
                entries = loaded.ToArray();
            }
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("FriendList").Address;
            // Only an open, ready outside-island list can replace the cache with an empty list.
            var allowEmpty = !inIsland && addon != null && addon->IsVisible && addon->IsReady;
            if (friends.Observe(owner, entries, allowEmpty, now))
            {
                config.PlayerVisibilityFriends[$"{owner:X16}"] = friends.Snapshot;
                Plugin.PluginInterface.SavePluginConfig(config);
            }
            friendReadFailed = false;
        }
        catch (Exception error)
        {
            // Failure to refresh must not prevent restoring models or discard an existing cache.
            if (!friendReadFailed) Plugin.Log.Warning(error, "Could not refresh player visibility friend roster; keeping last character snapshot");
            friendReadFailed = true;
        }
    }

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
                (flags & StatusFlags.Friend) != 0 || (enabled && friends.Contains(friendOwner,
                    new(((NativeCharacter*)player.Address)->ContentId, player.Name.TextValue, (ushort)player.HomeWorld.RowId))),
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
