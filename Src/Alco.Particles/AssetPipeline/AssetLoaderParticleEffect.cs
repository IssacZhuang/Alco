using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// Loads particle effect asset files (<c>.apeff</c>) directly into
/// <see cref="ParticleEffectAsset"/>s — no DTO layer: texture references load
/// through the asset system, behavior module references resolve and validate into
/// <see cref="ShaderLibrary"/>s at load time, so a bad reference (missing texture,
/// unknown module, unknown field, unknown <c>$type</c>) fails here, at load time,
/// with the file's name.
/// </summary>
public class AssetLoaderParticleEffect : BaseAssetLoader<ParticleEffectAsset>
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Creates the loader with the serializer options of <see cref="CreateJsonOptions"/>.</summary>
    public AssetLoaderParticleEffect(AssetSystem assetSystem, ShaderSystem shaderSystem)
    {
        _options = CreateJsonOptions(assetSystem, shaderSystem);
    }

    /// <inheritdoc />
    public override string Name => "ParticleEffect(.apeff)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [ParticleAssetPipeline.EffectExtension];

    /// <summary>
    /// Particle effect files are polymorphic: the concrete effect type is picked by
    /// the file's <c>$type</c> discriminator, so any type assignable to
    /// <see cref="ParticleEffectAsset"/> is a valid load request.
    /// </summary>
    /// <param name="type">The type of the asset.</param>
    /// <returns>True if the type is <see cref="ParticleEffectAsset"/> or one of its family types.</returns>
    public override bool CanHandleType(Type type)
    {
        return type.IsAssignableTo(typeof(ParticleEffectAsset));
    }

    /// <summary>
    /// The serializer options particle effect files parse with: author-friendly
    /// (camelCase, comments and trailing commas tolerated), strict about unmapped
    /// members, polymorphic over <see cref="ParticleEffectAsset"/>, with the vector/
    /// color, range, texture, shader(-library) and blend-state converters. Exposed
    /// for tests and tooling that parse effect files outside the asset system.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(AssetSystem assetSystem, ShaderSystem shaderSystem)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(ParticleEffectAsset)]),
        };
        options.Converters.Add(new JsonConverterMaterialVector3());
        options.Converters.Add(new JsonConverterMaterialVector4());
        options.Converters.Add(new JsonConverterParticleVector2());
        options.Converters.Add(new JsonConverterParticleRange());
        options.Converters.Add(new JsonConverterTexture2D(assetSystem));
        options.Converters.Add(new JsonConverterShader(shaderSystem));
        options.Converters.Add(new JsonConverterShaderLibrary(shaderSystem));
        options.Converters.Add(new JsonConverterBlendState());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        ParticleEffectAsset asset;
        try
        {
            asset = JsonSerializer.Deserialize<ParticleEffectAsset>(context.GetData(), _options)
                ?? throw new InvalidDataException("The file is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Particle effect asset '{context.Filename}' is invalid: {exception.Message}", exception);
        }

        AssetJson.ValidateVersion(asset.Version, ParticleEffectAsset.FormatVersion, "Particle effect asset", context.Filename);

        if (!context.AssetType.IsInstanceOfType(asset))
        {
            throw new InvalidDataException(
                $"Particle effect asset '{context.Filename}' is a {asset.GetType().Name} and cannot be loaded as {context.AssetType.Name}.");
        }

        asset.Name = string.IsNullOrWhiteSpace(asset.Name)
            ? Path.GetFileNameWithoutExtension(context.Filename)
            : asset.Name.Trim();
        return asset;
    }
}
