using Dalamud.Configuration;
using CrescentCompass.Core;

namespace CrescentCompass;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public HashSet<uint> GarbageItemIds { get; set; } = [];
    public LootCleanupMode LootMode { get; set; } = LootCleanupMode.Discard;
    public bool IncludeCarrots { get; set; } = true;
    public bool IncludeSilver { get; set; } = true;
    public bool IncludeBronze { get; set; } = false;
    // Fixed patrol policy. Getter-only properties also ignore obsolete values in saved configurations.
    public PointDisplayMode DisplayMode => PointDisplayMode.Observed;
    public bool IncludeExploration { get; set; } = false;
    public bool OnlyUnexplored { get; set; } = true;
    public bool ShowWorldHints { get; set; } = true;
    public bool HideOtherPlayers { get; set; } = false;
    public Dictionary<string, FriendIdentity[]> PlayerVisibilityFriends { get; set; } = [];
    public bool IncludeSpecial { get; set; } = true;
    public bool IncludeTower { get; set; } = false;
    public bool AutoFlagPot { get; set; } = true;
    public bool NotifyPotFateSpawn { get; set; } = true;
    public bool NotifyPotFateSoon { get; set; } = true;
    public bool ShowPotCountdownOverlay { get; set; } = false;
    public bool LockPotCountdownOverlay { get; set; } = true;
    public float PotCountdownOverlayX { get; set; } = 24;
    public float PotCountdownOverlayY { get; set; } = 160;
    public bool ShowPatrolOverlay { get; set; } = false;
    public bool LockPatrolOverlay { get; set; } = true;
    public float PatrolOverlayX { get; set; } = 24;
    public float PatrolOverlayY { get; set; } = 320;
    public bool TrackCeCooldowns { get; set; } = true;
    public bool AutoFlagFates { get; set; } = true;
    public bool IgnoreWaymarkDistance { get; set; } = false;
    public bool AutoAdvanceChests => true;
    public bool AutoOpenNearbyChests { get; set; } = false;
    public float EmptyCheckRadius => RouteAutomation.CheckRadius;
    public bool UseChartRoute => true;
    public int ChartStartNumber { get; set; } = 1;
    public Dictionary<string, ChestOpenProgress> ChestProgress { get; set; } = [];
}
