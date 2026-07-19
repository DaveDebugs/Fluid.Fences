using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopFences
{
    public class GlobalConfig
    {
        [JsonPropertyName("FirstRunComplete")] public bool FirstRunComplete { get; set; } = true;
        [JsonPropertyName("ShowTaskbarIcon")] public bool ShowTaskbarIcon { get; set; } = true;
        [JsonPropertyName("RestoreFilesOnDelete")] public bool RestoreFilesOnDelete { get; set; } = true;
        [JsonPropertyName("EnableGhostMode")] public bool EnableGhostMode { get; set; } = false;
        
        [JsonPropertyName("Theme")] public Core.ThemeSettings Theme { get; set; } = new Core.ThemeSettings();
    }

    public class SnapshotData
    {
        [JsonPropertyName("FenceId")] public string FenceId { get; set; } = "";
        [JsonPropertyName("Left")] public double Left { get; set; }
        [JsonPropertyName("Top")] public double Top { get; set; }
        [JsonPropertyName("Width")] public double Width { get; set; }
        [JsonPropertyName("Height")] public double Height { get; set; }
        [JsonPropertyName("IsRolledUp")] public bool IsRolledUp { get; set; }
        [JsonPropertyName("ExpandedHeight")] public double ExpandedHeight { get; set; }
        [JsonPropertyName("ExpandedWidth")] public double ExpandedWidth { get; set; }
        [JsonPropertyName("ExpandedLeft")] public double ExpandedLeft { get; set; }
        [JsonPropertyName("ExpandedTop")] public double ExpandedTop { get; set; }
    }

    public class FenceTab
    {
        [JsonPropertyName("TabId")] public string TabId { get; set; } = System.Guid.NewGuid().ToString();
        [JsonPropertyName("Title")] public string Title { get; set; } = "New Tab";
        [JsonPropertyName("SortMethod")] public string SortMethod { get; set; } = "None";
        [JsonPropertyName("AutoSortExtensions")] public string AutoSortExtensions { get; set; } = "";
        [JsonPropertyName("IsPortal")] public bool IsPortal { get; set; } = false;
        [JsonPropertyName("PortalPath")] public string PortalPath { get; set; } = "";
        [JsonPropertyName("Files")] public List<string> Files { get; set; } = [];
    }

    public class FenceData
    {
        [JsonPropertyName("Left")] public double Left { get; set; }
        [JsonPropertyName("Top")] public double Top { get; set; }
        [JsonPropertyName("Width")] public double Width { get; set; }
        [JsonPropertyName("Height")] public double Height { get; set; }
        [JsonPropertyName("IsRolledUp")] public bool IsRolledUp { get; set; }
        [JsonPropertyName("ExpandedHeight")] public double ExpandedHeight { get; set; }
        [JsonPropertyName("ExpandedWidth")] public double ExpandedWidth { get; set; }
        [JsonPropertyName("ExpandedLeft")] public double ExpandedLeft { get; set; }
        [JsonPropertyName("ExpandedTop")] public double ExpandedTop { get; set; }

        [JsonPropertyName("Tabs")] public List<FenceTab> Tabs { get; set; } = [];
        [JsonPropertyName("ActiveTabIndex")] public int ActiveTabIndex { get; set; } = 0;

        [JsonPropertyName("HexColor")] public string HexColor { get; set; } = "#000000";
        [JsonPropertyName("Opacity")] public double Opacity { get; set; } = 0.7;
        [JsonPropertyName("IconSize")] public double IconSize { get; set; } = 48;
        [JsonPropertyName("ShowSearch")] public bool ShowSearch { get; set; } = true;
        [JsonPropertyName("AutoMatchColor")] public bool AutoMatchColor { get; set; } = false;
        [JsonPropertyName("GhostModeOverride")] public int GhostModeOverride { get; set; } = 0;

        // Legacy properties for migration
        [JsonPropertyName("Title")] public string Title { get; set; } = "Fluid Fence";
        [JsonPropertyName("SortMethod")] public string SortMethod { get; set; } = "None";
        [JsonPropertyName("AutoSortExtensions")] public string AutoSortExtensions { get; set; } = "";
        [JsonPropertyName("IsPortal")] public bool IsPortal { get; set; } = false;
        [JsonPropertyName("PortalPath")] public string PortalPath { get; set; } = "";
        [JsonPropertyName("Files")] public List<string> Files { get; set; } = [];
    }
}