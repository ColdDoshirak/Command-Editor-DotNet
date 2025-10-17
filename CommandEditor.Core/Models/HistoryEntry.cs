namespace CommandEditor.Core.Models;

public class HistoryEntry
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long SizeBytes { get; set; }
}
