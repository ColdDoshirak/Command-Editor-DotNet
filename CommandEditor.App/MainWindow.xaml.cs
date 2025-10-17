using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommandEditor.App.ViewModels;
using CommandEditor.Core.Models;
using CommandEditor.Core.Services;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfButton = System.Windows.Controls.Button;
using CommandEditor.App.Views;

namespace CommandEditor.App;

public partial class MainWindow : Window
{
    private readonly MediaPlayer _mediaPlayer = new();
    private bool _isSoundPlaying;
    private System.Windows.Controls.Button? _activeSoundButton;
    private readonly ModeratorsService _moderatorsService;
    private readonly ITwitchService _twitchService;
    private System.Windows.Threading.DispatcherTimer? _currencyRefreshTimer;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow(ModeratorsService moderatorsService, ITwitchService twitchService)
    {
        _moderatorsService = moderatorsService;
        _twitchService = twitchService;
        
        InitializeComponent();
        Loaded += OnLoaded;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        
        // Subscribe to moderators updates from Twitch API
        _twitchService.ModeratorsUpdated += OnModeratorsUpdated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshHistoryAsync();
        
        // Start currency auto-refresh timer
        StartCurrencyAutoRefresh();
        
        // Set the about tab's data context to the about view model
        var mainViewModel = (MainViewModel)DataContext;
        var aboutViewModel = mainViewModel.AboutViewModel;
        
        // Set owner window for update notifications
        aboutViewModel.OwnerWindow = this;
        
        // Start automatic update checking (like the reference implementation)
        aboutViewModel.CheckForUpdatesOnStartup();
        
        AboutTab.DataContext = aboutViewModel;
    }

    private async Task RefreshHistoryAsync()
    {
        var history = await ViewModel.GetHistoryAsync();
        HistoryList.ItemsSource = history
            .Select(h => new HistoryListItem(h))
            .ToList();
    }

    private async void OnRefreshHistory(object sender, RoutedEventArgs e)
    {
        await RefreshHistoryAsync();
    }

