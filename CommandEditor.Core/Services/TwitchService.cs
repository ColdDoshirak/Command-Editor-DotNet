using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public sealed class TwitchService : ITwitchService
{
    private TwitchClient? _client;
    private readonly HashSet<string> _viewers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _isDisposed;
    private readonly HttpClient _httpClient = new();
    private TwitchSecrets? _secrets;
    private TwitchChannelConfig? _channelConfig;
    private string? _broadcasterId;
    private Timer? _streamStatusTimer;
    private Timer? _moderatorsTimer;
    private Timer? _viewersTimer;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<IReadOnlyCollection<string>>? ViewersUpdated;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<bool>? StreamStatusChanged;
    public event EventHandler<IReadOnlyCollection<string>>? ModeratorsUpdated;

    public bool IsConnected { get; private set; }
    public bool IsLive { get; private set; }

    public Task ConnectAsync(TwitchSecrets secrets, TwitchChannelConfig channel, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(secrets.AccessToken))
        {
            throw new InvalidOperationException("Access token is required");
        }

        if (string.IsNullOrWhiteSpace(channel.Channel))
        {
            throw new InvalidOperationException("Channel name is required");
        }

        _secrets = secrets;
        _channelConfig = channel;

        try
        {
            // Create credentials using the bot's username from the channel config or default to "bot"
            var botUsername = string.IsNullOrWhiteSpace(channel.Channel) ? "bot" : channel.Channel.TrimStart('#');
            var credentials = new ConnectionCredentials(botUsername, secrets.AccessToken);

            // Create WebSocket client
            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = 750,
                ThrottlingPeriod = TimeSpan.FromSeconds(30)
            };
            var websocketClient = new WebSocketClient(clientOptions);

            // Create TwitchLib client
            _client = new TwitchClient(websocketClient);
            _client.Initialize(credentials, channel.Channel.TrimStart('#'));

            // Setup event handlers
            _client.OnConnected += OnConnected;
            _client.OnDisconnected += OnDisconnected;
            _client.OnMessageReceived += OnMessageReceived;
            _client.OnJoinedChannel += OnJoinedChannel;
            _client.OnUserJoined += OnUserJoined;
            _client.OnUserLeft += OnUserLeft;
            _client.OnError += OnError;
            _client.OnConnectionError += OnConnectionError;

            // Connect to Twitch
            _client.Connect();

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            RaiseMessageReceived($"[ERROR] Connection failed: {ex.Message}");
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        if (!IsConnected || _client == null)
        {
            // Even if not connected, we should reset the status for UI consistency
            IsConnected = false;
            bool wasLive = IsLive;
            IsLive = false;
            
            if (wasLive)
            {
                StreamStatusChanged?.Invoke(this, false); // Stream is now offline
            }
            
            _broadcasterId = null;
            
            // Stop timers
            _streamStatusTimer?.Dispose();
            _streamStatusTimer = null;
            _moderatorsTimer?.Dispose();
            _moderatorsTimer = null;
            _viewersTimer?.Dispose();
            _viewersTimer = null;
            
            ClearViewers();
            return Task.CompletedTask;
        }

        try
        {
            _client.Disconnect();
            CleanupClient();
        }
        catch (Exception ex)
        {
            RaiseMessageReceived($"[ERROR] Disconnect failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("Not connected to Twitch");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        try
        {
            var channel = _client.JoinedChannels.FirstOrDefault();
            if (channel != null)
            {
                _client.SendMessage(channel, message);
            }
        }
        catch (Exception ex)
        {
            RaiseMessageReceived($"[ERROR] Failed to send message: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    private async void OnConnected(object? sender, OnConnectedArgs e)
    {
        IsConnected = true;
        RaiseMessageReceived($"[SYSTEM] Connected to Twitch IRC as {e.BotUsername}");
        
        // Fetch broadcaster ID
        await FetchBroadcasterIdAsync();
        
        // Immediately check stream status after getting broadcaster ID, then start polling every 60 seconds
        await CheckStreamStatusAsync();
        
        _streamStatusTimer = new Timer(async _ => await CheckStreamStatusAsync(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        
        // Start polling moderators every 5 minutes
        _moderatorsTimer = new Timer(async _ => await FetchModeratorsAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        
        // Start polling viewers every 30 seconds (default, can be changed)
        _viewersTimer = new Timer(async _ => await FetchViewersAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void OnDisconnected(object? sender, TwitchLib.Communication.Events.OnDisconnectedEventArgs e)
    {
        IsConnected = false;
        bool wasLive = IsLive;
        IsLive = false;
        
        // Only trigger stream status change if we were previously live
        if (wasLive)
        {
            StreamStatusChanged?.Invoke(this, false); // Stream is now offline
        }
        
        _broadcasterId = null;
        
        // Stop timers
        _streamStatusTimer?.Dispose();
        _streamStatusTimer = null;
        _moderatorsTimer?.Dispose();
        _moderatorsTimer = null;
        _viewersTimer?.Dispose();
        _viewersTimer = null;
        
        RaiseMessageReceived("[SYSTEM] Disconnected from Twitch IRC");
        ClearViewers();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        RaiseMessageReceived($"[SYSTEM] Joined channel: #{e.Channel}");
    }

    private void OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        var msg = e.ChatMessage;
        var displayName = msg.DisplayName ?? msg.Username;
        var message = $"[{displayName}]: {msg.Message}";
        RaiseMessageReceived(message);

        // Track active chatters
        AddViewer(msg.Username);
    }

    private void OnUserJoined(object? sender, OnUserJoinedArgs e)
    {
        AddViewer(e.Username);
    }

    private void OnUserLeft(object? sender, OnUserLeftArgs e)
    {
        RemoveViewer(e.Username);
    }

    private void OnError(object? sender, TwitchLib.Communication.Events.OnErrorEventArgs e)
    {
        RaiseMessageReceived($"[ERROR] {e.Exception.Message}");
    }

    private void OnConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        RaiseMessageReceived($"[ERROR] Connection error: {e.Error.Message}");
    }

    private void AddViewer(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        lock (_lock)
        {
            if (_viewers.Add(username))
            {
                RaiseViewersUpdated();
            }
        }
    }

    private void RemoveViewer(string username)
    {
        lock (_lock)
        {
            if (_viewers.Remove(username))
            {
                RaiseViewersUpdated();
            }
        }
    }

    private void ClearViewers()
    {
        lock (_lock)
        {
            _viewers.Clear();
            RaiseViewersUpdated();
        }
    }

    private void RaiseMessageReceived(string message)
    {
        MessageReceived?.Invoke(this, message);
    }

    private void RaiseViewersUpdated()
    {
        lock (_lock)
        {
            var viewersList = _viewers.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            ViewersUpdated?.Invoke(this, viewersList);
        }
    }

    private async Task FetchBroadcasterIdAsync()
    {
        if (_secrets == null || _channelConfig == null) 
        {
            RaiseMessageReceived("[ERROR] Secrets or channel config not available for fetching broadcaster ID");
            return;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/users?login={_channelConfig.Channel.TrimStart('#')}");
            request.Headers.Add("Authorization", $"Bearer {_secrets.AccessToken}");
            request.Headers.Add("Client-Id", _secrets.ClientId);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                if (data.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
                {
                    _broadcasterId = dataArray[0].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(_broadcasterId))
                    {
                        RaiseMessageReceived($"[SYSTEM] Broadcaster ID: {_broadcasterId}");
                    }
                    else
                    {
                        RaiseMessageReceived("[ERROR] Could not retrieve broadcaster ID from API response");
                    }
                }
                else
                {
                    RaiseMessageReceived("[ERROR] No user data found in API response for broadcaster ID");
                }
            }
            else
            {
                RaiseMessageReceived($"[ERROR] Failed to fetch broadcaster ID: HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            RaiseMessageReceived($"[ERROR] Failed to fetch broadcaster ID: {ex.Message}");
        }
    }

    private async Task CheckStreamStatusAsync()
    {
        if (_secrets == null || string.IsNullOrEmpty(_broadcasterId)) 
        {
            // If we don't have the required data, assume stream is offline
            // This ensures the UI gets updated with a definite status
            bool previousIsLive = IsLive;
            IsLive = false; // If we can't check, assume offline
            
            // Fire the event to update the UI
            StreamStatusChanged?.Invoke(this, false);
            
            if (previousIsLive) // Only log if we were previously live
            {
                RaiseMessageReceived("[SYSTEM] Stream status: OFFLINE (Insufficient data to verify)");
            }
            return;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/streams?user_id={_broadcasterId}");
            request.Headers.Add("Authorization", $"Bearer {_secrets.AccessToken}");
            request.Headers.Add("Client-Id", _secrets.ClientId);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                bool isLive = false;
                if (data.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
                {
                    isLive = true;
                }

                // Always update the internal state with the current status
                bool previousIsLive = IsLive;
                IsLive = isLive;
                
                // Fire the event to update the UI, even if the status hasn't changed
                // This ensures that the UI is updated with the current known status
                StreamStatusChanged?.Invoke(this, isLive);
                
                if (isLive != previousIsLive)
                {
                    RaiseMessageReceived($"[SYSTEM] Stream status: {(isLive ? "LIVE" : "OFFLINE")}");
                }
                else
                {
                    RaiseMessageReceived($"[SYSTEM] Stream status confirmed: {(isLive ? "LIVE" : "OFFLINE")}");
                }
            }
            else
            {
                // If the API call fails, treat as offline but still update the UI
                bool previousIsLive = IsLive;
                IsLive = false; // Assume offline when API fails
                
                // Fire the event to update the UI
                StreamStatusChanged?.Invoke(this, false);
                
                if (previousIsLive) // Only log if we were previously live
                {
                    RaiseMessageReceived($"[SYSTEM] Stream status: OFFLINE (API Error: HTTP {response.StatusCode})");
                }
                else
                {
                    RaiseMessageReceived($"[ERROR] Failed to check stream status: HTTP {response.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            // If there's an exception, treat as offline but still update the UI
            bool previousIsLive = IsLive;
            IsLive = false; // Assume offline when exception occurs
            
            // Fire the event to update the UI
            StreamStatusChanged?.Invoke(this, false);
            
            if (previousIsLive) // Only log if we were previously live
            {
                RaiseMessageReceived($"[SYSTEM] Stream status: OFFLINE (Exception: {ex.Message})");
            }
            else
            {
                RaiseMessageReceived($"[ERROR] Failed to check stream status: {ex.Message}");
            }
        }
    }

    private async Task FetchModeratorsAsync()
    {
        if (_secrets == null || string.IsNullOrEmpty(_broadcasterId)) return;

        try
        {
            var moderators = new List<string>();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/moderation/moderators?broadcaster_id={_broadcasterId}");
            request.Headers.Add("Authorization", $"Bearer {_secrets.AccessToken}");
            request.Headers.Add("Client-Id", _secrets.ClientId);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (data.TryGetProperty("data", out var dataArray))
                {
                    foreach (var mod in dataArray.EnumerateArray())
                    {
                        if (mod.TryGetProperty("user_login", out var login))
                        {
                            moderators.Add(login.GetString() ?? "");
                        }
                    }
                }

                ModeratorsUpdated?.Invoke(this, moderators);
                RaiseMessageReceived($"[SYSTEM] Fetched {moderators.Count} moderators from API");
            }
        }
        catch (Exception ex)
        {
            RaiseMessageReceived($"[ERROR] Failed to fetch moderators: {ex.Message}");
        }
    }

    public async Task<List<string>> GetModeratorsAsync(CancellationToken cancellationToken = default)
    {
        if (_secrets == null || string.IsNullOrEmpty(_broadcasterId)) 
            return new List<string>();

        try
        {
            var moderators = new List<string>();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/moderation/moderators?broadcaster_id={_broadcasterId}");
            request.Headers.Add("Authorization", $"Bearer {_secrets.AccessToken}");
            request.Headers.Add("Client-Id", _secrets.ClientId);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (data.TryGetProperty("data", out var dataArray))
                {
                    foreach (var mod in dataArray.EnumerateArray())
                    {
                        if (mod.TryGetProperty("user_login", out var login))
                        {
                            moderators.Add(login.GetString() ?? "");
                        }
                    }
                }
            }
            return moderators;
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task FetchViewersAsync()
    {
        if (string.IsNullOrEmpty(_broadcasterId) || _secrets == null || string.IsNullOrEmpty(_secrets.AccessToken))
        {
            return;
        }

        try
        {
            // Use /chat/chatters endpoint to get all current chatters
            var request = new HttpRequestMessage(HttpMethod.Get, 
                $"https://api.twitch.tv/helix/chat/chatters?broadcaster_id={_broadcasterId}&moderator_id={_broadcasterId}");
            request.Headers.Add("Authorization", $"Bearer {_secrets.AccessToken}");
            request.Headers.Add("Client-Id", _secrets.ClientId);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var fetchedViewers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var user in data.EnumerateArray())
                    {
                        if (user.TryGetProperty("user_login", out var login))
                        {
                            var username = login.GetString();
                            if (!string.IsNullOrEmpty(username))
                            {
                                fetchedViewers.Add(username.ToLowerInvariant());
                            }
                        }
                    }
                }

                // Update the viewers collection
                lock (_lock)
                {
                    _viewers.Clear();
                    foreach (var viewer in fetchedViewers)
                    {
                        _viewers.Add(viewer);
                    }
                    RaiseViewersUpdated();
                }
            }
        }
        catch
        {
            // Silent fail - keep existing viewer list
        }
    }

    public async Task RefreshViewersAsync()
    {
        await FetchViewersAsync();
    }

    public void SetViewersUpdateInterval(int seconds)
    {
        if (seconds < 10 || seconds > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Interval must be between 10 and 300 seconds");
        }

        _viewersTimer?.Dispose();
        if (IsConnected)
        {
            _viewersTimer = new Timer(async _ => await FetchViewersAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
        }
    }

    private void CleanupClient()
    {
        if (_client == null)
        {
            return;
        }

        _client.OnConnected -= OnConnected;
        _client.OnDisconnected -= OnDisconnected;
        _client.OnMessageReceived -= OnMessageReceived;
        _client.OnJoinedChannel -= OnJoinedChannel;
        _client.OnUserJoined -= OnUserJoined;
        _client.OnUserLeft -= OnUserLeft;
        _client.OnError -= OnError;
        _client.OnConnectionError -= OnConnectionError;

        _client = null;
        IsConnected = false;
        bool wasLive = IsLive;
        IsLive = false;
        
        if (wasLive)
        {
            StreamStatusChanged?.Invoke(this, false); // Stream is now offline
        }
        
        _broadcasterId = null;
        ClearViewers();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        await DisconnectAsync();
        CleanupClient();
        
        _streamStatusTimer?.Dispose();
        _streamStatusTimer = null;
        _moderatorsTimer?.Dispose();
        _moderatorsTimer = null;
        _viewersTimer?.Dispose();
        _viewersTimer = null;
        _httpClient?.Dispose();
    }
}
