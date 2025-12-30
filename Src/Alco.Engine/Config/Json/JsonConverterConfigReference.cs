using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

public class JsonConverterConfigReference<T> : JsonConverter<ConfigReference<T>> where T : Configable
{
    private readonly ConfigDatabase _database;

    public JsonConverterConfigReference(ConfigDatabase database)
    {
        _database = database;
    }

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

    public JsonConverterConfigReferenceFactory(ConfigDatabase database)
    {
        _database = database;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(ConfigReference<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(JsonConverterConfigReference<>).MakeGenericType(itemType);
        return (JsonConverter?)Activator.CreateInstance(converterType, _database);
    }

    
}

