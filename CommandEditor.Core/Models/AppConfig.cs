using System.Collections.Generic;

namespace CommandEditor.Core.Models;

public class AppConfig
{
    public string? CurrentFile { get; set; }
    public double Volume { get; set; } = 0.5;
    public AutoSaveConfig AutoSave { get; set; } = new();
    public SoundConfig Sound { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
    public TwitchChannelConfig Twitch { get; set; } = new();
    public Dictionary<string, object> CommandsMetadata { get; set; } = new();
    public bool EnableDebugLogging { get; set; } = false;
    public int MaxBackups { get; set; } = 100;
    public string? LastDismissedVersion { get; set; }
}
