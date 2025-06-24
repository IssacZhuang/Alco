using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// JSON converter for System.Type.
/// Serializes Type as a string representation of its full name.
/// </summary>
public class JsonConverterType : JsonConverter<Type>
{
    private readonly TypeHelper _typeHelper;

    public JsonConverterType()
    {
        _typeHelper = TypeHelper.Default;
    }

    public JsonConverterType(TypeHelper typeHelper)
    {
        _typeHelper = typeHelper;
    }

    public JsonConverterType(params ReadOnlySpan<Assembly> assemblies) : this()

    {
        _typeHelper = new TypeHelper(assemblies);
    }

    /// <summary>
    /// Reads a JSON string and converts it to a Type.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Type).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Type instance based on the type name string.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or the type cannot be found.</exception>
    public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value for Type");
        }

        string? typeName = reader.GetString();

        if (string.IsNullOrEmpty(typeName))
        {
            throw new JsonException("Expected string value for Type");
        }

        Type? type = _typeHelper.FindType(typeName);

        if (type == null && !string.IsNullOrEmpty(typeName))
        {
            throw new JsonException($"Type '{typeName}' could not be found in any loaded assemblies");
        }

        return type;
    }

    /// <summary>
    /// Writes a Type value as a JSON string.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Type value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.FullName ?? value.Name);
    }
}