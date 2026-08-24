using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Loads render node factory files (<c>.rnfact</c>, jsonc) into
/// <see cref="RenderNodeFactory"/>s: the file's <c>$type</c> discriminator
/// selects the factory class (the engine's polymorphic JSON convention,
/// discovered by assembly scan — factory classes in user assemblies configure
/// without registration), shader references resolve typed through the shared
/// shader system at load time (the material asset convention — a typoed module
/// fails at load with the file's name), and a bad file (unknown discriminator,
/// unknown field, malformed value) fails here too. The factories stay inert
/// data otherwise: nodes materialize only when
/// <see cref="RenderNodeFactory.Create"/> runs.
/// </summary>
public class AssetLoaderRenderNodeFactory : BaseAssetLoader<RenderNodeFactory>
{
    private readonly JsonSerializerOptions _options;

    public AssetLoaderRenderNodeFactory(ShaderSystem shaderSystem)
    {
        _options = CreateJsonOptions(shaderSystem);
    }

    /// <inheritdoc />
    public override string Name => "RenderNodeFactory(.rnfact)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [FileExt.RenderNodeFactory];

    /// <summary>
    /// The serializer options render node factory files parse with: author-friendly
    /// (camelCase, comments and trailing commas tolerated), strict about unmapped
    /// members, polymorphic over <see cref="RenderNodeFactory"/>, shader references
    /// resolving through the shader system, enums as strings. Exposed for tests
    /// and tooling that parse factory files outside the asset system.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(ShaderSystem shaderSystem)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(RenderNodeFactory)]),
        };
        options.Converters.Add(new JsonConverterShader(shaderSystem));
        options.Converters.Add(new JsonConverterShaderLibrary(shaderSystem));
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        try
        {
            return JsonSerializer.Deserialize<RenderNodeFactory>(context.GetData(), _options)
                ?? throw new InvalidDataException("The file is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Render node factory '{context.Filename}' is invalid: {exception.Message}", exception);
        }
    }
}
