using Dalamud.Configuration;
using CrescentCompass.Core;

namespace CrescentCompass;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool IncludeCarrots { get; set; } = true;
    public bool IncludeSilver { get; set; } = true;
    public bool IncludeBronze { get; set; } = false;
    public PointDisplayMode DisplayMode { get; set; } = PointDisplayMode.Observed;
    public bool IncludeExploration { get; set; } = false;
    public bool OnlyUnexplored { get; set; } = true;
    public bool ShowWorldHints { get; set; } = true;
    public bool HideOtherPlayers { get; set; } = false;
    public bool IncludeSpecial { get; set; } = true;
    public bool IncludeTower { get; set; } = false;
    public bool AutoFlagPot { get; set; } = true;
    public bool NotifyPotFateSpawn { get; set; } = true;
    public bool TrackCeCooldowns { get; set; } = true;
    public bool AutoAdvanceChests { get; set; } = true;
    public float EmptyCheckRadius { get; set; } = RouteAutomation.CheckRadius;
    public bool UseChartRoute { get; set; } = true;
    public int ChartStartNumber { get; set; } = 1;
    public Dictionary<string, ChestOpenProgress> ChestProgress { get; set; } = [];
}