    private async void OnRestoreHistory(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryListItem item)
        {
            WpfMessageBox.Show("Select a backup to restore.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ViewModel.RestoreHistoryAsync(item.Entry.FilePath);
        WpfMessageBox.Show("Commands restored from backup.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnLoadCommands(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Command Files|*.json;*.abcomg|JSON Files|*.json|Legacy Command Files|*.abcomg|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            var selectedPath = dialog.FileName;
            var wasLegacy = string.Equals(Path.GetExtension(selectedPath), ".abcomg", StringComparison.OrdinalIgnoreCase);
            await ViewModel.LoadCommandsAsync(selectedPath);

            if (wasLegacy)
            {
                WpfMessageBox.Show("Legacy commands converted and loaded successfully.", "Load", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void OnSaveCommands(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentFile))
        {
            OnSaveCommandsAs(sender, e);
            return;
        }

        await ViewModel.SaveCommandsAsync(ViewModel.CurrentFile);
        WpfMessageBox.Show("Commands saved.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnSaveCommandsAs(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Filter = "JSON Files|*.json|All Files|*.*",
            OverwritePrompt = true,
            FileName = ViewModel.CurrentFile ?? "commands.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.SaveCommandsAsync(dialog.FileName);
            WpfMessageBox.Show("Commands saved.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnSaveLegacyFormat(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Filter = "Legacy Command Files|*.abcomg|All Files|*.*",
            OverwritePrompt = true,
            FileName = Path.ChangeExtension(ViewModel.CurrentFile ?? "commands", ".abcomg")
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.SaveLegacyAsync(dialog.FileName);
            WpfMessageBox.Show("Commands saved in legacy format.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnExportLegacyFormat(object sender, RoutedEventArgs e)
    {
        try
        {
            // Export all data in legacy format
            var paths = new AppPaths(AppContext.BaseDirectory);
            var configService = new ConfigService(paths);
            var commandStorage = new CommandStorageService(paths);
            var moderatorsService = new ModeratorsService(paths.DataDirectory);
            
            // Export config
            var config = configService.Load();
            configService.SaveConfigLegacy(config);
            
            // Export secret Twitch config
            var secrets = configService.LoadTwitchSecrets();
            configService.SaveTwitchSecretsLegacy(secrets);
            
            // Export commands
            var commands = ViewModel.Commands.Select(c => c.Model);
            await commandStorage.ExportLegacyFormatAsync(commands);
            
            // Export currency users
            var currencyService = new CurrencyService(paths);
            await currencyService.ExportLegacyFormatAsync(currencyService.Users);
            
            // Export moderators (NEW)
            await moderatorsService.ExportLegacyFormatAsync();
            
            WpfMessageBox.Show(
                "Legacy format files exported successfully!\n\n" +
                "Created files:\n" +
                "• config_legacy.json\n" +
                "• twitch_config_legacy.json\n" +
                "• commands_legacy.json\n" +
                "• users_currency_legacy.json\n" +
                "• moderators_legacy.json\n\n" +
                "These files are compatible with the older reference version.",
                "Export Complete", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to export legacy format: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRunAutoSave(object sender, RoutedEventArgs e)
    {
        await ViewModel.AutoSaveAsync();
        WpfMessageBox.Show("Auto-save complete.", "Auto-Save", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnAutoAssignSounds(object sender, RoutedEventArgs e)
    {
        // Step 1: Ask user to select sounds directory
        using var folderDialog = new Forms.FolderBrowserDialog
        {
            Description = "Select Sounds Directory",
            ShowNewFolderButton = false
        };

        if (folderDialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var soundDir = folderDialog.SelectedPath;

        // Step 2: Scan for .wav and .mp3 files
        var soundFiles = Directory.GetFiles(soundDir, "*.wav", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(soundDir, "*.mp3", SearchOption.TopDirectoryOnly))
            .ToList();

        if (soundFiles.Count == 0)
        {
            WpfMessageBox.Show("No sound files found in the selected directory!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Step 3: Create dictionary mapping filename (without extension) to full path
        var soundDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var soundFile in soundFiles)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(soundFile);
            soundDict[nameWithoutExt] = soundFile;
            
            // Also handle names starting with '!' (e.g., !command.wav)
            if (nameWithoutExt.StartsWith("!"))
            {
                soundDict[nameWithoutExt.Substring(1)] = soundFile;
            }
        }

        // Step 4: Match commands with sound files and assign
        var assigned = 0;
        foreach (var command in ViewModel.Commands)
        {
            var commandName = command.Command.Trim();
            var commandWithoutBang = commandName.TrimStart('!');

            // Try to match with or without '!'
            if (soundDict.TryGetValue(commandName, out var soundPath))
            {
                command.SoundFile = soundPath;
                command.Enabled = true;  // Enable the command
                assigned++;
            }
            else if (soundDict.TryGetValue(commandWithoutBang, out soundPath))
            {
                command.SoundFile = soundPath;
                command.Enabled = true;  // Enable the command
                assigned++;
            }
        }

        // Step 5: Save commands automatically
        if (assigned > 0)
        {
            await ViewModel.SaveCommandsAsync();
            WpfMessageBox.Show($"Sound files assigned automatically!\n{assigned} commands updated and enabled.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show("No new sound files to assign.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnBrowseSoundFile(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCommand is null)
        {
            WpfMessageBox.Show("Select a command first.", "Sound", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WpfOpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            var tag = (sender as System.Windows.Controls.Button)?.Tag as string;
            if (string.Equals(tag, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SelectedCommand.SoundFile = dialog.FileName;
            }
            else
            {
                ViewModel.SelectedCommand.FkSoundFile = dialog.FileName;
            }
        }
    }

    private void OnSelectSoundDirectory(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select sound directory"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ViewModel.UpdateSoundDirectory(dialog.SelectedPath);
        }
    }

    private void OnPlaySound(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        // If sound is playing and the same button was clicked, stop the sound
        if (_isSoundPlaying && _activeSoundButton == button)
        {
            _mediaPlayer.Stop();
            ResetSoundState();
            return;
        }

        // If a different button was clicked while sound is playing, stop the current sound
        if (_isSoundPlaying && _activeSoundButton != button)
        {
            _mediaPlayer.Stop();
            ResetSoundState();
        }

        if (ViewModel.SelectedCommand is null)
        {
            WpfMessageBox.Show("Select a command first.", "Sound", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = ResolveSoundPath(ViewModel.SelectedCommand.SoundFile)
                   ?? ResolveSoundPath(ViewModel.SelectedCommand.FkSoundFile);

        if (path is null || !File.Exists(path))
        {
            WpfMessageBox.Show("Sound file not found.", "Sound", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _mediaPlayer.Stop();
        _mediaPlayer.Open(new Uri(path));

        var masterVolume = Math.Clamp(ViewModel.Volume, 0d, 1d);
        var commandVolume = ViewModel.SelectedCommand.Volume / 100d;
        _mediaPlayer.Volume = Math.Clamp(masterVolume * commandVolume, 0d, 1d);
        _mediaPlayer.Play();

        _activeSoundButton = button;
        _activeSoundButton.Content = "Stop";
        _isSoundPlaying = true;
    }

    private void OnForceStopSound(object sender, RoutedEventArgs e)
    {
        var stoppedAnySound = false;
        
        // Stop any playing sound from the UI MediaPlayer
        if (_isSoundPlaying)
        {
            _mediaPlayer.Stop();
            ResetSoundState();
            stoppedAnySound = true;
        }
        
        // Stop any playing sound from the ViewModel's sound system (Twitch-triggered sounds)
        if (ViewModel != null)
        {
            ViewModel.StopAllSounds();
            stoppedAnySound = true;
        }
        
        // Show appropriate message
        if (stoppedAnySound)
        {
            WpfMessageBox.Show("All sounds stopped.", "Sound", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show("No sound is currently playing.", "Sound", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e) => Dispatcher.Invoke(ResetSoundState);

    private void OnMediaFailed(object? sender, ExceptionEventArgs e) => Dispatcher.Invoke(ResetSoundState);

    private void ResetSoundState()
    {
        if (_isSoundPlaying)
        {
            _mediaPlayer.Stop();
        }

        _isSoundPlaying = false;
        if (_activeSoundButton is not null)
        {
            _activeSoundButton.Content = "Play";
            _activeSoundButton = null;
        }
    }



    private async void OnManageTwitchTokens(object sender, RoutedEventArgs e)
    {
        var dialog = new TwitchSecretsWindow
        {
            Owner = this
        };

        dialog.SetSecrets(ViewModel.TwitchClientId, ViewModel.TwitchAccessToken, ViewModel.TwitchRefreshToken);

        if (dialog.ShowDialog() == true)
        {
            ViewModel.TwitchClientId = dialog.ClientId;
            ViewModel.TwitchAccessToken = dialog.AccessToken;
            ViewModel.TwitchRefreshToken = dialog.RefreshToken;
            await ViewModel.SaveTwitchSecretsAsync();
            WpfMessageBox.Show("Twitch credentials saved.", "Twitch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string? ResolveSoundPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.SoundDirectory))
        {
            var combined = System.IO.Path.Combine(ViewModel.SoundDirectory, path);
            if (File.Exists(combined))
            {
                return combined;
            }
        }

        return null;
    }

    private async void OnSaveTwitchSecrets(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveTwitchSecretsAsync();
        WpfMessageBox.Show("Twitch credentials saved.", "Twitch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnAddModerator(object sender, RoutedEventArgs e)
    {
        var username = AddModeratorInput.Text.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var added = await _moderatorsService.AddManualModeratorAsync(username);
        if (added)
        {
            UpdateModeratorsDisplay();
            WpfMessageBox.Show($"Added {username} to moderators list.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show($"{username} is already in the moderators list.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        AddModeratorInput.Clear();
    }

    private async void OnRemoveModerator(object sender, RoutedEventArgs e)
    {
        if (ModeratorsList.SelectedItem is ListBoxItem item && item.Content is string displayText)
        {
            // Extract username from display text like "[Manual] username" or "[Both] username"
            var username = displayText.Split(new[] { ' ' }, 2).LastOrDefault()?.Trim().ToLower();
            
            if (string.IsNullOrEmpty(username))
                return;

            var source = _moderatorsService.GetModeratorSource(username);
            
            // Handle excluded moderators differently - allow restoration
            if (source == "Excluded")
            {
                var result = WpfMessageBox.Show(
                    $"Restore {username} as a moderator?\n\nThis will remove them from the excluded list.",
                    "Confirm Restore",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _moderatorsService.RestoreModeratorAsync(username);
                    UpdateModeratorsDisplay();
                    WpfMessageBox.Show($"Restored {username} as moderator.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            // For active moderators, exclude them
            var confirmResult = WpfMessageBox.Show(
                $"Exclude {username} from moderators list?\n\nThis will add them to the excluded list.",
                "Confirm Exclude",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult == MessageBoxResult.Yes)
            {
                // If they're manual or both, remove from manual list first
                if (source == "Manual" || source == "Both")
                {
                    await _moderatorsService.RemoveManualModeratorAsync(username);
                }
                else if (source == "API")
                {
                    // For API-only moderators, just add to excluded
                    await _moderatorsService.ExcludeModeratorAsync(username);
                }
                
                UpdateModeratorsDisplay();
                WpfMessageBox.Show($"Excluded {username} from moderators list.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void OnOpenModeratorsFile(object sender, RoutedEventArgs e)
    {
        var moderatorsPath = Path.Combine(AppContext.BaseDirectory, "data", "moderators.json");
        if (File.Exists(moderatorsPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = moderatorsPath,
                UseShellExecute = true
            });
        }
        else
        {
            WpfMessageBox.Show("moderators.json not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnRefreshModerators(object sender, RoutedEventArgs e)
    {
        try
        {
            // Fetch moderators from Twitch API
            var apiModerators = await _twitchService.GetModeratorsAsync();
            _moderatorsService.UpdateApiModerators(apiModerators);
            
            // Update the display
            UpdateModeratorsDisplay();
            
            WpfMessageBox.Show($"Fetched {apiModerators.Count} moderators from Twitch API.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to fetch moderators: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void OnModeratorsUpdated(object? sender, IReadOnlyCollection<string> moderators)
    {
        Dispatcher.Invoke(() =>
        {
            _moderatorsService.UpdateApiModerators(moderators);
            UpdateModeratorsDisplay();
        });
    }
    
    private void UpdateModeratorsDisplay()
    {
        ModeratorsList.Items.Clear();
        
        // Get all moderators including excluded ones for display
        var apiMods = _moderatorsService.GetApiModerators();
        var manualMods = _moderatorsService.GetManualModerators();
        var excludedMods = _moderatorsService.GetExcludedModerators();
        
        // Combine all unique moderators
        var allUsernames = apiMods.Union(manualMods).Union(excludedMods).Distinct().OrderBy(m => m).ToList();
        
        foreach (var mod in allUsernames)
        {
            var source = _moderatorsService.GetModeratorSource(mod);
            var displayText = source switch
            {
                "API" => $"[API] {mod}",
                "Manual" => $"[Manual] {mod}",
                "Both" => $"[Both] {mod}",
                "Excluded" => $"[Excluded] {mod}",
                _ => mod
            };
            
            var item = new ListBoxItem { Content = displayText };
            
            // Color code by source
            item.Foreground = source switch
            {
                "API" => System.Windows.Media.Brushes.Blue,
                "Manual" => System.Windows.Media.Brushes.Green,
                "Both" => System.Windows.Media.Brushes.Purple,
                "Excluded" => System.Windows.Media.Brushes.Red,
                _ => System.Windows.Media.Brushes.Black
            };
            
            ModeratorsList.Items.Add(item);
        }
    }

    private void OnCheckConnection(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsTwitchConnected)
        {
            WpfMessageBox.Show("Not connected to Twitch.", "Connection Status", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Show connection status and current stream status
        WpfMessageBox.Show($"Connected to Twitch IRC.\nCurrent stream status: {ViewModel.TwitchStreamStatus}", "Connection Check", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnCreateBackup(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Commands == null || !ViewModel.Commands.Any())
        {
            WpfMessageBox.Show("No commands to backup.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await ViewModel.CreateBackupAsync();
            OnRefreshHistory(sender, e);
            WpfMessageBox.Show("Backup created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to create backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnViewHistory(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryListItem selected)
        {
            WpfMessageBox.Show("Please select a backup to view.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Open the backup file in default JSON viewer
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = selected.Entry.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to open backup file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnApplyMaxBackups(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MaxBackupsInput.Text, out var maxBackups) && maxBackups > 0)
        {
            ViewModel.MaxBackups = maxBackups;
            WpfMessageBox.Show($"Max backups set to {maxBackups}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            WpfMessageBox.Show("Please enter a valid number greater than 0.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCurrencySettingChanged(object sender, EventArgs e)
    {
        // Trigger update of IntervalInfoText when any currency setting changes
        // Check if ViewModel is initialized (it's null during XAML loading)
        if (ViewModel != null)
        {
            ViewModel.RefreshIntervalInfo();
        }
    }

    private void OnDebugLoggingChanged(object sender, RoutedEventArgs e)
    {
        // Update log file path display is handled in the About tab view now
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        // Save configuration on app close
        if (ViewModel != null)
        {
            ViewModel.PersistConfig();
        }
        
        _mediaPlayer.Close();
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        _mediaPlayer.MediaFailed -= OnMediaFailed;
        _currencyRefreshTimer?.Stop();
    }

    private void StartCurrencyAutoRefresh()
    {
        _currencyRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _currencyRefreshTimer.Tick += async (s, e) => await RefreshCurrencyUsers();
        _currencyRefreshTimer.Start();
    }

    private async void OnRefreshCurrencyUsers(object sender, RoutedEventArgs e)
    {
        await RefreshCurrencyUsers();
    }

    private async Task RefreshCurrencyUsers()
    {
        await ViewModel.LoadCurrencyUsersAsync();
        CurrencyLastUpdateLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
    }

    private void OnAutoRefreshCurrencyChanged(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCurrencyCheckBox.IsChecked == true)
        {
            _currencyRefreshTimer?.Start();
        }
        else
        {
            _currencyRefreshTimer?.Stop();
        }
    }

    private async void OnRefreshViewers(object sender, RoutedEventArgs e)
    {
        await _twitchService.RefreshViewersAsync();
    }

    private void OnViewersIntervalChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewersUpdateIntervalComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && 
            item.Tag is string tag && 
            int.TryParse(tag, out var seconds))
        {
            _twitchService.SetViewersUpdateInterval(seconds);
        }
    }

    private sealed record HistoryListItem(HistoryEntry Entry)
    {
        public string FilePath => Entry.FilePath;
        public DateTime CreatedAtUtc => Entry.CreatedAtUtc;
        public string SizeBytes => (Entry.SizeBytes / 1024d).ToString("F2");
    }
}