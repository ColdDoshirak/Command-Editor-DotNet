using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public sealed class CommandExecutionService : IAsyncDisposable
{
    private readonly ITwitchService _twitchService;
    private readonly CurrencyService _currencyService;
    private readonly SoundPlaybackService _soundPlaybackService;
    private readonly Dictionary<string, DateTime> _commandCooldowns = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _userCooldowns = new();
    private readonly object _lock = new();

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;
    
    // Callback to determine user permission
    public Func<string, UserPermission>? GetUserPermission { get; set; }

    public CommandExecutionService(ITwitchService twitchService, CurrencyService currencyService, SoundPlaybackService soundPlaybackService)
    {
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _soundPlaybackService = soundPlaybackService ?? throw new ArgumentNullException(nameof(soundPlaybackService));

        _twitchService.MessageReceived += OnMessageReceived;
        _twitchService.ViewersUpdated += OnViewersUpdated;
    }

    private void OnMessageReceived(object? sender, string message)
    {
        try
        {
            // Parse message format: "[Username]: message"
            var match = Regex.Match(message, @"^\[(.+?)\]:\s*(.+)$");
            if (!match.Success)
            {
                return; // Not a chat message (probably system message)
            }

            var username = match.Groups[1].Value;
            var messageText = match.Groups[2].Value.Trim();

            // Update user's last seen time regardless of whether it's a command
            // This ensures all active chatters are eligible for payouts
            var user = _currencyService.GetOrCreateUser(username);
            user.LastSeenUtc = DateTime.UtcNow;

            // Check if message starts with command prefix
            if (!messageText.StartsWith("!"))
            {
                return; // Not a command, just an active chatter
            }

            // Parse command and arguments
            var parts = messageText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var commandName = parts[0]; // includes the '!'
            var arguments = parts.Length > 1 ? parts[1] : string.Empty;

            ProcessCommandAsync(commandName, username, arguments);
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            Console.WriteLine($"Error processing command: {ex.Message}");
        }
    }

    private void OnViewersUpdated(object? sender, IReadOnlyCollection<string> viewers)
    {
        try
        {
            // Add all viewers to the currency system to ensure they're eligible for payouts
            // This is especially important in offline mode when there might be no chat activity
            foreach (var viewer in viewers)
            {
                if (!string.IsNullOrWhiteSpace(viewer))
                {
                    var user = _currencyService.GetOrCreateUser(viewer);
                    // Update last seen time to ensure they're considered active
                    user.LastSeenUtc = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            Console.WriteLine($"Error processing viewers update: {ex.Message}");
        }
    }

    private async void ProcessCommandAsync(string commandName, string username, string arguments)
    {
        // First check if it's a system command
        var normalizedCommand = commandName.TrimStart('!').ToLowerInvariant();
        
        if (normalizedCommand == "points")
        {
            await HandlePointsCommandAsync(username);
            return;
        }
        else if (normalizedCommand == "points_add")
        {
            await HandlePointsAddCommandAsync(username, arguments);
            return;
        }
        else if (normalizedCommand == "points_remove")
        {
            await HandlePointsRemoveCommandAsync(username, arguments);
            return;
        }

        // If not a system command, fire event for custom command handling
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs
        {
            CommandName = commandName,
            Username = username,
            Arguments = arguments
        });
    }

    private async Task HandlePointsCommandAsync(string username)
    {
        try
        {
            var user = _currencyService.GetOrCreateUser(username);
            var currencyName = _currencyService.Settings.Name ?? "Points";
            var points = user.Points;
            var hours = user.Hours;
            
            // Format hours as "Xh Ym"
            var formattedHours = FormatHours(hours);
            
            // Format points with 2 decimal places
            var formattedPoints = points.ToString("F2");
            
            // Get rank (for now just "None" - will be implemented later)
            var rank = "None";
            var rankText = string.IsNullOrEmpty(rank) || rank == "None" ? "" : $" [{rank}]";
            
            var response = $"{username}{rankText} - Hours: {formattedHours} - {currencyName}: {formattedPoints}";
            await _twitchService.SendMessageAsync(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in !points command: {ex.Message}");
        }
    }

    private async Task HandlePointsAddCommandAsync(string username, string arguments)
    {
        try
        {
            // Check if user is moderator or admin
            var userPermission = GetUserPermission?.Invoke(username) ?? UserPermission.Everyone;
            
            if (userPermission != UserPermission.Moderator && userPermission != UserPermission.Admin)
            {
                await _twitchService.SendMessageAsync($"@{username}: You don't have permission to use this command");
                return;
            }

            // Parse arguments: <target> <amount>
            var parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _twitchService.SendMessageAsync("Usage: !points_add <username> <amount>");
                return;
            }

            var target = parts[0].TrimStart('@').ToLowerInvariant();
            if (!double.TryParse(parts[1], out var amount))
            {
                await _twitchService.SendMessageAsync("Amount must be a number");
                return;
            }

            _currencyService.AddPoints(target, amount);
            await _currencyService.SaveUsersAsync();
            
            var currencyName = _currencyService.Settings.Name ?? "Points";
            await _twitchService.SendMessageAsync($"Successfully given {target} {amount:F2} {currencyName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in !points_add command: {ex.Message}");
        }
    }

    private async Task HandlePointsRemoveCommandAsync(string username, string arguments)
    {
        try
        {
            // Check if user is moderator or admin
            var userPermission = GetUserPermission?.Invoke(username) ?? UserPermission.Everyone;
            
            if (userPermission != UserPermission.Moderator && userPermission != UserPermission.Admin)
            {
                await _twitchService.SendMessageAsync($"@{username}: You don't have permission to use this command");
                return;
            }

            // Parse arguments: <target> <amount>
            var parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _twitchService.SendMessageAsync("Usage: !points_remove <username> <amount>");
                return;
            }

            var target = parts[0].TrimStart('@').ToLowerInvariant();
            if (!double.TryParse(parts[1], out var amount))
            {
                await _twitchService.SendMessageAsync("Amount must be a number");
                return;
            }

            _currencyService.RemovePoints(target, amount);
            await _currencyService.SaveUsersAsync();
            
            var currencyName = _currencyService.Settings.Name ?? "Points";
            await _twitchService.SendMessageAsync($"Successfully removed {amount:F2} {currencyName} from {target}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in !points_remove command: {ex.Message}");
        }
    }

    private string FormatHours(double hours)
    {
        var totalMinutes = (int)(hours * 60);
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        return $"{h}h {m}m";
    }

    public async Task<CommandExecutionResult> ExecuteCommandAsync(
        CommandItem command,
        string username,
        string arguments,
        UserPermission userPermission)
    {
        // 1. Check if command is enabled
        if (!command.Enabled)
        {
            return new CommandExecutionResult
            {
                Success = false,
                Reason = "Command is disabled"
            };
        }

        // 2. Check permission
        if (!HasPermission(userPermission, command.Permission))
        {
            return new CommandExecutionResult
            {
                Success = false,
                Reason = "Insufficient permissions"
            };
        }

        // 3. Check global cooldown
        int? globalCooldownRemaining = null;
        lock (_lock)
        {
            if (_commandCooldowns.TryGetValue(command.Command, out var lastExecution))
            {
                var elapsed = DateTime.UtcNow - lastExecution;
                if (elapsed.TotalSeconds < command.Cooldown)
                {
                    globalCooldownRemaining = command.Cooldown - (int)elapsed.TotalSeconds;
                }
            }
        }

        if (globalCooldownRemaining.HasValue)
        {
            await _twitchService.SendMessageAsync($"@{username}: command is on cooldown. Try in {globalCooldownRemaining.Value} sec.");
            return new CommandExecutionResult
            {
                Success = false,
                Reason = $"Command on cooldown ({globalCooldownRemaining.Value}s remaining)"
            };
        }

        // 4. Check user cooldown
        int? userCooldownRemaining = null;
        lock (_lock)
        {
            if (!_userCooldowns.ContainsKey(command.Command))
            {
                _userCooldowns[command.Command] = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }

            if (_userCooldowns[command.Command].TryGetValue(username, out var lastUserExecution))
            {
                var elapsed = DateTime.UtcNow - lastUserExecution;
                if (elapsed.TotalSeconds < command.UserCooldown)
                {
                    userCooldownRemaining = command.UserCooldown - (int)elapsed.TotalSeconds;
                }
            }
        }

        if (userCooldownRemaining.HasValue)
        {
            await _twitchService.SendMessageAsync($"@{username}: you can use this command in {userCooldownRemaining.Value} sec.");
            return new CommandExecutionResult
            {
                Success = false,
                Reason = $"User cooldown active ({userCooldownRemaining.Value}s remaining)"
            };
        }

        // 5. Check currency cost
        if (command.Cost > 0)
        {
            var user = _currencyService.GetUser(username);
            if (user == null || user.Points < (double)command.Cost)
            {
                return new CommandExecutionResult
                {
                    Success = false,
                    Reason = $"Insufficient points (need {command.Cost})"
                };
            }

            // Deduct cost
            _currencyService.DeductPoints(username, (double)command.Cost);
        }

        // 6. Send response (before sound to match Python behavior)
        if (!string.IsNullOrWhiteSpace(command.Response))
        {
            var response = ReplaceVariables(command.Response, username, command);
            await _twitchService.SendMessageAsync(response);
        }

        // 8. Trigger sound playback with volume
        bool hasSound = !string.IsNullOrWhiteSpace(command.SoundFile);
        SoundPlaybackResult? soundResult = null;
        
        if (hasSound)
        {
            soundResult = _soundPlaybackService.RequestPlayback(command.SoundFile, command.Volume);
            
            if (!soundResult.Success)
            {
                // Sound playback failed - need to handle refund
                if (soundResult.ShouldRefund)
                {
                    string blockMessage;
                    
                    if (command.Cost > 0)
                    {
                        // Refund the points
                        _currencyService.AddPoints(username, (double)command.Cost);
                        await _currencyService.SaveUsersAsync();
                        
                        var currencyName = _currencyService.Settings.Name ?? "Points";
                        blockMessage = $"@{username}: {soundResult.Reason}. {command.Cost} {currencyName} refunded.";
                    }
                    else
                    {
                        // No cost - just show block message
                        blockMessage = $"@{username}: {soundResult.Reason}.";
                    }
                    
                    // Only show message if interruption messages are enabled
                    if (soundResult.ShowMessage)
                    {
                        await _twitchService.SendMessageAsync(blockMessage);
                    }
                }
                
                // Command failed - don't increment count or set cooldowns
                return new CommandExecutionResult
                {
                    Success = false,
                    Reason = soundResult.Reason
                };
            }
        }

        // 9. Only increment count and set cooldowns if command succeeded
        command.Count++;
        
        // Set cooldowns
        lock (_lock)
        {
            _commandCooldowns[command.Command] = DateTime.UtcNow;
            _userCooldowns[command.Command][username] = DateTime.UtcNow;
        }

        return new CommandExecutionResult
        {
            Success = true,
            Reason = "Command executed successfully"
        };
    }

    private bool HasPermission(UserPermission userPermission, CommandPermission requiredPermission)
    {
        // Permission hierarchy (higher includes lower)
        var permissionLevels = new Dictionary<CommandPermission, int>
        {
            { CommandPermission.Everyone, 0 },
            { CommandPermission.Moderator, 1 },
            { CommandPermission.Admin, 2 }
        };

        var userLevels = new Dictionary<UserPermission, int>
        {
            { UserPermission.Everyone, 0 },
            { UserPermission.Moderator, 1 },
            { UserPermission.Admin, 2 }
        };

        var requiredLevel = permissionLevels.GetValueOrDefault(requiredPermission, 0);
        var userLevel = userLevels.GetValueOrDefault(userPermission, 0);

        return userLevel >= requiredLevel;
    }

    private string ReplaceVariables(string template, string username, CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var result = template;
        var user = _currencyService.GetUser(username);

        // Replace variables
        result = result.Replace("$user", username, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$username", username, StringComparison.OrdinalIgnoreCase);
        
        if (user != null)
        {
            result = result.Replace("$points", user.Points.ToString(), StringComparison.OrdinalIgnoreCase);
            result = result.Replace("$currencyname", _currencyService.Settings.Name ?? "Points", StringComparison.OrdinalIgnoreCase);
            result = result.Replace("$hours", user.Hours.ToString("F2"), StringComparison.OrdinalIgnoreCase);
            result = result.Replace("$rank", "None", StringComparison.OrdinalIgnoreCase);
        }

        result = result.Replace("$count", command.Count.ToString(), StringComparison.OrdinalIgnoreCase);

        return result;
    }

    public ValueTask DisposeAsync()
    {
        _twitchService.MessageReceived -= OnMessageReceived;
        _twitchService.ViewersUpdated -= OnViewersUpdated;
        return ValueTask.CompletedTask;
    }
}

public class CommandExecutedEventArgs : EventArgs
{
    public string CommandName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
}

public class CommandExecutionResult
{
    public bool Success { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public enum UserPermission
{
    Everyone = 0,
    Moderator = 1,
    Admin = 2
}
