using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public class CurrencyService : IAsyncDisposable
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
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, CurrencyUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RankDefinition> _ranks = new();
    private Timer? _payoutTimer;
    private ITwitchService? _twitchService;

    public CurrencyService(AppPaths paths)
    {
        _paths = paths;
        _options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        _options.Converters.Add(new CurrencyUserConverter());
    }

    public void SetTwitchService(ITwitchService twitchService)
    {
        _twitchService = twitchService;
    }

    public CurrencySettings Settings { get; private set; } = new();

    public IEnumerable<CurrencyUser> Users => _users.Values;

    public IReadOnlyList<RankDefinition> Ranks => _ranks;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        await LoadSettingsAsync();
        await LoadUsersAsync();
        await LoadRanksAsync();
        
        // Start the payout system if accumulation is enabled
        if (Settings.AccumulationEnabled)
        {
            await StartPayoutSystemAsync();
        }
    }

    public double AddPoints(string username, double amount)
    {
        var user = GetOrCreateUser(username);
        user.Points = Math.Max(0, user.Points + amount);
        user.LastSeenUtc = DateTime.UtcNow;
        return user.Points;
    }

    public double RemovePoints(string username, double amount)
    {
        var user = GetOrCreateUser(username);
        user.Points = Math.Max(0, user.Points - amount);
        user.LastSeenUtc = DateTime.UtcNow;
        return user.Points;
    }

    public double GetPoints(string username)
    {
        var key = Normalize(username);
        return _users.TryGetValue(key, out var user) ? user.Points : 0;
    }

    public CurrencyUser? GetUser(string username)
    {
        var key = Normalize(username);
        return _users.TryGetValue(key, out var user) ? user : null;
    }

    public async Task StartPayoutSystemAsync()
    {
        // Stop any existing timer first
        await StopPayoutSystemAsync();

        // Start a new timer that ticks every minute to award points
        _payoutTimer = new Timer(async _ => await ProcessPayoutsAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    public async Task StopPayoutSystemAsync()
    {
        if (_payoutTimer != null)
        {
            await _payoutTimer.DisposeAsync();
            _payoutTimer = null;
        }
    }

    private async Task ProcessPayoutsAsync()
    {
        try
        {
            // Don't process if accumulation is disabled
            if (!Settings.AccumulationEnabled)
            {
                return;
            }

            // Determine if stream is live to decide which payout to use
            bool isLive = _twitchService?.IsLive ?? false;
            double payoutAmount = isLive ? Settings.LivePayout : Settings.OfflinePayout;
            int intervalMinutes = isLive ? Settings.OnlineIntervalMinutes : Settings.OfflineIntervalMinutes;

            // Don't process if payouts are disabled
            if (payoutAmount <= 0 && Settings.RegularBonus <= 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            int usersUpdated = 0;

            foreach (var user in _users.Values.ToList())
            {
                // Calculate time since last payout was processed for this user
                var timeSinceLastPayout = now - user.LastPayoutUtc;
                
                // Calculate how many full intervals have passed since last payout
                int intervalsPassed = (int)(timeSinceLastPayout.TotalMinutes / intervalMinutes);
                
                // Only process if at least one full interval has passed
                if (intervalsPassed > 0)
                {
                    double totalPayout = 0.0;
                    
                    // Calculate base payout
                    if (payoutAmount > 0)
                    {
                        totalPayout += payoutAmount * intervalsPassed;
                    }
                    
                    // Add regular bonus
                    if (Settings.RegularBonus > 0)
                    {
                        totalPayout += Settings.RegularBonus * intervalsPassed;
                    }
                    
                    if (totalPayout > 0)
                    {
                        user.Points += totalPayout;
                        
                        // Update hours based on intervals passed (full intervals only)
                        // Only add hours if stream is live OR if we're tracking offline hours
                        if (isLive || Settings.TrackOfflineHours)
                        {
                            user.Hours += (intervalMinutes * intervalsPassed) / 60.0;
                        }
                        
                        // Update the last payout time to account for the intervals we just processed
                        // This ensures we don't double-payout for the same time period
                        user.LastPayoutUtc = user.LastPayoutUtc.AddMinutes(intervalMinutes * intervalsPassed);
                        
                        // Also update LastSeenUtc if needed, but separately from payout tracking
                        user.LastSeenUtc = now;
                        
                        usersUpdated++;
                    }
                }
            }

            if (usersUpdated > 0)
            {
                // Save users after processing payouts
                await SaveUsersAsync();
                
                if (Settings.ShowServiceMessages && usersUpdated > 0)
                {
                    Console.WriteLine($"[Currency] Processed payouts for {usersUpdated} users. Live: {isLive}, Payout: {payoutAmount}/interval, Interval: {intervalMinutes}min");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in payout processing: {ex.Message}");
        }
    }

    public bool DeductPoints(string username, double amount)
    {
        var user = GetUser(username);
        if (user == null || user.Points < amount)
        {
            return false;
        }

        user.Points -= amount;
        user.LastSeenUtc = DateTime.UtcNow;
        return true;
    }

    public CurrencyUser GetOrCreateUser(string username)
    {
        var key = Normalize(username);
        return _users.GetOrAdd(key, _ => new CurrencyUser
        {
            Username = key,
            Points = 0,
            Hours = 0,
            LastSeenUtc = DateTime.UtcNow,
            LastPayoutUtc = DateTime.UtcNow  // Initialize payout tracking when user is first created
        });
    }

    public async Task SaveUsersAsync()
    {
        var users = _users.Values
            .OrderBy(u => u.Username)
            .ToDictionary(u => u.Username, u => u, StringComparer.OrdinalIgnoreCase);
        await using var stream = File.Create(_paths.CurrencyUsersFile);
        await JsonSerializer.SerializeAsync(stream, users, _options);
    }

    public async Task SaveSettingsAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.CurrencySettingsFile)!);
        await using var stream = File.Create(_paths.CurrencySettingsFile);
        await JsonSerializer.SerializeAsync(stream, Settings, _options);
    }

    public async Task SaveRanksAsync()
    {
        await using var stream = File.Create(_paths.RanksFile);
        await JsonSerializer.SerializeAsync(stream, _ranks, _options);
    }

    public void ReplaceUsers(IEnumerable<CurrencyUser> users)
    {
        _users.Clear();
        foreach (var user in users)
        {
            _users[Normalize(user.Username)] = user;
        }
    }

    public void ReplaceRanks(IEnumerable<RankDefinition> ranks)
    {
        _ranks.Clear();
        _ranks.AddRange(ranks.OrderBy(r => r.Requirement));
    }

    private async Task LoadUsersAsync()
    {
        if (!File.Exists(_paths.CurrencyUsersFile))
        {
            return;
        }

        await using var stream = File.OpenRead(_paths.CurrencyUsersFile);
        var users = await JsonSerializer.DeserializeAsync<Dictionary<string, CurrencyUser>>(stream, _options);
        if (users is null)
        {
            return;
        }

        foreach (var pair in users)
        {
            _users[Normalize(pair.Key)] = pair.Value;
        }
    }

    private async Task LoadSettingsAsync()
    {
        if (!File.Exists(_paths.CurrencySettingsFile))
        {
            Settings = new CurrencySettings();
            await SaveSettingsAsync();
            return;
        }

        await using var stream = File.OpenRead(_paths.CurrencySettingsFile);
        Settings = await JsonSerializer.DeserializeAsync<CurrencySettings>(stream, _options) ?? new CurrencySettings();
    }

    private async Task LoadRanksAsync()
    {
        if (!File.Exists(_paths.RanksFile))
        {
            return;
        }

        await using var stream = File.OpenRead(_paths.RanksFile);
        var ranks = await JsonSerializer.DeserializeAsync<List<RankDefinition>>(stream, _options);
        if (ranks is null)
        {
            return;
        }

        _ranks.Clear();
        _ranks.AddRange(ranks.OrderBy(r => r.Requirement));
    }

    private static string Normalize(string username) => username.Trim().TrimStart('@').ToLowerInvariant();

    public async Task ExportLegacyFormatAsync(IEnumerable<CurrencyUser> users)
    {
        var legacyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = new SnakeCaseNamingPolicy()
        };

        var legacyUsers = new Dictionary<string, object>();
        foreach (var user in users)
        {
            legacyUsers[user.Username] = new
            {
                points = user.Points,
                hours = user.Hours,
                last_seen = ((DateTimeOffset)user.LastSeenUtc).ToUnixTimeSeconds(),
                is_regular = false, // Default value, could be calculated if needed
                is_mod = false // Default value, could be calculated if needed
            };
        }

        var legacyPath = Path.Combine(_paths.DataDirectory, "users_currency_legacy.json");
        await using var stream = File.Create(legacyPath);
        await JsonSerializer.SerializeAsync(stream, legacyUsers, legacyOptions);
    }

    public async ValueTask DisposeAsync()
    {
        await StopPayoutSystemAsync();
    }
}

