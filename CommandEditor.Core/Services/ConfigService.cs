using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public class ConfigService
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

    public ConfigService(AppPaths paths)
    {
        _paths = paths;
        _paths.EnsureDirectories();
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _options.Converters.Add(new JsonStringEnumConverter());
        _options.Converters.Add(new AppConfigConverter());
        _options.Converters.Add(new TwitchSecretsConverter());
    }

    public AppConfig Load()
    {
        if (!File.Exists(_paths.ConfigFile))
        {
            var defaultConfig = CreateDefaultConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        // Check if file is empty
        var fileInfo = new FileInfo(_paths.ConfigFile);
        if (fileInfo.Length == 0)
        {
            var defaultConfig = CreateDefaultConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            using var stream = File.OpenRead(_paths.ConfigFile);
            var config = JsonSerializer.Deserialize<AppConfig>(stream, _options) ?? CreateDefaultConfig();
            return config;
        }
        catch (JsonException)
        {
            // If JSON is corrupted or empty, create default
            var defaultConfig = CreateDefaultConfig();
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    public void Save(AppConfig config)
    {
        CreateBackup(_paths.ConfigFile, _paths.BackupDirectory, "config");
        using var stream = File.Create(_paths.ConfigFile);
        JsonSerializer.Serialize(stream, config, _options);
    }

    public TwitchSecrets LoadTwitchSecrets()
    {
        if (!File.Exists(_paths.TwitchConfigFile))
        {
            var secrets = new TwitchSecrets();
            SaveTwitchSecrets(secrets);
            return secrets;
        }

        using var stream = File.OpenRead(_paths.TwitchConfigFile);
        return JsonSerializer.Deserialize<TwitchSecrets>(stream, _options) ?? new TwitchSecrets();
    }

    public void SaveTwitchSecrets(TwitchSecrets secrets)
    {
        using var stream = File.Create(_paths.TwitchConfigFile);
        JsonSerializer.Serialize(stream, secrets, _options);
    }

    public void SaveTwitchSecretsLegacy(TwitchSecrets secrets)
    {
        var legacyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy()
        };
        
        using var stream = File.Create(Path.Combine(_paths.DataDirectory, "twitch_config_legacy.json"));
        JsonSerializer.Serialize(stream, secrets, legacyOptions);
    }

    public void SaveConfigLegacy(AppConfig config)
    {
        var legacyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        
        using var stream = File.Create(Path.Combine(_paths.DataDirectory, "config_legacy.json"));
        
        // Create legacy format object
        var legacyConfig = new
        {
            format_version = "2.0",
            current_file = config.CurrentFile,
            volume = config.Volume,
            twitch = new
            {
                channel = config.Twitch?.Channel ?? ""
            },
            auto_save = new
            {
                enabled = config.AutoSave?.Enabled ?? false,
                interval = config.AutoSave?.IntervalSeconds ?? 300
            },
            recent_files = config.RecentFiles ?? new List<string>(),
            sound = new
            {
                volume = config.Sound?.Volume ?? 1.0,
                sound_dir = config.Sound?.SoundDirectory ?? "",
                allow_interruption = config.Sound?.AllowInterruption ?? false,
                show_interruption_message = config.Sound?.ShowInterruptionMessage ?? false
            },
            commands = new { }, // Empty for now
            backup = new
            {
                max_backups = config.MaxBackups
            }
        };
        
        JsonSerializer.Serialize(stream, legacyConfig, legacyOptions);
    }

    public ModeratorsConfig LoadModerators()
    {
        if (!File.Exists(_paths.ModeratorsFile))
        {
            var defaultModerators = new ModeratorsConfig();
            SaveModerators(defaultModerators);
            return defaultModerators;
        }

        using var stream = File.OpenRead(_paths.ModeratorsFile);
        return JsonSerializer.Deserialize<ModeratorsConfig>(stream, _options) ?? new ModeratorsConfig();
    }

    public void SaveModerators(ModeratorsConfig config)
    {
        using var stream = File.Create(_paths.ModeratorsFile);
        JsonSerializer.Serialize(stream, config, _options);
    }

    public AutoSaveConfig GetAutoSave(AppConfig config) => config.AutoSave ?? new AutoSaveConfig();

    public void UpdateAutoSave(AppConfig config, bool enabled, int intervalSeconds)
    {
        config.AutoSave.Enabled = enabled;
        config.AutoSave.IntervalSeconds = intervalSeconds;
        Save(config);
    }

    private static AppConfig CreateDefaultConfig() => new();

    private static void CreateBackup(string sourceFile, string backupDirectory, string prefix)
    {
        if (!File.Exists(sourceFile))
        {
            return;
        }

        Directory.CreateDirectory(backupDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFile = Path.Combine(backupDirectory, $"{prefix}_{timestamp}.json");
        
        // Handle case where file with same timestamp already exists
        var counter = 1;
        var originalBackupFile = backupFile;
        while (File.Exists(backupFile))
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalBackupFile);
            var ext = Path.GetExtension(originalBackupFile);
            var dir = Path.GetDirectoryName(originalBackupFile);
            backupFile = Path.Combine(dir!, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }
        
        File.Copy(sourceFile, backupFile, overwrite: false);
        var backups = Directory.GetFiles(backupDirectory, $"{prefix}_*.json")
            .OrderByDescending(x => x)
            .Skip(25)
            .ToList();
        foreach (var file in backups)
        {
            File.Delete(file);
        }
    }
}

// Config converter for backwards compatibility
internal class AppConfigConverter : JsonConverter<AppConfig>
{
    public override AppConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;
        
        var config = new AppConfig();
        
        // Handle snake_case to PascalCase mapping
        if (root.TryGetProperty("current_file", out var currentFile))
            config.CurrentFile = currentFile.GetString();
        else if (root.TryGetProperty("CurrentFile", out var currentFilePascal))
            config.CurrentFile = currentFilePascal.GetString();
            
        if (root.TryGetProperty("volume", out var volume))
            config.Volume = volume.GetDouble();
        else if (root.TryGetProperty("Volume", out var volumePascal))
            config.Volume = volumePascal.GetDouble();
            
        if (root.TryGetProperty("auto_save", out var autoSave))
            config.AutoSave = JsonSerializer.Deserialize<AutoSaveConfig>(autoSave.GetRawText(), options) ?? new();
        else if (root.TryGetProperty("AutoSave", out var autoSavePascal))
            config.AutoSave = JsonSerializer.Deserialize<AutoSaveConfig>(autoSavePascal.GetRawText(), options) ?? new();
            
        if (root.TryGetProperty("sound", out var sound))
            config.Sound = JsonSerializer.Deserialize<SoundConfig>(sound.GetRawText(), options) ?? new();
        else if (root.TryGetProperty("Sound", out var soundPascal))
            config.Sound = JsonSerializer.Deserialize<SoundConfig>(soundPascal.GetRawText(), options) ?? new();
            
        if (root.TryGetProperty("recent_files", out var recentFiles))
            config.RecentFiles = JsonSerializer.Deserialize<List<string>>(recentFiles.GetRawText(), options) ?? new();
        else if (root.TryGetProperty("RecentFiles", out var recentFilesPascal))
            config.RecentFiles = JsonSerializer.Deserialize<List<string>>(recentFilesPascal.GetRawText(), options) ?? new();
            
        if (root.TryGetProperty("twitch", out var twitch))
            config.Twitch = JsonSerializer.Deserialize<TwitchChannelConfig>(twitch.GetRawText(), options) ?? new();
        else if (root.TryGetProperty("Twitch", out var twitchPascal))
            config.Twitch = JsonSerializer.Deserialize<TwitchChannelConfig>(twitchPascal.GetRawText(), options) ?? new();
            
        if (root.TryGetProperty("backup", out var backup))
        {
            var backupConfig = JsonSerializer.Deserialize<Dictionary<string, int>>(backup.GetRawText(), options);
            if (backupConfig?.TryGetValue("max_backups", out var maxBackups) == true)
                config.MaxBackups = maxBackups;
        }
        else if (root.TryGetProperty("MaxBackups", out var maxBackupsPascal))
            config.MaxBackups = maxBackupsPascal.GetInt32();
            
        return config;
    }

    public override void Write(Utf8JsonWriter writer, AppConfig value, JsonSerializerOptions options)
    {
        // Use default serialization for writing - don't use our custom converter again
        var defaultOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        defaultOptions.Converters.Add(new JsonStringEnumConverter());
        
        JsonSerializer.Serialize(writer, value, defaultOptions);
    }
}

