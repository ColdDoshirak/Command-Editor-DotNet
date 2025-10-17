namespace CommandEditor.Core.Models;

public class CurrencySettings
{
    public bool AccumulationEnabled { get; set; } = true;
    public bool ShowServiceMessages { get; set; }
    public string Command { get; set; } = "!points";
    public string Name { get; set; } = "Points";
    public string Response { get; set; } = "$username [$rank] - Hours: $hours - $currencyname: $points";
    public int CooldownSeconds { get; set; } = 5;
    public UserRankType RankType { get; set; } = UserRankType.Points;
    public bool TrackOfflineHours { get; set; }
    public bool AutoRegular { get; set; }
    public double AutoRegularAmount { get; set; } = 100;
    public UserRankType AutoRegularType { get; set; } = UserRankType.Points;
    public int OnlineIntervalMinutes { get; set; } = 5;
    public int OfflineIntervalMinutes { get; set; } = 15;
    public double LivePayout { get; set; } = 1;
    public double OfflinePayout { get; set; }
    public double RegularBonus { get; set; }
    public double SubBonus { get; set; }
    public double ModBonus { get; set; }
    public double ActiveBonus { get; set; } = 1;
    public string PayoutMode { get; set; } = "per_minute";
    public double RaidAward { get; set; } = 10;
    public double FollowAward { get; set; } = 10;
    public double SubAward { get; set; } = 10;
    public double MassSubAward { get; set; }
    public double HostAward { get; set; }
}
