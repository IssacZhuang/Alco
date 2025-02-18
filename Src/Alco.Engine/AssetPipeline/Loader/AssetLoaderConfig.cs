using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;


public class AssetLoaderConfig : IAssetLoader, IConfigReferenceResolver
{
    private class ConfigReference
    {
        public string Id { get; }
        public string PropertyName { get; }
        public Type PropertyType { get; }

        public ConfigReference(string id, string propertyName, Type propertyType)
        {
            Id = id;
            PropertyName = propertyName;
            PropertyType = propertyType;
        }
    }

    private readonly ConcurrentDictionary<Type, DynamicAccessor> _dynamicAccessors = new();
    private readonly ConditionalWeakTable<BaseConfig, ConfigReference> _configReferences = new();
    private readonly ConcurrentDictionary<string, BaseConfig> _loadingConfigs = new();
    private readonly JsonSerializerOptions _options;
    private readonly AssetSystem _assetSystem;

    public AssetLoaderConfig(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
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
        return type.IsAssignableTo(typeof(BaseConfig));
    }

    public object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType)
    {
        BaseConfig asset = JsonSerializer.Deserialize<BaseConfig>(data, _options) ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
        _loadingConfigs.TryAdd(filename, asset);

        try
        {
            ResolveRealReference(asset);
            return asset;
        }
        catch
        {
            //rethrow the exception
            throw;
        }
        finally
        {
            _loadingConfigs.TryRemove(filename, out _);
        }
    }

    bool IConfigReferenceResolver.TryResolve(string id, string propertyName, Type propertyType, out BaseConfig config)
    {
        if (_loadingConfigs.TryGetValue(id, out var loadingConfig))
        {
            config = loadingConfig;
            return true;
        }

        //it might be loop loading if resolve the reference immediately
        //so just create a placeholder config to store the reference
        BaseConfig placeHolder = Activator.CreateInstance(propertyType) as BaseConfig ?? throw new InvalidOperationException($"Failed to create an instance of {propertyType}");
        SetReference(placeHolder, new ConfigReference(id, propertyName, propertyType));
        config = placeHolder;
        return true;
    }

    private void ResolveRealReference(BaseConfig asset)
    {
        var accessor = GetDynamicAccessor(asset.GetType());
        JsonTypeInfo typeInfo = _options.GetTypeInfo(asset.GetType());

        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType.IsAssignableTo(typeof(BaseConfig)))
            {
                ResolveConfigProperty(asset, property.Name, property.PropertyType, accessor);
            }
        }
    }

    private void ResolveConfigProperty(BaseConfig asset, string propertyName, Type propertyType, DynamicAccessor accessor)
    {
        var config = accessor.GetValue(asset, propertyName) as BaseConfig;

        if (TryGetReference(config, out var reference))
        {
            object resolvedConfig = _loadingConfigs.TryGetValue(reference.Id, out var loadingConfig)
                ? loadingConfig
                : _assetSystem.Load(reference.Id, reference.PropertyType);

            accessor.SetValue(asset, propertyName, resolvedConfig);
        }
    }

    private bool TryGetReference(BaseConfig? config, [NotNullWhen(true)] out ConfigReference? references)
    {
        if (config == null)
        {
            references = null;
            return false;
        }
        return _configReferences.TryGetValue(config, out references);
    }

    private void SetReference(BaseConfig config, ConfigReference reference)
    {
        _configReferences.AddOrUpdate(config, reference);
    }

    private DynamicAccessor GetDynamicAccessor(Type type)
    {
        return _dynamicAccessors.GetOrAdd(type, t => new DynamicAccessor(t));
    }



    public void OnAssetLoaded(object asset)
    {

    }
}
