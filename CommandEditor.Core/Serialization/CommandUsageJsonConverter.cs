using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Serialization;

public sealed class CommandUsageJsonConverter : JsonConverter<CommandUsage>
{
    private static readonly Dictionary<string, CommandUsage> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Streamlabs Chatbot"] = CommandUsage.StreamlabsChatbot,
        ["StreamlabsChatbot"] = CommandUsage.StreamlabsChatbot,
        ["Streamlabs"] = CommandUsage.StreamlabsChatbot,
        ["SC"] = CommandUsage.StreamlabsChatbot,
        ["Chat"] = CommandUsage.Chat,
        ["Both"] = CommandUsage.Both
    };

    public override CommandUsage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString() ?? string.Empty;
            if (TryParse(value, out var usage))
            {
                return usage;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            if (Enum.IsDefined(typeof(CommandUsage), numericValue))
            {
                return (CommandUsage)numericValue;
            }
        }

        throw new JsonException("Unsupported command usage value.");
    }

    public override void Write(Utf8JsonWriter writer, CommandUsage value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            CommandUsage.StreamlabsChatbot => "Streamlabs Chatbot",
            CommandUsage.Chat => "Chat",
            CommandUsage.Both => "Both",
            _ => value.ToString()
        });
    }

    public static bool TryParse(string value, out CommandUsage usage)
    {
        value = value.Trim();

        if (Map.TryGetValue(value, out usage))
        {
            return true;
        }

        var normalized = value.Replace(" ", string.Empty);
        if (Map.TryGetValue(normalized, out usage))
        {
            return true;
        }

        return Enum.TryParse(normalized, true, out usage);
    }
}
