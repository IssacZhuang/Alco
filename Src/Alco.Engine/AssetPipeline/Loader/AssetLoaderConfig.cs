using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

public class AssetLoaderConfig : IAssetLoader
{
    private readonly ConcurrentDictionary<Type, DynamicAccessor> _dynamicAccessors = new();
    private readonly JsonSerializerOptions _options;

    private readonly ConfigReferenceResolver _configReferenceResolver;

    public AssetLoaderConfig(JsonSerializerOptions options, ConfigReferenceResolver configReferenceResolver    )
    {
        _options = options;
        _configReferenceResolver = configReferenceResolver;
    }

    public void AddJsonConverter(JsonConverter converter)
    {
        _options.Converters.Add(converter);
    }

    public string Name => "AssetLoader.Config";

    public IReadOnlyList<string> FileExtensions => new[] { ".json" };

    public bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(BaseConfig));
    }

    public object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType)
    {
        BaseConfig asset = JsonSerializer.Deserialize<BaseConfig>(data, _options) ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
        _configReferenceResolver.AddLoadingConfig(filename, asset);

        try
        {
            var accessor = GetDynamicAccessor(asset.GetType());
            JsonTypeInfo typeInfo = _options.GetTypeInfo(asset.GetType());
            _configReferenceResolver.ResolveRealReference(asset, accessor, typeInfo);
            return asset;
        }
        catch
        {
            //rethrow the exception
            throw;
        }
        finally
        {
            _configReferenceResolver.RemoveLoadingConfig(filename);
        }
    }

    private DynamicAccessor GetDynamicAccessor(Type type)
    {
        return _dynamicAccessors.GetOrAdd(type, t => new DynamicAccessor(t));
    }

    public void OnAssetLoaded(object asset)
    {
    }
}

