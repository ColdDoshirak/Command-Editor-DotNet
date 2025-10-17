namespace CommandEditor.Core.Models;

public class SoundConfig
{
    public double Volume { get; set; } = 1.0;
    public string SoundDirectory { get; set; } = string.Empty;
    public bool AllowInterruption { get; set; } = true;
    public bool ShowInterruptionMessage { get; set; }
}
