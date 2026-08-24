using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Loads material asset files (<c>.amat</c>) directly into <see cref="MaterialAsset"/>s —
/// no DTO layer: the file's <c>$type</c> discriminator selects the pipeline family's
/// derived asset type (the engine's polymorphic JSON convention, discovered by assembly
/// scan; a file without one parses as the pipeline-agnostic base type), and resource
/// references land typed — textures load through the asset system, the surface resolves
/// and validates into a <see cref="ShaderLibrary"/>, parameters and PBR factors read as
/// vectors/colors. A bad reference (missing texture, unknown module, unknown field,
/// unknown <c>$type</c>) fails here, at load time, with the file's name.
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
        options.Converters.Add(new JsonConverterMaterialTexture(assetSystem));
        options.Converters.Add(new JsonConverterShaderLibrary(shaderSystem));
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
        asset.Name = string.IsNullOrWhiteSpace(asset.Name)
            ? Path.GetFileNameWithoutExtension(context.Filename)
            : asset.Name.Trim();
        return asset;
    }
}
