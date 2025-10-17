using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommandEditor.Core.Models;
using CommandEditor.Core.Serialization;

namespace CommandEditor.Core.Services;

public class CommandStorageService
{
    // Custom snake case naming policy for legacy export
    private class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
                
            return string.Concat(name.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        }
    }

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _options;

    public string DefaultCommandsPath => _paths.CommandsFile;
    public string LastLoadedPath { get; private set; } = string.Empty;

    public CommandStorageService(AppPaths paths)
    {
        _paths = paths;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _options.Converters.Add(new CommandUsageJsonConverter());
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<IList<CommandItem>> LoadAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && string.Equals(Path.GetExtension(filePath), ".abcomg", StringComparison.OrdinalIgnoreCase))
        {
            var legacyPath = Path.GetFullPath(filePath);
            var legacyCommands = await LoadLegacyAsync(legacyPath, cancellationToken);
            LastLoadedPath = _paths.CommandsFile;
            await SaveAsync(legacyCommands, _paths.CommandsFile, cancellationToken);
            return legacyCommands;
        }

        var path = ResolvePath(filePath);
        if (!File.Exists(path))
        {
            LastLoadedPath = path;
            return new List<CommandItem>();
        }

        await using var stream = File.OpenRead(path);
        var commands = await JsonSerializer.DeserializeAsync<IList<CommandItem>>(stream, _options, cancellationToken);
        LastLoadedPath = path;
        return commands ?? new List<CommandItem>();
    }

    public async Task SaveAsync(IEnumerable<CommandItem> commands, string? filePath = null, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(filePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, commands, _options, cancellationToken);
    }

    public async Task SaveLegacyAsync(IEnumerable<CommandItem> commands, string filePath, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Convert modern commands to legacy format (with 0-1 volume range)
        var legacyCommands = new List<LegacyCommand>();
        foreach (var cmd in commands)
        {
            legacyCommands.Add(new LegacyCommand
            {
                Command = cmd.Command,
                SoundFile = cmd.SoundFile,
                Enabled = cmd.Enabled,
                Permission = cmd.Permission,
                Response = cmd.Response,
                Info = cmd.Info,
                Group = cmd.Group,
                Cooldown = cmd.Cooldown,
                UserCooldown = cmd.UserCooldown,
                Cost = cmd.Cost,
                Count = cmd.Count,
                Usage = cmd.Usage.ToString(),
                FkSoundFile = cmd.FkSoundFile,
                Volume = cmd.Volume // Will be saved as 0-100 in legacy format
            });
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, legacyCommands, _options, cancellationToken);
    }

    public async Task ExportLegacyFormatAsync(IEnumerable<CommandItem> commands, CancellationToken cancellationToken = default)
    {
        var legacyOptions = new JsonSerializerOptions
        {
            WriteIndented = true
            // Remove SnakeCaseNamingPolicy - use default PascalCase for legacy format
        };
        
        var legacyCommands = new List<LegacyExportCommand>();
        foreach (var cmd in commands)
        {
            legacyCommands.Add(new LegacyExportCommand
            {
                Command = cmd.Command,
                Permission = cmd.Permission.ToString(),
                Response = cmd.Response ?? "",
                SoundFile = cmd.SoundFile ?? "",
                Enabled = cmd.Enabled,
                Info = cmd.Info ?? "",
                Group = cmd.Group ?? "",
                Cooldown = cmd.Cooldown,
                UserCooldown = cmd.UserCooldown,
                Cost = cmd.Cost,
                Count = cmd.Count,
                Usage = cmd.Usage.ToString(),
                FKSoundFile = cmd.FkSoundFile ?? "",
                Volume = cmd.Volume
            });
        }

        var legacyPath = Path.Combine(_paths.DataDirectory, "commands_legacy.json");
        await using var stream = File.Create(legacyPath);
        await JsonSerializer.SerializeAsync(stream, legacyCommands, legacyOptions, cancellationToken);
    }

    public string ResolvePath(string? filePath) => string.IsNullOrWhiteSpace(filePath)
        ? _paths.CommandsFile
        : Path.GetFullPath(filePath);

    private async Task<IList<CommandItem>> LoadLegacyAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var legacyCommands = await JsonSerializer.DeserializeAsync<IList<LegacyCommand>>(stream, _options, cancellationToken) ?? new List<LegacyCommand>();
        var result = new List<CommandItem>(legacyCommands.Count);

        foreach (var legacy in legacyCommands)
        {
            var usage = CommandUsage.StreamlabsChatbot;
            if (!string.IsNullOrWhiteSpace(legacy.Usage) && CommandUsageJsonConverter.TryParse(legacy.Usage, out var parsedUsage))
            {
                usage = parsedUsage;
            }

            var volume = Math.Clamp(legacy.Volume, 0, 100);

            result.Add(new CommandItem
            {
                Command = legacy.Command ?? string.Empty,
                SoundFile = legacy.SoundFile ?? string.Empty,
                Enabled = legacy.Enabled ?? true,
                Permission = legacy.Permission ?? CommandPermission.Everyone,
                Response = legacy.Response ?? string.Empty,
                Info = legacy.Info ?? string.Empty,
                Group = legacy.Group ?? string.Empty,
                Cooldown = legacy.Cooldown ?? 0,
                UserCooldown = legacy.UserCooldown ?? 0,
                Cost = legacy.Cost ?? 0,
                Count = legacy.Count ?? 0,
                Usage = usage,
                FkSoundFile = legacy.FkSoundFile,
                Volume = volume
            });
        }

        return result;
    }

    private sealed class LegacyCommand
    {
        public string? Command { get; set; }
        public string? SoundFile { get; set; }
        public bool? Enabled { get; set; }
        public CommandPermission? Permission { get; set; }
        public string? Response { get; set; }
        public string? Info { get; set; }
        public string? Group { get; set; }
        public int? Cooldown { get; set; }
        public int? UserCooldown { get; set; }
        public decimal? Cost { get; set; }
        public int? Count { get; set; }
        public string? Usage { get; set; }
        public string? FkSoundFile { get; set; }
        [JsonConverter(typeof(CommandVolumeJsonConverter))]
        public int Volume { get; set; } = 100;
    }

    private sealed class LegacyExportCommand
    {
        public string? Command { get; set; }
        public string? Permission { get; set; }
        public string? Response { get; set; }
        public string? SoundFile { get; set; }
        public bool Enabled { get; set; }
        public string? Info { get; set; }
        public string? Group { get; set; }
        public int? Cooldown { get; set; }
        public int? UserCooldown { get; set; }
        public decimal? Cost { get; set; }
        public int? Count { get; set; }
        public string? Usage { get; set; }
        [JsonPropertyName("FKSoundFile")]
        public string? FKSoundFile { get; set; }
        public int Volume { get; set; }
    }
}
