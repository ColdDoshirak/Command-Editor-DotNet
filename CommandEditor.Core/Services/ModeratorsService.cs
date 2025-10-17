using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public class ModeratorsService
{
    private readonly string _moderatorsFilePath;
    private ModeratorsConfig _config;
    private HashSet<string> _apiModerators = new(StringComparer.OrdinalIgnoreCase);

    public ModeratorsService(string dataDirectory)
    {
        _moderatorsFilePath = Path.Combine(dataDirectory, "moderators.json");
        _config = LoadConfig();
    }

    private ModeratorsConfig LoadConfig()
    {
        if (File.Exists(_moderatorsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_moderatorsFilePath);
                return JsonSerializer.Deserialize<ModeratorsConfig>(json) ?? new ModeratorsConfig();
            }
            catch
            {
                return new ModeratorsConfig();
            }
        }
        return new ModeratorsConfig();
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_moderatorsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_config, options);
            await File.WriteAllTextAsync(_moderatorsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving moderators config: {ex.Message}");
        }
    }

    public void UpdateApiModerators(IEnumerable<string> moderators)
    {
        _apiModerators = new HashSet<string>(moderators.Select(m => m.ToLower()), StringComparer.OrdinalIgnoreCase);
    }

    public List<string> GetAllModerators()
    {
        var manual = _config.ManualModerators.Select(m => m.ToLower()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = _config.ExcludedModerators.Select(m => m.ToLower()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Combine API and manual, then remove excluded
        var combined = _apiModerators.Union(manual).Except(excluded);
        
        return combined.OrderBy(m => m).ToList();
    }

    public List<string> GetManualModerators()
    {
        return _config.ManualModerators.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> GetApiModerators()
    {
        return _apiModerators.OrderBy(m => m).ToList();
    }

    public List<string> GetExcludedModerators()
    {
        return _config.ExcludedModerators.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> AddManualModeratorAsync(string username)
    {
        username = username.ToLower().Trim();
        if (string.IsNullOrEmpty(username))
            return false;

        if (!_config.ManualModerators.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            _config.ManualModerators.Add(username);
            
            // Remove from excluded if present
            _config.ExcludedModerators.RemoveAll(m => m.Equals(username, StringComparison.OrdinalIgnoreCase));
            
            await SaveConfigAsync();
            return true;
        }
        return false;
    }

    public async Task<bool> RemoveManualModeratorAsync(string username)
    {
        username = username.ToLower().Trim();
        
        var removed = _config.ManualModerators.RemoveAll(m => m.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
        
        if (removed)
        {
            // Add to excluded list so API doesn't add them back
            if (!_config.ExcludedModerators.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                _config.ExcludedModerators.Add(username);
            }
            
            await SaveConfigAsync();
        }
        
        return true; // Always return true as the action completed
    }

    public async Task<bool> ExcludeModeratorAsync(string username)
    {
        username = username.ToLower().Trim();
        if (string.IsNullOrEmpty(username))
            return false;

        // Add to excluded list
        if (!_config.ExcludedModerators.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            _config.ExcludedModerators.Add(username);
            await SaveConfigAsync();
            return true;
        }
        
        return false;
    }

    public async Task<bool> RestoreModeratorAsync(string username)
    {
        username = username.ToLower().Trim();
        
        var removed = _config.ExcludedModerators.RemoveAll(m => m.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
        
        if (removed)
        {
            await SaveConfigAsync();
            return true;
        }
        
        return false;
    }

    public bool IsModerator(string username)
    {
        return GetAllModerators().Contains(username, StringComparer.OrdinalIgnoreCase);
    }

    public string GetModeratorSource(string username)
    {
        username = username.ToLower();
        
        var isInApi = _apiModerators.Contains(username);
        var isManual = _config.ManualModerators.Any(m => m.Equals(username, StringComparison.OrdinalIgnoreCase));
        var isExcluded = _config.ExcludedModerators.Any(m => m.Equals(username, StringComparison.OrdinalIgnoreCase));

        // If excluded, always show as excluded (regardless of API/Manual status)
        if (isExcluded)
        {
            return "Excluded";
        }
        
        if (isInApi && isManual) return "Both";
        if (isInApi) return "API";
        if (isManual) return "Manual";
        return "None";
    }

    public async Task ExportLegacyFormatAsync()
    {
        var legacyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy()
        };

        var legacyModerators = new
        {
            manual_moderators = _config.ManualModerators,
            excluded_moderators = _config.ExcludedModerators,
            notes = "Этот файл содержит список ручных модераторов и исключенных модераторов. Безопасен для показа на стриме"
        };

        var legacyPath = Path.Combine(Path.GetDirectoryName(_moderatorsFilePath)!, "moderators_legacy.json");
        await using var stream = File.Create(legacyPath);
        await JsonSerializer.SerializeAsync(stream, legacyModerators, legacyOptions);
    }

    private class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
                
            return string.Concat(name.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        }
    }
}
