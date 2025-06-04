using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Color32 type.
/// Supports two formats:
/// 1. Hex string format: "#FF0000" or "#FF0000FF"
/// 2. Object format: {"r": 255, "g": 0, "b": 0, "a": 255}
/// </summary>
public unsafe class JsonConverterColor32 : BaseJsonConverterVector<Color32>
{
    /// <summary>
    /// Reads a JSON value and converts it to a Color32.
    /// Supports both hex string format and object format.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Color32).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Color32 instance containing the deserialized color values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected formats.</exception>
    public override Color32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? hex = reader.GetString();
            if (hex != null && Color32.TryParse(hex, out Color32 color))
            {
                return color;
            }
            throw new JsonException("Invalid hex color string");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading Color32");
        }

        byte r = 0, g = 0, b = 0, a = 255;
        reader.Read();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading Color32");
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected number value for property '{propertyName}' when reading Color32");
            }

            byte value = reader.GetByte();

            switch (propertyName)
            {
                case "r":
                    r = value;
                    break;
                case "g":
                    g = value;
                    break;
                case "b":
                    b = value;
                    break;
                case "a":
                    a = value;
                    break;
                default:
                    throw new JsonException($"Unknown property '{propertyName}' when reading Color32");
            }

            reader.Read();
        }

        return new Color32(r, g, b, a);
    }

    /// <summary>
    /// Writes a Color32 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Color32 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Color32 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("r", value.R);
        writer.WriteNumber("g", value.G);
        writer.WriteNumber("b", value.B);
        writer.WriteNumber("a", value.A);
        writer.WriteEndObject();
    }
}
