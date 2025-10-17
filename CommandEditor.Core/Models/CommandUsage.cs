using System.Text.Json.Serialization;
using CommandEditor.Core.Serialization;

namespace CommandEditor.Core.Models;

[JsonConverter(typeof(CommandUsageJsonConverter))]
public enum CommandUsage
{
    StreamlabsChatbot,
    Chat,
    Both
}
