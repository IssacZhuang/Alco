using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

public class JsonConverterConfigReference : JsonConverter<Configable>
{
    private readonly ConfigReferenceResolver _configResolver;
    private readonly Type _propertyType;// the real type of the property or field

    public JsonConverterConfigReference(
        Type realType,
        ConfigReferenceResolver configResolver
    )
    {
        _configResolver = configResolver;
        _propertyType = realType;
    }

    public override Configable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            Configable configable = JsonSerializer.Deserialize<Configable>(ref reader, options)!; 
            configable.IsSubResource = true;
            return configable;
        }

        string id = reader.GetString() ?? throw new JsonException("The reference of config must be a string id.");
        if (!_configResolver.TryResolve(id, _propertyType, out var config))
        {
            throw new JsonException($"The config with id {id} is not found.");
        }

        return config;
    }

    public override void Write(Utf8JsonWriter writer, Configable value, JsonSerializerOptions options)
    {
        if (value.IsSubResource)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
        else
        {
            writer.WriteStringValue(value.Id);
        }
    }
}
