using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

public class ConfigReferenceResolver : IConfigReferenceResolver
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

    private readonly ConditionalWeakTable<BaseConfig, ConfigReference> _configReferences = new();
    private readonly ConcurrentDictionary<string, BaseConfig> _loadingConfigs = new();
    private readonly AssetSystem _assetSystem;

    public ConfigReferenceResolver(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    public bool TryResolve(string id, string propertyName, Type propertyType, [NotNullWhen(true)] out BaseConfig? config)
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

    public void AddLoadingConfig(string filename, BaseConfig config)
    {
        _loadingConfigs.TryAdd(filename, config);
    }

    public void RemoveLoadingConfig(string filename)
    {
        _loadingConfigs.TryRemove(filename, out _);
    }

    public void ResolveRealReference(BaseConfig asset, DynamicAccessor accessor, JsonTypeInfo typeInfo)
    {
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
}