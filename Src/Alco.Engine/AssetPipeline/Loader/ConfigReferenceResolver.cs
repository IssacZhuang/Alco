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

    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    private readonly ConditionalWeakTable<Configable, ConfigReference> _configReferences = new();
    private readonly ConcurrentDictionary<string, Configable> _loadingConfigs = new();
    private readonly AssetSystem _assetSystem;

    public ConfigReferenceResolver(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    public bool TryResolve(string id, string propertyName, Type propertyType, [NotNullWhen(true)] out Configable? config)
    {
        if (_loadingConfigs.TryGetValue(id, out var loadingConfig))
        {
            config = loadingConfig;
            return true;
        }

        //it might be loop loading if resolve the reference immediately
        //so just create a placeholder config to store the reference
        Configable placeHolder = Activator.CreateInstance(propertyType) as Configable ?? throw new InvalidOperationException($"Failed to create an instance of {propertyType}");
        SetReference(placeHolder, new ConfigReference(id, propertyName, propertyType));
        config = placeHolder;
        return true;
    }

    public void AddLoadingConfig(string filename, Configable config)
    {
        _loadingConfigs.TryAdd(filename, config);
    }

    public void RemoveLoadingConfig(string filename)
    {
        _loadingConfigs.TryRemove(filename, out _);
    }

    public void ResolveReferenceFor(Configable asset)
    {
        AccessTypeInfo accessTypeInfo = GetAccessTypeInfo(asset.GetType());
        foreach (var accessMember in accessTypeInfo.Members)
        {
            if (accessMember.MemberType.IsAssignableTo(typeof(Configable)))
            {
                ResolveConfigProperty(asset, accessMember.Name, accessMember);
            }
        }
    }

    private void ResolveConfigProperty(Configable asset, string propertyName, AccessMemberInfo accessMember)
    {
        var config = accessMember.GetValue<Configable>(asset);

        if (TryGetReference(config, out var reference))
        {
            object resolvedConfig = _loadingConfigs.TryGetValue(reference.Id, out var loadingConfig)
                ? loadingConfig
                : _assetSystem.Load(reference.Id, reference.PropertyType);

            accessMember.SetValue(asset, resolvedConfig);
        }
    }

    private bool TryGetReference(Configable? config, [NotNullWhen(true)] out ConfigReference? references)
    {
        if (config == null)
        {
            references = null;
            return false;
        }
        return _configReferences.TryGetValue(config, out references);
    }

    private void SetReference(Configable config, ConfigReference reference)
    {
        _configReferences.AddOrUpdate(config, reference);
    }

    private AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }
}