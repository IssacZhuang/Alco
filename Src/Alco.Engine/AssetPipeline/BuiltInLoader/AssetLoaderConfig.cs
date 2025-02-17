
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    private static readonly ConditionalWeakTable<IConfig, ConfigReference> _configReferences = new();
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
        //resolve the reference after the asset is loaded
    }

    bool IConfigReferenceResolver.TryResolve(string id, string propertyName, Type propertyType, out IConfig? config)
    {
        //it might be loop loading if resolve the reference immediately
        //so just create a placeholder config to store the reference
        IConfig placeHolder = Activator.CreateInstance(propertyType) as IConfig ?? throw new InvalidOperationException($"Failed to create an instance of {propertyType}");
        SetReference(placeHolder, new ConfigReference(id, propertyName, propertyType));
        config = placeHolder;
        return true;
    }

    private static bool TryGetReference(IConfig config, [NotNullWhen(true)] out ConfigReference? references)
    {
        return _configReferences.TryGetValue(config, out references);
    }

    private static void SetReference(IConfig config, ConfigReference reference)
    {
        _configReferences.AddOrUpdate(config, reference);
    }
}
