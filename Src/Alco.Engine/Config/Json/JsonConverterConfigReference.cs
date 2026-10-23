using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Serializes a <see cref="ConfigReference{T}"/> as its config Id string.
/// </summary>
/// <typeparam name="T">The referenced config type.</typeparam>
public class JsonConverterConfigReference<T> : JsonConverter<ConfigReference<T>> where T : Configable
{
    private readonly ConfigDatabase _database;

    /// <summary>
    /// Initializes a new instance of <see cref="JsonConverterConfigReference{T}"/>.
    /// </summary>
    /// <param name="database">Database used to resolve referenced configs.</param>
    public JsonConverterConfigReference(ConfigDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Reads a config reference from its Id string.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The deserialized reference.</returns>
    public override ConfigReference<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? id = reader.GetString();
            if (id == null)
            {
                throw new JsonException("ConfigReference Id cannot be null.");
            }
            return new ConfigReference<T>(_database, id);
        }

        throw new JsonException($"Expected string for ConfigReference<{typeof(T).Name}>.");
    }

    /// <summary>
    /// Writes a config reference as its Id string.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The reference to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, ConfigReference<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Id);
    }
}

/// <summary>
/// JSON converter factory for ConfigReference&lt;T&gt;.
/// Serializes ConfigReference&lt;T&gt; as a string (Id).
/// </summary>
public class JsonConverterConfigReferenceFactory : JsonConverterFactory
{
    private readonly ConfigDatabase _database;

    /// <summary>
    /// Initializes a new instance of <see cref="JsonConverterConfigReferenceFactory"/>.
    /// </summary>
    /// <param name="database">Database used to resolve referenced configs.</param>
    public JsonConverterConfigReferenceFactory(ConfigDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Determines whether the type is a <see cref="ConfigReference{T}"/>.
    /// </summary>
    /// <param name="typeToConvert">The type to check.</param>
    /// <returns>True if the type is supported; otherwise false.</returns>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(ConfigReference<>);
    }

    /// <summary>
    /// Creates the converter for the concrete reference type.
    /// </summary>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The converter instance, or null if none was created.</returns>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(JsonConverterConfigReference<>).MakeGenericType(itemType);
        return (JsonConverter?)Activator.CreateInstance(converterType, _database);
    }

    
}

