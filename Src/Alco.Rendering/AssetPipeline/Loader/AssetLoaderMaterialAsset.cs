using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Loads material asset files (<c>.amat</c>) directly into <see cref="MaterialAsset"/> instances with typed resource references and load-time validation.
/// </summary>
public class AssetLoaderMaterialAsset : BaseAssetLoader<MaterialAsset>
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoaderMaterialAsset"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to load referenced textures.</param>
    /// <param name="shaderSystem">The shader system used to resolve shader libraries.</param>
    /// <param name="typeInfoResolver">The optional JSON type metadata resolver; reflection-based when null.</param>
    public AssetLoaderMaterialAsset(AssetSystem assetSystem, ShaderSystem shaderSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        _options = CreateJsonOptions(assetSystem, shaderSystem, typeInfoResolver);
    }

    /// <inheritdoc />
    public override string Name => "MaterialAsset(.amat)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [FileExt.Material];

    /// <summary>
    /// Material files are polymorphic: the concrete family type is picked by the file's
    /// <c>$type</c> discriminator, so any type assignable to <see cref="MaterialAsset"/>
    /// is a valid load request, not just the base type itself.
    /// </summary>
    /// <param name="type">The type of the asset.</param>
    /// <returns>True if the type is <see cref="MaterialAsset"/> or one of its family types.</returns>
    public override bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(MaterialAsset));
    }

    /// <summary>
    /// The serializer options used to parse material asset files, exposed for tests and tooling.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(AssetSystem assetSystem, ShaderSystem shaderSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
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
        options.Converters.Add(new JsonConverterMaterialTexture(assetSystem));
        options.Converters.Add(new JsonConverterShaderLibrary(shaderSystem));
        options.Converters.Add(new JsonConverterBlendState());
        options.Converters.Add(new JsonConverterDepthStencilState());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        MaterialAsset asset;
        try
        {
            asset = JsonSerializer.Deserialize<MaterialAsset>(context.GetData(), _options)
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
        // The roundtrippable reference path for assets embedding this material (see
        // MaterialAsset.SourceFile): the resolved entry path, e.g. "Materials/Water.amat".
        asset.SourceFile = context.Filename;
        return asset;
    }
}
