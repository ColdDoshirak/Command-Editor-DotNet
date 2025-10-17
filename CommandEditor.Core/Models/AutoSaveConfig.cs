namespace CommandEditor.Core.Models;

public class AutoSaveConfig
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
}
