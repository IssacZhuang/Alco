using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Represents an asset loader for shader meta files.
/// </summary>
public class AssetLoaderMeta : IAssetLoader
{
    private static readonly string[] Extensions = new string[] { FileExt.Meta };

    private readonly JsonSerializerOptions? _jsonSerializerOptions;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Meta";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderMeta()
    {

    }

    public AssetLoaderMeta(JsonSerializerOptions jsonSerializerOptions)
    {
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    public AssetLoaderMeta(IEnumerable<JsonConverter> jsonConverters)
    {
        _jsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            AllowTrailingCommas = true,
        };

        foreach (var converter in jsonConverters)
        {
            _jsonSerializerOptions.Converters.Add(converter);
        }

        _jsonSerializerOptions.MakeReadOnly();
    }

    /// <inheritdoc/>
    public bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(Meta)) || type == typeof(string);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        if (context.AssetType.IsAssignableTo(typeof(Meta)))
        {
            return CreateMeta(context);
        }
        else if (context.AssetType == typeof(string))
        {
            return CreateString(context);
        }
        throw new InvalidOperationException($"Cannot create asset of type {context.AssetType.Name}");
    }

    private Meta CreateMeta(in AssetLoadContext context)
    {
        string jsonText = Encoding.UTF8.GetString(context.Data);
        if (JsonSerializer.Deserialize(jsonText, context.AssetType, _jsonSerializerOptions) is not Meta meta)
        {
            throw new InvalidOperationException($"Failed to deserialize meta from {context.Filename}");
        }
        return meta;
    }

    private string CreateString(in AssetLoadContext context)
    {
        return Encoding.UTF8.GetString(context.Data);
    }
}