// Twitch secrets converter for backwards compatibility
internal class TwitchSecretsConverter : JsonConverter<TwitchSecrets>
{
    public override TwitchSecrets Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;
        
        var secrets = new TwitchSecrets();
        
        if (root.TryGetProperty("access_token", out var accessToken))
            secrets.AccessToken = accessToken.GetString() ?? string.Empty;
        else if (root.TryGetProperty("AccessToken", out var accessTokenPascal))
            secrets.AccessToken = accessTokenPascal.GetString() ?? string.Empty;
            
        if (root.TryGetProperty("client_id", out var clientId))
            secrets.ClientId = clientId.GetString() ?? string.Empty;
        else if (root.TryGetProperty("ClientId", out var clientIdPascal))
            secrets.ClientId = clientIdPascal.GetString() ?? string.Empty;
            
        if (root.TryGetProperty("refresh_token", out var refreshToken))
            secrets.RefreshToken = refreshToken.GetString() ?? string.Empty;
        else if (root.TryGetProperty("RefreshToken", out var refreshTokenPascal))
            secrets.RefreshToken = refreshTokenPascal.GetString() ?? string.Empty;
            
        return secrets;
    }

    public override void Write(Utf8JsonWriter writer, TwitchSecrets value, JsonSerializerOptions options)
    {
        // Use default serialization for writing - don't use our custom converter again
        var defaultOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        
        JsonSerializer.Serialize(writer, value, defaultOptions);
    }
}
