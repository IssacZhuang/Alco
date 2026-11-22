using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// Loads particle effect asset files (<c>.afx</c>) directly into <see cref="ParticleEffect"/>s.
/// Image, material, and optional behavior-module references validate at load time.
/// </summary>
public class AssetLoaderParticleEffect : BaseAssetLoader<ParticleEffect>
{
    private readonly JsonSerializerOptions _options;
    private readonly ShaderSystem? _shaderSystem;

    /// <summary>Creates the loader without eager shader-module validation.</summary>
    public AssetLoaderParticleEffect(AssetSystem assetSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
        : this(assetSystem, null, typeInfoResolver)
    {
    }

    /// <summary>Creates the loader with serializer options from <see cref="CreateJsonOptions"/> and optional behavior validation.</summary>
    public AssetLoaderParticleEffect(AssetSystem assetSystem, ShaderSystem? shaderSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        _shaderSystem = shaderSystem;
        _options = CreateJsonOptions(assetSystem, typeInfoResolver);
    }

    /// <inheritdoc />
    public override string Name => "ParticleEffect(.afx)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [ParticleAssetPipeline.EffectExtension];

    /// <inheritdoc />
    public override bool CanHandleType(Type type) => type.IsAssignableTo(typeof(ParticleEffect));

    /// <summary>
    /// Creates the serializer options particle effect files parse with: camelCase, comments and trailing commas,
    /// disallowed unmapped members, and polymorphic dispatch over <see cref="ParticleEffect"/>.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(AssetSystem assetSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(ParticleEffect)], typeInfoResolver),
        };
        options.Converters.Add(new JsonConverterMaterialVector3());
        options.Converters.Add(new JsonConverterMaterialVector4());
        options.Converters.Add(new JsonConverterParticleVector2());
        options.Converters.Add(new JsonConverterParticleRange());
        options.Converters.Add(new JsonConverterParticleColor());
        options.Converters.Add(new JsonConverterImage(assetSystem));
        options.Converters.Add(new JsonConverterMaterialAsset(assetSystem));
        options.Converters.Add(new JsonConverterBlendState());
        options.Converters.Add(new JsonConverterDepthStencilState());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        ParticleEffect asset;
        try
        {
            ReadOnlySpan<byte> data = context.GetData();
            if (data.StartsWith([(byte)0xEF, (byte)0xBB, (byte)0xBF]))
                data = data[3..];
            asset = JsonSerializer.Deserialize<ParticleEffect>(data, _options)
                ?? throw new InvalidDataException("The file is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Particle effect asset '{context.Filename}' is invalid: {exception.Message}", exception);
        }

        AssetJson.ValidateVersion(asset.Version, ParticleEffect.FormatVersion, "Particle effect asset", context.Filename);

        if (!context.AssetType.IsInstanceOfType(asset))
        {
            throw new InvalidDataException(
                $"Particle effect asset '{context.Filename}' is a {asset.GetType().Name} and cannot be loaded as {context.AssetType.Name}.");
        }

        if (_shaderSystem != null)
            ValidateBehaviors(asset, context.Filename, _shaderSystem);

        asset.Name = string.IsNullOrWhiteSpace(asset.Name)
            ? Path.GetFileNameWithoutExtension(context.Filename)
            : asset.Name.Trim();
        return asset;
    }

    private static void ValidateBehaviors(ParticleEffect effect, string filename, ShaderSystem shaders)
    {
        IEnumerable<ParticleGroup> groups = effect switch
        {
            ParticleEffect2D e2 => e2.Groups,
            ParticleEffect3D e3 => e3.Groups,
            _ => [],
        };

        foreach (ParticleGroup group in groups)
        {
            if (string.IsNullOrEmpty(group.Behavior))
                continue;
            try
            {
                AlcoEffectsShaders.EnsureBuiltInModulesLoaded(shaders);
                shaders.Compiler.LoadModule(group.Behavior);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Particle effect asset '{filename}' references unknown behavior module '{group.Behavior}': {exception.Message}",
                    exception);
            }
        }
    }
}
