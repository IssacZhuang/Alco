using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// JSON converter for CurvePoint{Vector3} type.
/// </summary>
public class JsonConverterCurvePointVector3 : JsonConverter<CurvePoint<Vector3>>
{
    public override CurvePoint<Vector3> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        float time = 0;
        Vector3 value = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new CurvePoint<Vector3>(time, value);
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
                    value = JsonSerializer.Deserialize<Vector3>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject token.");
    }

    public override void Write(Utf8JsonWriter writer, CurvePoint<Vector3> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("time", value.Time);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

