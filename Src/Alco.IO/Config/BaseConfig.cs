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
                new JsonConverterHalf2(),
                new JsonConverterHalf3(),
                new JsonConverterHalf4(),
                new JsonConverterInt2(),
                new JsonConverterInt3(),
                new JsonConverterInt4(),
                new JsonConverterUInt2(),
                new JsonConverterUInt3(),
                new JsonConverterUInt4(),
                new JsonConverterQuaternion(),
            }
        }; ;
    }
}

