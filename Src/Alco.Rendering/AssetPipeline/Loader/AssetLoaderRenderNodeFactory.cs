using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Loads render node factory files (<c>.rnfact</c>) into <see cref="RenderNodeFactory"/> instances with shader references resolved and validated at load time.
/// </summary>
public class AssetLoaderRenderNodeFactory : BaseAssetLoader<RenderNodeFactory>
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoaderRenderNodeFactory"/> class.
    /// </summary>
    /// <param name="shaderSystem">The shader system used to resolve shader references.</param>
    /// <param name="typeInfoResolver">The optional JSON type metadata resolver; reflection-based when null.</param>
    public AssetLoaderRenderNodeFactory(ShaderSystem shaderSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        _options = CreateJsonOptions(shaderSystem, typeInfoResolver);
    }

    /// <inheritdoc />
    public override string Name => "RenderNodeFactory(.rnfact)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [FileExt.RenderNodeFactory];

    /// <summary>
    /// The serializer options used to parse render node factory files, exposed for tests and tooling.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions(ShaderSystem shaderSystem, IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(RenderNodeFactory)], typeInfoResolver),
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
