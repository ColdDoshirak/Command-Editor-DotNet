using System.Threading;
using System.Threading.Tasks;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public interface ITwitchService : IAsyncDisposable
{
    event EventHandler<string>? MessageReceived;
    event EventHandler<IReadOnlyCollection<string>>? ViewersUpdated;
    event EventHandler? Connected;
    event EventHandler? Disconnected;
    event EventHandler<bool>? StreamStatusChanged;
    event EventHandler<IReadOnlyCollection<string>>? ModeratorsUpdated;
    Task ConnectAsync(TwitchSecrets secrets, TwitchChannelConfig channel, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
    Task<List<string>> GetModeratorsAsync(CancellationToken cancellationToken = default);
    Task RefreshViewersAsync();
    void SetViewersUpdateInterval(int seconds);
    bool IsConnected { get; }
    bool IsLive { get; }
}
