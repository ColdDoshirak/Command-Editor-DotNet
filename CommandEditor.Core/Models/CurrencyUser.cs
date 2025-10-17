using System;
using System.Text.Json.Serialization;

namespace CommandEditor.Core.Models;

public class CurrencyUser
{
    public string Username { get; set; } = string.Empty;

    public double Points { get; set; }

    public double Hours { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UserRankType RankType { get; set; } = UserRankType.Points;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    
    public DateTime LastPayoutUtc { get; set; } = DateTime.UtcNow;
}
