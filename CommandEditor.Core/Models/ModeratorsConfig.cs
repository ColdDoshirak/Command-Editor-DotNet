using System.Collections.Generic;

namespace CommandEditor.Core.Models;

public class ModeratorsConfig
{
    public List<string> ManualModerators { get; set; } = new();
    public List<string> ExcludedModerators { get; set; } = new();
    public string Notes { get; set; } = "This file contains manual moderators and excluded moderators list. Safe to show on stream.";
}
