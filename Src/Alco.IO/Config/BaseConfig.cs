using System.Text.Json;

namespace Alco.IO;

public class BaseConfig
{
    public string Id { get; set; } = string.Empty;

    public static JsonSerializerOptions BuiltJsonSerializerOptions(JsonSerializerOptions? baseOptions, IConfigReferenceResolver configReferenceResolver)
    {
        JsonSerializerOptions options = baseOptions ?? new JsonSerializerOptions();

        var typeResolver = new ConfigJsonTypeResolver(configReferenceResolver);
        options = new JsonSerializerOptions()
        {
            TypeInfoResolver = typeResolver,
            Converters = {
                new JsonConverterType(),
                new JsonConverterVector2(),
                new JsonConverterVector3(),
                new JsonConverterVector4(),
                new JsonConverterQuaternion(),
            }
        };

        return options;
    }
}

