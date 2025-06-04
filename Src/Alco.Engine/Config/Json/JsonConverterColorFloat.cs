using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// JSON converter for ColorFloat type.
/// Supports two formats:
/// 1. Hex string format: "#FF0000" or "#FF0000FF"
/// 2. Object format: {"r": 1.0, "g": 0.0, "b": 0.0, "a": 1.0}
/// </summary>
public unsafe class JsonConverterColorFloat : BaseJsonConverterVector<ColorFloat>
{
    private static readonly string[] ComponentNames = { "r", "g", "b", "a" };

    /// <summary>
    /// Reads a JSON value and converts it to a ColorFloat.
    /// Supports both hex string format and object format.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (ColorFloat).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A ColorFloat instance containing the deserialized color values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected formats.</exception>
    public override ColorFloat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? hex = reader.GetString();
            if (hex != null && ColorFloat.TryParse(hex, out ColorFloat color))
            {
                return color;
            }
            throw new JsonException("Invalid hex color string");
        }

        float* array = stackalloc float[4]
        {
            0.0f,
            0.0f,
            0.0f,
            1.0f
        };
        ReadFloatObject(ref reader, array, ComponentNames, false);
        return new ColorFloat(array[0], array[1], array[2], array[3]);
    }


    /// <summary>
    /// Writes a ColorFloat value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The ColorFloat value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, ColorFloat value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatObject(writer, ptr, ComponentNames);
    }
}
