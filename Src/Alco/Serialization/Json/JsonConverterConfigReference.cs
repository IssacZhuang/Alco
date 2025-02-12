using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco;

public class JsonConverterConfigReference : JsonConverter<IConfig>
{
    private readonly IConfigReferenceResolver _configResolver;

    public JsonConverterConfigReference(IConfigReferenceResolver configResolver)
    {
        _configResolver = configResolver;
    }

    public override IConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The reference of config must be a string id.");
        }

        string id = reader.GetString() ?? throw new JsonException("The reference of config must be a string id.");
        if (!_configResolver.TryResolve(id, out var config))
        {
            throw new JsonException($"The config with id {id} is not found.");
        }

        return config;
    }

    public override void Write(Utf8JsonWriter writer, IConfig value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Id);
    }
}
