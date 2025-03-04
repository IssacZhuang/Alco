using System.Text.Json;

namespace Alco.IO;

public class BaseConfig
{
    public string Id { get; set; } = string.Empty;

    public static JsonSerializerOptions BuildJsonSerializerOptions(IConfigReferenceResolver configReferenceResolver)
    {
        var typeResolver = new ConfigJsonTypeResolver(configReferenceResolver);
        return new JsonSerializerOptions()
        {
            TypeInfoResolver = typeResolver,
            Converters = {
                new JsonConverterType(),
                new JsonConverterVector2(),
                new JsonConverterVector3(),
                new JsonConverterVector4(),
                new JsonConverterQuaternion(),
            }
        }; ;
    }
}

