using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco;

namespace Alco.Engine;

/// <summary>
/// JSON converter for generic CurvePoint{T} type.
/// </summary>
/// <typeparam name="T">The type of the value in the curve point.</typeparam>
public class JsonConverterCurvePoint<T> : JsonConverter<CurvePoint<T>> where T : struct
{
    public override CurvePoint<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        float time = 0;
        T value = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                // If value is null and T is non-nullable value type, this might be issue if it wasn't populated.
                // But default(T) is fine for structs.
                return new CurvePoint<T>(time, value);
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propertyName = reader.GetString();
                reader.Read();

                if (string.Equals(propertyName, nameof(CurvePoint<>.Time), StringComparison.OrdinalIgnoreCase))
                {
                    time = reader.GetSingle();
                }
                else if (string.Equals(propertyName, nameof(CurvePoint<>.Value), StringComparison.OrdinalIgnoreCase))
                {
                    value = JsonSerializer.Deserialize<T>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject token.");
    }

    public override void Write(Utf8JsonWriter writer, CurvePoint<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(CurvePoint<>.Time), value.Time);
        writer.WritePropertyName(nameof(CurvePoint<>.Value));
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter factory for CurvePoint{T}.
/// </summary>
public class JsonConverterCurvePointFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(CurvePoint<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(JsonConverterCurvePoint<>).MakeGenericType(itemType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
