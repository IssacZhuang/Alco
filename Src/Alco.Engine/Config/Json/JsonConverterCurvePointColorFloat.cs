using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco;

namespace Alco.Engine;

/// <summary>
/// JSON converter for CurvePoint{ColorFloat} type.
/// </summary>
public class JsonConverterCurvePointColorFloat : JsonConverter<CurvePoint<ColorFloat>>
{
    public override CurvePoint<ColorFloat> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        float time = 0;
        ColorFloat value = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new CurvePoint<ColorFloat>(time, value);
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propertyName = reader.GetString();
                reader.Read();

                if (propertyName == "time")
                {
                    time = reader.GetSingle();
                }
                else if (propertyName == "value")
                {
                    value = JsonSerializer.Deserialize<ColorFloat>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject token.");
    }

    public override void Write(Utf8JsonWriter writer, CurvePoint<ColorFloat> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("time", value.Time);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

