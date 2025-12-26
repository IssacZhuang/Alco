using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// JSON converter for CurvePoint{Vector2} type.
/// </summary>
public class JsonConverterCurvePointVector2 : JsonConverter<CurvePoint<Vector2>>
{
    public override CurvePoint<Vector2> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        float time = 0;
        Vector2 value = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new CurvePoint<Vector2>(time, value);
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
                    value = JsonSerializer.Deserialize<Vector2>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject token.");
    }

    public override void Write(Utf8JsonWriter writer, CurvePoint<Vector2> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("time", value.Time);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

