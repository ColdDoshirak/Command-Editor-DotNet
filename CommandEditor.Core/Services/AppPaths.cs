using System.IO;

namespace CommandEditor.Core.Services;

public class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        BackupDirectory = Path.Combine(RootDirectory, "backups");
        HistoryDirectory = Path.Combine(RootDirectory, "command_history");
        DataDirectory = Path.Combine(RootDirectory, "data");
        CommandsFile = Path.Combine(RootDirectory, "commands.json");
        ConfigFile = Path.Combine(RootDirectory, "config.json");
        TwitchConfigFile = Path.Combine(RootDirectory, "twitch_config.json");
        ModeratorsFile = Path.Combine(RootDirectory, "moderators.json");
        CurrencyUsersFile = Path.Combine(RootDirectory, "users_currency.json");
        CurrencySettingsFile = Path.Combine(DataDirectory, "currency_settings.json");
        RanksFile = Path.Combine(RootDirectory, "ranks.json");
    }

    public string RootDirectory { get; }
    public string BackupDirectory { get; }
    public string HistoryDirectory { get; }
    public string DataDirectory { get; }
    public string CommandsFile { get; }
    public string ConfigFile { get; }
    public string TwitchConfigFile { get; }
    public string ModeratorsFile { get; }
    public string CurrencyUsersFile { get; }
    public string CurrencySettingsFile { get; }
    public string RanksFile { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(DataDirectory);
    }
}
