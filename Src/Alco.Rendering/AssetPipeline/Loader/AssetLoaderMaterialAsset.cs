using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Loads material asset files (<c>.amat</c>) directly into <see cref="MaterialAsset"/>s —
/// no DTO layer: resource references land typed — textures load through the asset system,
/// the surface resolves and validates into a <see cref="ShaderLibrary"/>, parameters and
/// PBR factors read as vectors/colors. A bad reference (missing texture, unknown module,
/// unknown field, unknown <c>$type</c>) fails here, at load time, with the file's name.
/// </summary>
public class AssetLoaderMaterialAsset : BaseAssetLoader<MaterialAsset>
{
    private readonly JsonSerializerOptions _options;

    public AssetLoaderMaterialAsset(AssetSystem assetSystem, ShaderSystem shaderSystem)
    {
        _options = CreateJsonOptions(assetSystem, shaderSystem);
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
    /// The serializer options material asset files parse with: author-friendly (camelCase,
    /// comments and trailing commas tolerated), strict about unmapped members, polymorphic
    /// over <see cref="MaterialAsset"/>, with the material converters (textures, shader
    /// libraries, vectors/colors, enums). Exposed for tests and tooling that parse
    /// material files outside the asset system.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(AssetSystem assetSystem, ShaderSystem shaderSystem)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(MaterialAsset)]),
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
        return asset;
    }
}
