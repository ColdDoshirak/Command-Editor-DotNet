namespace CommandEditor.Core.Models;

public class RankDefinition
{
    public string Name { get; set; } = string.Empty;
    public double Requirement { get; set; }
    public UserRankType Type { get; set; } = UserRankType.Points;
}
