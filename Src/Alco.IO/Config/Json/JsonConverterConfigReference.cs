using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.IO;

public class JsonConverterConfigReference : JsonConverter<Configable>
{
    private readonly IConfigReferenceResolver _configResolver;
    private readonly string _propertyName;
    private readonly Type _propertyType;// the real type of the property or field

    public JsonConverterConfigReference(
        string propertyName,
        Type realType,
        IConfigReferenceResolver configResolver
    )
    {
        _configResolver = configResolver;
        _propertyName = propertyName;
        _propertyType = realType;
    }

    public override Configable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The reference of config must be a string id.");
        }

        string id = reader.GetString() ?? throw new JsonException("The reference of config must be a string id.");
        if (!_configResolver.TryResolve(id, _propertyName, _propertyType, out var config))
        {
            throw new JsonException($"The config with id {id} is not found.");
        }

        return config;
    }

    public override void Write(Utf8JsonWriter writer, Configable value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Id);
    }
}
