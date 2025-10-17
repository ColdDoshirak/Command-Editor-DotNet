using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandEditor.Core.Serialization;

public sealed class CommandVolumeJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var intValue))
            {
                return Clamp(intValue);
            }

            if (reader.TryGetDouble(out var doubleValue))
            {
                return FromDouble(doubleValue);
            }
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return FromDouble(doubleValue);
            }
        }

        return 100;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(Clamp(value));
    }

    private static int FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 100;
        }

        if (value >= 0 && value <= 1)
        {
            return Clamp((int)Math.Round(value * 100d, MidpointRounding.AwayFromZero));
        }

        return Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
