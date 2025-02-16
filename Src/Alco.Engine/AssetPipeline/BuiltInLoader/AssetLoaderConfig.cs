
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using Alco.IO;

namespace Alco.Engine;


public class AssetLoaderConfig : IAssetLoader, IConfigReferenceResolver
{

    private readonly JsonSerializerOptions _options;

    public AssetLoaderConfig()
    {
        var typeResolver = new ConfigJsonTypeResolver(this);
        _options = new JsonSerializerOptions()
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
    }

    public void AddJsonConverter(JsonConverter converter)
    {
        _options.Converters.Add(converter);
    }

    public string Name => "AssetLoader.Config";

    public IReadOnlyList<string> FileExtensions => new[] { ".json" };

    public bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(IConfig));
    }

    public object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType)
    {
        throw new NotImplementedException();
    }

    public void OnAssetLoaded(object asset)
    {

    }

    bool IConfigReferenceResolver.TryResolve(string id, string propertyName, Type propertyType, out IConfig? config)
    {
        throw new NotImplementedException();
    }
}
