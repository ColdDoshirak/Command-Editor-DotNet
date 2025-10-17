using System;
using System.Windows;
using CommandEditor.App.ViewModels;
using CommandEditor.Core.Services;

namespace CommandEditor.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths(AppContext.BaseDirectory);
        var configService = new ConfigService(paths);
        var logService = new LogService(paths.DataDirectory);
        var soundPlaybackService = new SoundPlaybackService();
        var commandStorage = new CommandStorageService(paths);
        var historyService = new HistoryService(paths);
        var twitchService = new TwitchService();
        var currencyService = new CurrencyService(paths);
        currencyService.SetTwitchService(twitchService); // Set the Twitch service to enable payout system
        var moderatorsService = new ModeratorsService(paths.DataDirectory);
        var commandExecutionService = new CommandExecutionService(twitchService, currencyService, soundPlaybackService);

        var viewModel = new MainViewModel(commandStorage, configService, historyService, currencyService, twitchService, commandExecutionService);
        viewModel.SetModeratorsService(moderatorsService);
        viewModel.SetLogService(logService);
        viewModel.SetSoundPlaybackService(soundPlaybackService);
        await viewModel.InitializeAsync();

        var window = new MainWindow(moderatorsService, twitchService) { DataContext = viewModel };
        window.Show();
    }
}

