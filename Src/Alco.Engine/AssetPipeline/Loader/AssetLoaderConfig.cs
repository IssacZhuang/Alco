using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

public class AssetLoaderConfig : IAssetLoader
{
    private readonly JsonSerializerOptions _options;

    private readonly ConfigReferenceResolver _configReferenceResolver;

    public AssetLoaderConfig(JsonSerializerOptions options, ConfigReferenceResolver configReferenceResolver)
    {
        _options = options;
        _configReferenceResolver = configReferenceResolver;
    }

    public void AddJsonConverter(JsonConverter converter)
    {
        _options.Converters.Add(converter);
    }

    public string Name => "AssetLoader.Config";

    public IReadOnlyList<string> FileExtensions => [".json"];

    public bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(Configable));
    }

    public object CreateAsset(in AssetLoadContext context)
    {
        Configable asset = JsonSerializer.Deserialize<Configable>(context.Data, _options) ?? throw new InvalidOperationException($"Failed to deserialize {context.Filename}");
        asset.Id = context.Filename;
        _configReferenceResolver.AddLoadingConfig(context.Filename, asset);

        try
        {
            _configReferenceResolver.ResolveReferenceFor(asset);
            return asset;
        }
        catch
        {
            //rethrow the exception
            throw;
        }
        finally
        {
            _configReferenceResolver.RemoveLoadingConfig(context.Filename);
        }
    }

    public void OnAssetLoaded(object asset)
    {
    }
}

