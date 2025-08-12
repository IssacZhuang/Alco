using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// JSON converter for <see cref="JsonPointer"/>.
/// Serializes a <see cref="JsonPointer"/> as its pointer string (e.g. "/store/book/0").
/// Deserializes from a JSON string into a <see cref="JsonPointer"/> instance.
/// </summary>
public class JsonConverterJsonPointer : JsonConverter<JsonPointer>
{
    /// <summary>
    /// Reads a JSON string and converts it to a <see cref="JsonPointer"/>.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (<see cref="JsonPointer"/>).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A <see cref="JsonPointer"/> instance parsed from the string, or null when JSON token is null.</returns>
    /// <exception cref="JsonException">Thrown when the JSON token is not a string or null.</exception>
    public override JsonPointer? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new JsonPointer(string.Empty);
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value for JsonPointer");
        }

        string? pointer = reader.GetString();
        // Empty string is valid and denotes the root per RFC6901 semantics used by our implementation
        return new JsonPointer(pointer ?? string.Empty);
    }

    /// <summary>
    /// Writes a <see cref="JsonPointer"/> value as a JSON string.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The <see cref="JsonPointer"/> value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, JsonPointer value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}