// Currency user converter for backwards compatibility
internal class CurrencyUserConverter : JsonConverter<Dictionary<string, CurrencyUser>>
{
    public override Dictionary<string, CurrencyUser> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;
        
        var users = new Dictionary<string, CurrencyUser>();
        
        foreach (var property in root.EnumerateObject())
        {
            var username = property.Name;
            var userElement = property.Value;
            
            var user = new CurrencyUser { Username = username };
            
            // Handle snake_case to PascalCase mapping and timestamp conversion
            if (userElement.TryGetProperty("points", out var points))
                user.Points = points.GetDouble();
            else if (userElement.TryGetProperty("Points", out var pointsPascal))
                user.Points = pointsPascal.GetDouble();
                
            if (userElement.TryGetProperty("hours", out var hours))
                user.Hours = hours.GetDouble();
            else if (userElement.TryGetProperty("Hours", out var hoursPascal))
                user.Hours = hoursPascal.GetDouble();
                
            // Handle timestamp conversion (Unix timestamp to DateTime)
            if (userElement.TryGetProperty("last_seen", out var lastSeen))
            {
                if (lastSeen.ValueKind == JsonValueKind.Number)
                {
                    var timestamp = lastSeen.GetDouble();
                    user.LastSeenUtc = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).UtcDateTime;
                }
                else
                {
                    user.LastSeenUtc = DateTime.Parse(lastSeen.GetString() ?? DateTime.UtcNow.ToString());
                }
            }
            else if (userElement.TryGetProperty("LastSeenUtc", out var lastSeenPascal))
            {
                user.LastSeenUtc = DateTime.Parse(lastSeenPascal.GetString() ?? DateTime.UtcNow.ToString());
            }
            else
            {
                user.LastSeenUtc = DateTime.UtcNow;
            }
            
            // Handle new field LastPayoutUtc
            if (!userElement.TryGetProperty("LastPayoutUtc", out var lastPayout))
            {
                user.LastPayoutUtc = user.LastSeenUtc;
            }
            else
            {
                user.LastPayoutUtc = DateTime.Parse(lastPayout.GetString() ?? user.LastSeenUtc.ToString());
            }
            
            // Handle rank type
            if (userElement.TryGetProperty("RankType", out var rankType))
            {
                var rankTypeString = rankType.GetString();
                if (Enum.TryParse<UserRankType>(rankTypeString, out var rankTypeEnum))
                    user.RankType = rankTypeEnum;
                else
                    user.RankType = UserRankType.Points;
            }
            else
            {
                user.RankType = UserRankType.Points;
            }
                
            users[username] = user;
        }
        
        return users;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, CurrencyUser> value, JsonSerializerOptions options)
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
