using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco;

namespace Alco.Engine;

/// <summary>
/// JSON converter for CurvePoint{Color32} type.
/// </summary>
public class JsonConverterCurvePointColor32 : JsonConverter<CurvePoint<Color32>>
{
    public override CurvePoint<Color32> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        float time = 0;
        Color32 value = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new CurvePoint<Color32>(time, value);
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
                    value = JsonSerializer.Deserialize<Color32>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject token.");
    }

    public override void Write(Utf8JsonWriter writer, CurvePoint<Color32> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("time", value.Time);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

