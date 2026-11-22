using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Asset loader for material definition files (<c>.amat</c>) into <see cref="MaterialAsset"/> instances.
/// </summary>
public class AssetLoaderMaterialAsset : BaseAssetLoader<MaterialAsset>
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Initializes a new instance of the <see cref="AssetLoaderMaterialAsset"/> class.</summary>
    /// <param name="assetSystem">The asset system used to load referenced texture assets.</param>
    /// <param name="typeInfoResolver">Optional JSON type metadata resolver; reflection-based when null.</param>
    public AssetLoaderMaterialAsset(AssetSystem assetSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        _options = CreateJsonOptions(assetSystem, typeInfoResolver);
    }

    /// <inheritdoc/>
    public override string Name => "MaterialAsset(.amat)";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => [FileExt.Material];

    /// <inheritdoc/>
    public override bool CanHandleType(Type type) => type.IsAssignableTo(typeof(MaterialAsset));

    /// <summary>Creates the serializer options used to parse <c>.amat</c> material files.</summary>
    /// <param name="assetSystem">The asset system used to resolve texture slots.</param>
    /// <param name="typeInfoResolver">Optional JSON type metadata resolver.</param>
    /// <returns>The configured read-only serializer options.</returns>
    public static JsonSerializerOptions CreateJsonOptions(AssetSystem assetSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(MaterialAsset)], typeInfoResolver),
        };
        options.Converters.Add(new JsonConverterMaterialVector3());
        options.Converters.Add(new JsonConverterMaterialVector4());
        options.Converters.Add(new JsonConverterShaderValue());
        options.Converters.Add(new JsonConverterImage(assetSystem));
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        MaterialAsset asset;
        try
        {
            ReadOnlySpan<byte> data = context.GetData();
            if (data.StartsWith([(byte)0xEF, (byte)0xBB, (byte)0xBF]))
                data = data[3..];
            asset = JsonSerializer.Deserialize<MaterialAsset>(data, _options)
                ?? throw new InvalidDataException("The file is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Material asset '{context.Filename}' is invalid: {exception.Message}", exception);
        }

        AssetJson.ValidateVersion(asset.Version, MaterialAsset.FormatVersion, "Material asset", context.Filename);

        if (!context.AssetType.IsInstanceOfType(asset))
        {
            throw new InvalidDataException(
                $"Material asset '{context.Filename}' is a {asset.GetType().Name} and cannot be loaded as {context.AssetType.Name}.");
        }

        asset.Name = string.IsNullOrWhiteSpace(asset.Name)
            ? Path.GetFileNameWithoutExtension(context.Filename)
            : asset.Name.Trim();
        asset.SourceFile = context.Filename;
        return asset;
    }
}
