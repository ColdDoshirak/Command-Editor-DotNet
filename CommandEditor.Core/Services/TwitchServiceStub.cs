using System;
using System.Threading;
using System.Threading.Tasks;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public sealed class TwitchServiceStub : ITwitchService
{
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
        IsConnected = true;
        MessageReceived?.Invoke(this, $"Connected to {channel.Channel} (stub)");
        ViewersUpdated?.Invoke(this, Array.Empty<string>());
        Connected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (IsConnected)
        {
            MessageReceived?.Invoke(this, "Disconnected from Twitch (stub)");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            MessageReceived?.Invoke(this, $"[BOT]: {message}");
        }
        return Task.CompletedTask;
    }

    public Task<List<string>> GetModeratorsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>());
    }

    public Task RefreshViewersAsync()
    {
        return Task.CompletedTask;
    }

    public void SetViewersUpdateInterval(int seconds)
    {
        // Stub - no implementation needed
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        IsLive = false;
        return ValueTask.CompletedTask;
    }
}
