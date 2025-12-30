using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

public class JsonConverterConfigReferenceOptional<T> : JsonConverter<ConfigReferenceOptional<T>> where T : Configable
{
    private readonly ConfigDatabase _database;

    public JsonConverterConfigReferenceOptional(ConfigDatabase database)
    {
        _database = database;
    }

    public override ConfigReferenceOptional<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? id = reader.GetString();
            return new ConfigReferenceOptional<T>(_database, id ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        throw new JsonException($"Expected string or null for ConfigReferenceOptional<{typeof(T).Name}>.");
    }

    public override void Write(Utf8JsonWriter writer, ConfigReferenceOptional<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Id);
    }
}

/// <summary>
/// JSON converter factory for ConfigReferenceOptional&lt;T&gt;.
/// Serializes ConfigReferenceOptional&lt;T&gt; as a string (Id).
/// </summary>
public class JsonConverterConfigReferenceOptionalFactory : JsonConverterFactory
{
    private readonly ConfigDatabase _database;

    public JsonConverterConfigReferenceOptionalFactory(ConfigDatabase database)
    {
        _database = database;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(ConfigReferenceOptional<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(JsonConverterConfigReferenceOptional<>).MakeGenericType(itemType);
        return (JsonConverter?)Activator.CreateInstance(converterType, _database);
    }

    
}

