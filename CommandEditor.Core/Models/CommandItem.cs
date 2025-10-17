using System.Text.Json.Serialization;
using CommandEditor.Core.Serialization;

namespace CommandEditor.Core.Models;

public class CommandItem
{
    public string Command { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CommandPermission Permission { get; set; } = CommandPermission.Everyone;

    public string Info { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;

    public int Cooldown { get; set; }

    public int UserCooldown { get; set; }

    public decimal Cost { get; set; }

    public int Count { get; set; }

    [JsonConverter(typeof(CommandUsageJsonConverter))]
    public CommandUsage Usage { get; set; } = CommandUsage.StreamlabsChatbot;

    public bool Enabled { get; set; } = true;

    public string SoundFile { get; set; } = string.Empty;

    public string? FkSoundFile { get; set; }

    [JsonConverter(typeof(CommandVolumeJsonConverter))]
    public int Volume { get; set; } = 100;

    public string NormalizedKey => Command.TrimStart('!').Trim().ToLowerInvariant();
}
