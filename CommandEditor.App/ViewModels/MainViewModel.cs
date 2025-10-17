using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using CommandEditor.App.Commands;
using CommandEditor.Core.Models;
using CommandEditor.Core.Services;

namespace CommandEditor.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly CommandStorageService _commandStorage;
    private readonly ConfigService _configService;
    private readonly HistoryService _historyService;
    private readonly CurrencyService _currencyService;
    private readonly ITwitchService _twitchService;
    private readonly CommandExecutionService _commandExecutionService;
    private ModeratorsService? _moderatorsService;
    private LogService? _logService;
    private SoundPlaybackService? _soundPlaybackService;
    private System.Windows.Media.MediaPlayer? _mediaPlayer;

    private readonly ObservableCollection<CommandItemViewModel> _commands = new();
    private readonly ObservableCollection<string> _twitchMessages = new();
    private readonly ObservableCollection<string> _twitchViewers = new();
    private readonly ObservableCollection<string> _twitchActiveChatters = new();
    private readonly ObservableCollection<CurrencyUser> _currencyUsers = new();

    private AppConfig _config = new();
    private TwitchSecrets _twitchSecrets = new();
    private CommandItemViewModel? _selectedCommand;
    private AboutViewModel _aboutViewModel = new();
    private string _searchText = string.Empty;
    private string? _currentFile;
    private bool _autoSaveEnabled;
    private int _autoSaveIntervalSeconds = 300;
    private double _volume = 0.5;
    private bool _allowInterruption = true;
    private bool _showInterruptionMessage;
    private bool _isTwitchConnecting;
    private bool _isTwitchConnected;
    private string _twitchChannel = string.Empty;
    private string _pendingMessage = string.Empty;
    private string _soundDirectory = string.Empty;
    private string _twitchConnectionStatus = "Not connected";
    private string _twitchStreamStatus = "Stream: Unknown";
    private string _twitchActiveViewersCount = "Active: 0";
    private string _twitchAllViewersCount = "All: 0";
    private int _maxBackups = 10;
    private string? _lastDismissedVersion;

    public MainViewModel(
        CommandStorageService commandStorage,
        ConfigService configService,
        HistoryService historyService,
        CurrencyService currencyService,
        ITwitchService twitchService,
        CommandExecutionService commandExecutionService)
    {
        _commandStorage = commandStorage;
        _configService = configService;
        _historyService = historyService;
        _currencyService = currencyService;
        _twitchService = twitchService;
        _commandExecutionService = commandExecutionService;

        CommandsView = CollectionViewSource.GetDefaultView(_commands);
        CommandsView.Filter = FilterCommand;

        AddCommandCommand = new RelayCommand(AddCommand);
        RemoveCommandCommand = new RelayCommand(RemoveCommand, () => SelectedCommand is not null);
        DuplicateCommandCommand = new RelayCommand(DuplicateCommand, () => SelectedCommand is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => !string.IsNullOrWhiteSpace(SearchText));
        SaveCurrencySettingsCommand = new RelayCommand(async () => await SaveCurrencySettingsAsync());
        SaveCurrencyUsersCommand = new RelayCommand(async () => await SaveCurrencyUsersAsync());
        ConnectTwitchCommand = new RelayCommand(async () => await ConnectTwitchAsync(), () => !_isTwitchConnecting);
        DisconnectTwitchCommand = new RelayCommand(async () => await DisconnectTwitchAsync(), () => _isTwitchConnected);
        SendTwitchMessageCommand = new RelayCommand(async () => await SendTwitchMessageAsync(), () => _isTwitchConnected && !string.IsNullOrWhiteSpace(PendingMessage));

        _twitchService.MessageReceived += (_, message) => App.Current.Dispatcher.Invoke(() => _twitchMessages.Add(message));
        _twitchService.ViewersUpdated += (_, viewers) => App.Current.Dispatcher.Invoke(() =>
        {
            _twitchViewers.Clear();
            foreach (var viewer in viewers.OrderBy(v => v))
            {
                _twitchViewers.Add(viewer);
            }
            TwitchAllViewersCount = viewers.Count.ToString();
        });
        _twitchService.Connected += (_, _) => App.Current.Dispatcher.Invoke(() =>
        {
            IsTwitchConnected = true;
            TwitchConnectionStatus = $"Connected to: {_config.Twitch.Channel}";
        });
        _twitchService.Disconnected += (_, _) => App.Current.Dispatcher.Invoke(() =>
        {
            IsTwitchConnected = false;
            TwitchConnectionStatus = "Not connected";
            TwitchStreamStatus = "Stream: Unknown";
            TwitchAllViewersCount = "0";
            TwitchActiveViewersCount = "0";
        });
        _twitchService.StreamStatusChanged += (_, isLive) => App.Current.Dispatcher.Invoke(() =>
        {
            TwitchStreamStatus = isLive ? "Stream: LIVE" : "Stream: OFFLINE";
        });

        _commandExecutionService.CommandExecuted += OnCommandExecuted;
        _commandExecutionService.GetUserPermission = DetermineUserPermission;
    }

    public ICollectionView CommandsView { get; }
    public IEnumerable<CommandItemViewModel> Commands => _commands;
    public ObservableCollection<string> TwitchMessages => _twitchMessages;
    public ObservableCollection<string> TwitchViewers => _twitchViewers;
    public ObservableCollection<string> TwitchActiveChatters => _twitchActiveChatters;
    public ObservableCollection<CurrencyUser> CurrencyUsers => _currencyUsers;
    public CurrencySettings CurrencySettings => _currencyService.Settings;
    public AboutViewModel AboutViewModel 
    { 
        get => _aboutViewModel;
        private set
        {
            if (_aboutViewModel != null)
            {
                _aboutViewModel.PropertyChanged -= AboutViewModel_PropertyChanged;
            }
            
            _aboutViewModel = value;
            
            if (_aboutViewModel != null)
            {
                _aboutViewModel.PropertyChanged += AboutViewModel_PropertyChanged;
            }
        }
    }

    private void AboutViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AboutViewModel.UpdateStatus))
        {
            OnPropertyChanged(nameof(AboutTabStatus));
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
    }

    public string AboutTabStatus
    {
        get
        {
            // Provide a simplified status for the tab header
            var status = _aboutViewModel.UpdateStatus;
            if (string.IsNullOrEmpty(status))
                return "About";
            
            // Check for update indicators in the status string
            if (status.Contains("Update available", StringComparison.OrdinalIgnoreCase))
                return "About (Update!)";
            else if (status.Contains("Error", StringComparison.OrdinalIgnoreCase))
                return "About (Error)";
            else if (status.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                return "About (Timeout)";
            else if (status.Contains("Up to date", StringComparison.OrdinalIgnoreCase))
                return "About";
            else
                return "About";
        }
    }

    public bool IsUpdateAvailable
    {
        get
        {
            var status = _aboutViewModel.UpdateStatus;
            return !string.IsNullOrEmpty(status) && status.Contains("Update available", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string IntervalInfoText
    {
        get
        {
            var settings = _currencyService.Settings;
            
            // Calculate min/max per hour for LIVE
            var liveMinPerHour = (60.0 / settings.OnlineIntervalMinutes) * settings.LivePayout;
            var liveMaxPerHour = liveMinPerHour + (60.0 / settings.OnlineIntervalMinutes) * 
                                (settings.RegularBonus + settings.SubBonus + settings.ModBonus + settings.ActiveBonus);
            
            // Calculate min/max per hour for OFFLINE
            var offlineMinPerHour = (60.0 / settings.OfflineIntervalMinutes) * settings.OfflinePayout;
            var offlineMaxPerHour = offlineMinPerHour + (60.0 / settings.OfflineIntervalMinutes) * 
                                   (settings.RegularBonus + settings.SubBonus + settings.ModBonus + settings.ActiveBonus);
            
            return $"[LIVE] Min: {liveMinPerHour:F1}/h - Max: {liveMaxPerHour:F1}/h    [OFFLINE] Min: {offlineMinPerHour:F1}/h - Max: {offlineMaxPerHour:F1}/h";
        }
    }

    public RelayCommand AddCommandCommand { get; }
    public RelayCommand RemoveCommandCommand { get; }
    public RelayCommand DuplicateCommandCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand SaveCurrencySettingsCommand { get; }
    public RelayCommand SaveCurrencyUsersCommand { get; }
    public RelayCommand ConnectTwitchCommand { get; }
    public RelayCommand DisconnectTwitchCommand { get; }
    public RelayCommand SendTwitchMessageCommand { get; }

    public IReadOnlyList<string> RecentFiles => _config.RecentFiles;

    public string? CurrentFile
    {
        get => _currentFile;
        private set => SetProperty(ref _currentFile, value);
    }

    public CommandItemViewModel? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (SetProperty(ref _selectedCommand, value))
            {
                RemoveCommandCommand.RaiseCanExecuteChanged();
                DuplicateCommandCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                CommandsView.Refresh();
                ClearSearchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set
        {
            if (SetProperty(ref _autoSaveEnabled, value))
            {
                _config.AutoSave.Enabled = value;
                PersistConfig();
            }
        }
    }

    public int AutoSaveIntervalSeconds
    {
        get => _autoSaveIntervalSeconds;
        set
        {
            if (SetProperty(ref _autoSaveIntervalSeconds, value))
            {
                _config.AutoSave.IntervalSeconds = Math.Clamp(value, 60, 3600);
                PersistConfig();
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _config.Volume = value;
                PersistConfig();
            }
        }
    }

    public bool AllowSoundInterruption
    {
        get => _allowInterruption;
        set
        {
            if (SetProperty(ref _allowInterruption, value))
            {
                _config.Sound.AllowInterruption = value;
                if (_soundPlaybackService != null)
                {
                    _soundPlaybackService.AllowInterruption = value;
                    Console.WriteLine($"[SoundPlayback] AllowInterruption changed to: {value}");
                    Log($"[SoundPlayback] AllowInterruption changed to: {value}");
                }
                PersistConfig();
            }
        }
    }

    public bool ShowInterruptionMessage
    {
        get => _showInterruptionMessage;
        set
        {
            if (SetProperty(ref _showInterruptionMessage, value))
            {
                _config.Sound.ShowInterruptionMessage = value;
                if (_soundPlaybackService != null)
                {
                    _soundPlaybackService.ShowInterruptionMessage = value;
                    Console.WriteLine($"[SoundPlayback] ShowInterruptionMessage changed to: {value}");
                    Log($"[SoundPlayback] ShowInterruptionMessage changed to: {value}");
                }
                PersistConfig();
            }
        }
    }

    public string SoundDirectory
    {
        get => _soundDirectory;
        set
        {
            if (SetProperty(ref _soundDirectory, value))
            {
                _config.Sound.SoundDirectory = value;
                PersistConfig();
            }
        }
    }

    public bool IsTwitchConnected
    {
        get => _isTwitchConnected;
        private set
        {
            if (SetProperty(ref _isTwitchConnected, value))
            {
                DisconnectTwitchCommand.RaiseCanExecuteChanged();
                SendTwitchMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTwitchConnecting
    {
        get => _isTwitchConnecting;
        private set
        {
            if (SetProperty(ref _isTwitchConnecting, value))
            {
                ConnectTwitchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string TwitchChannel
    {
        get => _twitchChannel;
        set
        {
            if (SetProperty(ref _twitchChannel, value))
            {
                _config.Twitch.Channel = value.Trim();
                PersistConfig();
            }
        }
    }

    public string TwitchAccessToken
    {
        get => _twitchSecrets.AccessToken;
        set
        {
            if (_twitchSecrets.AccessToken != value)
            {
                _twitchSecrets.AccessToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string TwitchClientId
    {
        get => _twitchSecrets.ClientId;
        set
        {
            if (_twitchSecrets.ClientId != value)
            {
                _twitchSecrets.ClientId = value;
                OnPropertyChanged();
            }
        }
    }

    public string TwitchRefreshToken
    {
        get => _twitchSecrets.RefreshToken;
        set
        {
            if (_twitchSecrets.RefreshToken != value)
            {
                _twitchSecrets.RefreshToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string PendingMessage
    {
        get => _pendingMessage;
        set
        {
            if (SetProperty(ref _pendingMessage, value))
            {
                SendTwitchMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string TwitchConnectionStatus
    {
        get => _twitchConnectionStatus;
        set => SetProperty(ref _twitchConnectionStatus, value);
    }

    public string TwitchStreamStatus
    {
        get => _twitchStreamStatus;
        set => SetProperty(ref _twitchStreamStatus, value);
    }

    public string TwitchActiveViewersCount
    {
        get => _twitchActiveViewersCount;
        set => SetProperty(ref _twitchActiveViewersCount, value);
    }

    public string TwitchAllViewersCount
    {
        get => _twitchAllViewersCount;
        set => SetProperty(ref _twitchAllViewersCount, value);
    }

    public int MaxBackups
    {
        get => _maxBackups;
        set
        {
            if (SetProperty(ref _maxBackups, value))
            {
                PersistConfig();
            }
        }
    }

    public string? LastDismissedVersion
    {
        get => _lastDismissedVersion;
        set
        {
            if (SetProperty(ref _lastDismissedVersion, value))
            {
                // Update the AboutViewModel's LastDismissedVersion property as well
                AboutViewModel.LastDismissedVersion = value;
                PersistConfig();
            }
        }
    }

    public bool EnableDebugLogging
    {
        get => _config.EnableDebugLogging;
        set
        {
            _config.EnableDebugLogging = value;
            if (_logService != null)
            {
                _logService.IsEnabled = value;
            }
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        _config = _configService.Load();
        _twitchSecrets = _configService.LoadTwitchSecrets();
        CurrentFile = _config.CurrentFile;
        _autoSaveEnabled = _config.AutoSave.Enabled;
        _autoSaveIntervalSeconds = _config.AutoSave.IntervalSeconds;
        _volume = _config.Volume;
        _allowInterruption = _config.Sound.AllowInterruption;
        _showInterruptionMessage = _config.Sound.ShowInterruptionMessage;
        _twitchChannel = _config.Twitch.Channel;
        _soundDirectory = _config.Sound.SoundDirectory;
        MaxBackups = _config.MaxBackups;
        LastDismissedVersion = _config.LastDismissedVersion;
        // Ensure the AboutViewModel also has the correct LastDismissedVersion when initializing
        AboutViewModel.LastDismissedVersion = _config.LastDismissedVersion;
        
        // Update sound playback service with config settings
        if (_soundPlaybackService != null)
        {
            _soundPlaybackService.AllowInterruption = _config.Sound.AllowInterruption;
            _soundPlaybackService.ShowInterruptionMessage = _config.Sound.ShowInterruptionMessage;
            Console.WriteLine($"[SoundPlayback] Settings loaded - AllowInterruption: {_config.Sound.AllowInterruption}, ShowMessage: {_config.Sound.ShowInterruptionMessage}");
            Log($"[SoundPlayback] Settings loaded - AllowInterruption: {_config.Sound.AllowInterruption}, ShowMessage: {_config.Sound.ShowInterruptionMessage}");
        }

        var commands = await _commandStorage.LoadAsync(CurrentFile);
        ReplaceCommands(commands);
        if (!string.IsNullOrWhiteSpace(_commandStorage.LastLoadedPath))
        {
            SetCurrentFile(_commandStorage.LastLoadedPath);
        }

        await _currencyService.InitializeAsync();
        RefreshCurrencyUsers();

        RaiseAll();
    }

    public async Task LoadCommandsAsync(string filePath)
    {
        var commands = await _commandStorage.LoadAsync(filePath);
        ReplaceCommands(commands);
        if (!string.IsNullOrWhiteSpace(_commandStorage.LastLoadedPath))
        {
            SetCurrentFile(_commandStorage.LastLoadedPath);
        }
    }

    public async Task SaveCommandsAsync(string? filePath = null)
    {
        var actualPath = string.IsNullOrWhiteSpace(filePath) ? CurrentFile : filePath;
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return;
        }

        var models = _commands.Select(x => x.Model).ToList();
        await _commandStorage.SaveAsync(models, actualPath);
        await _historyService.SaveBackupAsync(models, MaxBackups);
        SetCurrentFile(actualPath);
    }

    public async Task SaveLegacyAsync(string filePath)
    {
        var models = _commands.Select(x => x.Model).ToList();
        await _commandStorage.SaveLegacyAsync(models, filePath);
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync() => await _historyService.GetBackupsAsync();

    public async Task RestoreHistoryAsync(string filePath)
    {
        var commands = await _historyService.RestoreAsync(filePath);
        ReplaceCommands(commands);
    }

    public async Task AutoSaveAsync()
    {
        if (!AutoSaveEnabled || string.IsNullOrWhiteSpace(CurrentFile))
        {
            return;
        }

        await SaveCommandsAsync(CurrentFile);
    }

    public void UpdateSoundDirectory(string directory)
    {
        SoundDirectory = directory;
    }

    public async Task SaveTwitchSecretsAsync()
    {
        _configService.SaveTwitchSecrets(_twitchSecrets);
        await Task.CompletedTask;
    }

    private void ReplaceCommands(IEnumerable<CommandItem> commands)
    {
        _commands.Clear();
        foreach (var command in commands)
        {
            _commands.Add(new CommandItemViewModel(command));
        }
        CommandsView.Refresh();
    }

    private void AddCommand()
    {
        var model = new CommandItem
        {
            Command = "!new",
            Enabled = true,
            Permission = CommandPermission.Everyone,
            Usage = CommandUsage.StreamlabsChatbot,
            Volume = (int)Math.Clamp(Math.Round(Volume * 100d), 0d, 100d)
        };
        var vm = new CommandItemViewModel(model);
        _commands.Add(vm);
        SelectedCommand = vm;
    }

    private void RemoveCommand()
    {
        if (SelectedCommand is null)
        {
            return;
        }

        _commands.Remove(SelectedCommand);
        SelectedCommand = _commands.LastOrDefault();
    }

    private void DuplicateCommand()
    {
        if (SelectedCommand is null)
        {
            return;
        }

        var clone = new CommandItem
        {
            Command = SelectedCommand.Command + "_copy",
            Permission = SelectedCommand.Permission,
            Info = SelectedCommand.Info,
            Group = SelectedCommand.Group,
            Response = SelectedCommand.Response,
            Cooldown = SelectedCommand.Cooldown,
            UserCooldown = SelectedCommand.UserCooldown,
            Cost = SelectedCommand.Cost,
            Count = SelectedCommand.Count,
            Usage = SelectedCommand.Usage,
            Enabled = SelectedCommand.Enabled,
            SoundFile = SelectedCommand.SoundFile,
            FkSoundFile = SelectedCommand.FkSoundFile,
            Volume = SelectedCommand.Volume
        };
        var vm = new CommandItemViewModel(clone);
        _commands.Add(vm);
        SelectedCommand = vm;
    }

    private bool FilterCommand(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        if (obj is not CommandItemViewModel command)
        {
            return false;
        }

        var text = SearchText.Trim();
        return command.Command.Contains(text, StringComparison.OrdinalIgnoreCase)
            || command.Response.Contains(text, StringComparison.OrdinalIgnoreCase)
            || command.Info.Contains(text, StringComparison.OrdinalIgnoreCase)
            || command.Group.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveCurrencySettingsAsync()
    {
        await _currencyService.SaveSettingsAsync();
        OnPropertyChanged(nameof(IntervalInfoText));
    }

    private async Task SaveCurrencyUsersAsync()
    {
        _currencyService.ReplaceUsers(_currencyUsers);
        await _currencyService.SaveUsersAsync();
        RefreshCurrencyUsers();
    }

    private async Task ConnectTwitchAsync()
    {
        if (IsTwitchConnected)
        {
            return;
        }

        IsTwitchConnecting = true;
        try
        {
            await _twitchService.ConnectAsync(_twitchSecrets, _config.Twitch);
            IsTwitchConnected = _twitchService.IsConnected;
            if (IsTwitchConnected)
            {
                TwitchConnectionStatus = $"Connected to: {_config.Twitch.Channel}";
                await SaveTwitchSecretsAsync();
            }
        }
        finally
        {
            IsTwitchConnecting = false;
        }
    }

    private async Task DisconnectTwitchAsync()
    {
        await _twitchService.DisconnectAsync();
        IsTwitchConnected = false;
        TwitchConnectionStatus = "Not connected";
        TwitchStreamStatus = "Stream: Unknown";
    }

    private async Task SendTwitchMessageAsync()
    {
        if (!IsTwitchConnected)
        {
            return;
        }

        await _twitchService.SendMessageAsync(PendingMessage);
        PendingMessage = string.Empty;
    }

    private void RefreshCurrencyUsers()
    {
        _currencyUsers.Clear();
        foreach (var user in _currencyService.Users.OrderBy(u => u.Username))
        {
            _currencyUsers.Add(user);
        }
    }

    public async Task LoadCurrencyUsersAsync()
    {
        await _currencyService.InitializeAsync();
        RefreshCurrencyUsers();
    }

    private void SetCurrentFile(string filePath)
    {
        CurrentFile = filePath;
        _config.CurrentFile = filePath;
        UpdateRecentFiles(filePath);
        PersistConfig();
    }

    private void UpdateRecentFiles(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalized = Path.GetFullPath(filePath);
        _config.RecentFiles.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        _config.RecentFiles.Insert(0, normalized);
        while (_config.RecentFiles.Count > 10)
        {
            _config.RecentFiles.RemoveAt(_config.RecentFiles.Count - 1);
        }
        OnPropertyChanged(nameof(RecentFiles));
    }

    internal void PersistConfig()
    {
        _config.MaxBackups = MaxBackups;
        _config.LastDismissedVersion = LastDismissedVersion;
        _configService.Save(_config);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(AutoSaveEnabled));
        OnPropertyChanged(nameof(AutoSaveIntervalSeconds));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(AllowSoundInterruption));
        OnPropertyChanged(nameof(ShowInterruptionMessage));
        OnPropertyChanged(nameof(TwitchChannel));
        OnPropertyChanged(nameof(TwitchAccessToken));
        OnPropertyChanged(nameof(TwitchClientId));
        OnPropertyChanged(nameof(TwitchRefreshToken));
        OnPropertyChanged(nameof(RecentFiles));
        OnPropertyChanged(nameof(SoundDirectory));
    }

    private async void OnCommandExecuted(object? sender, CommandExecutedEventArgs e)
    {
        await App.Current.Dispatcher.InvokeAsync(async () =>
        {
            // Normalize command name (remove ! prefix for comparison)
            var normalizedCommandName = e.CommandName.TrimStart('!').ToLowerInvariant();
            
            Log($"[CommandExecution] Command received: {e.CommandName} from {e.Username}");
            
            // Find the command
            var command = _commands.FirstOrDefault(c => 
                c.Model.NormalizedKey.Equals(normalizedCommandName, StringComparison.OrdinalIgnoreCase));

            if (command == null)
            {
                Log($"[CommandExecution] Command not found: {normalizedCommandName}");
                return;
            }
            
            Log($"[CommandExecution] Command found: {command.Command}");
            
            // Check if command is enabled
            if (!command.Enabled)
            {
                Log($"[CommandExecution] Command is disabled: {command.Command}");
                return;
            }

            // Determine user permission level
            var userPermission = DetermineUserPermission(e.Username);
            Log($"[CommandExecution] User {e.Username} has permission: {userPermission}, Command requires: {command.Permission}");

            // Execute the command
            var result = await _commandExecutionService.ExecuteCommandAsync(
                command.Model,
                e.Username,
                e.Arguments,
                userPermission);

            // If command was executed, save the updated count
            if (result.Success)
            {
                Log($"[CommandExecution] Command executed successfully!");
                await AutoSaveAsync();
            }
            else
            {
                Log($"[CommandExecution] Command execution failed: {result.Reason}");
            }
        });
    }

    private void OnSoundPlaybackRequested(object? sender, SoundPlaybackRequestEventArgs e)
    {
        // Play sound on the UI thread
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            Log($"[SoundPlayback] Sound requested: {e.SoundFile} (Volume: {e.Volume})");

            try
            {
                var extension = System.IO.Path.GetExtension(e.SoundFile).ToLowerInvariant();
                var volumeDecimal = e.Volume / 100.0; // Convert 0-100 to 0.0-1.0
                
                Log($"[SoundPlayback] Playing sound file (type: {extension}, volume: {volumeDecimal:F2})");
                
                if (extension == ".wav")
                {
                    // Use SoundPlayer for WAV files
                    // Note: SoundPlayer doesn't support volume, so WAV files play at system volume
                    var player = new System.Media.SoundPlayer(e.SoundFile);
                    player.Play();
                    Log($"[SoundPlayback] WAV file played successfully (system volume)");
                }
                else
                {
                    // Use MediaPlayer for MP3 and other formats with volume support
                    if (_mediaPlayer == null)
                    {
                        _mediaPlayer = new System.Windows.Media.MediaPlayer();
                        _mediaPlayer.MediaEnded += (s, args) =>
                        {
                            _soundPlaybackService?.NotifyPlaybackEnded();
                            Log($"[SoundPlayback] Playback ended");
                        };
                        _mediaPlayer.MediaFailed += (s, args) =>
                        {
                            _soundPlaybackService?.NotifyPlaybackEnded();
                            Log($"[SoundPlayback] Playback failed: {args.ErrorException?.Message}");
                        };
                    }
                    
                    _mediaPlayer.Stop();
                    _mediaPlayer.Open(new Uri(e.SoundFile, UriKind.Absolute));
                    _mediaPlayer.Volume = volumeDecimal;
                    _mediaPlayer.Play();
                    Log($"[SoundPlayback] Media file played with MediaPlayer at volume {volumeDecimal:F2}");
                }
            }
            catch (Exception ex)
            {
                Log($"[SoundPlayback] Error playing sound: {ex.Message}");
                _soundPlaybackService?.NotifyPlaybackEnded();
            }
        });
    }

    public async Task CreateBackupAsync()
    {
        var commands = _commands.Select(c => c.Model).ToList();
        await _historyService.SaveBackupAsync(commands, MaxBackups);
    }

    public void RefreshIntervalInfo()
    {
        OnPropertyChanged(nameof(IntervalInfoText));
    }

    public void SetModeratorsService(ModeratorsService moderatorsService)
    {
        _moderatorsService = moderatorsService;
    }

    public void SetLogService(LogService logService)
    {
        _logService = logService;
    }

    public void SetSoundPlaybackService(SoundPlaybackService soundPlaybackService)
    {
        _soundPlaybackService = soundPlaybackService;
        _soundPlaybackService.PlaybackRequested += OnSoundPlaybackRequested;
    }

    private UserPermission DetermineUserPermission(string username)
    {
        var normalizedUsername = username.ToLowerInvariant();
        var channelName = _config.Twitch.Channel.TrimStart('#').ToLowerInvariant();

        Log($"[Permission] Checking permission for '{normalizedUsername}' vs channel '{channelName}'");

        // Broadcaster has Admin permission
        if (normalizedUsername == channelName)
        {
            Log($"[Permission] User is broadcaster -> Admin");
            return UserPermission.Admin;
        }

        // Check if user is a moderator
        if (_moderatorsService != null && _moderatorsService.IsModerator(username))
        {
            Log($"[Permission] User is moderator -> Moderator");
            return UserPermission.Moderator;
        }

        // Default to Everyone
        Log($"[Permission] User is regular viewer -> Everyone");
        return UserPermission.Everyone;
    }

    private void Log(string message)
    {
        _logService?.Log(message);
    }

    public void StopAllSounds()
    {
        // Stop the MediaPlayer used for command sounds
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Stop();
            Log("[SoundPlayback] UI MediaPlayer stopped");
        }
        
        // Stop the SoundPlaybackService used by Twitch chat commands
        if (_soundPlaybackService != null)
        {
            _soundPlaybackService.StopPlayback();
            Log("[SoundPlayback] SoundPlaybackService stopped");
        }
        
        // Notify the sound playback service that playback ended
        _soundPlaybackService?.NotifyPlaybackEnded();
        Log("[SoundPlayback] All sounds stopped successfully");
    }
}

internal static class ListExtensions
{
    public static void RemoveAll<T>(this IList<T> list, Func<T, bool> predicate)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (predicate(list[i]))
            {
                list.RemoveAt(i);
            }
        }
    }
}
