using System.Text.Json.Serialization.Metadata;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Lightweight <see cref="Image"/> asset loader for NoGPU mode.
/// Skips image decoding and creates a minimal 1x1 placeholder texture while preserving resolved import metadata.
/// </summary>
public class AssetLoaderImageNoGPU : IAssetLoader
{
    private static readonly string[] Extensions =
    [
        FileExt.ImagePNG,
        FileExt.ImageJPG,
        FileExt.ImageBMP,
        FileExt.ImageTGA,
        FileExt.ImageGIF,
        FileExt.ImageHDR,
        FileExt.ImageDDS,
    ];

    private readonly RenderingSystem _renderingSystem;
    private readonly ImageOptionCache? _cache;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Image.NoGPU";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance without option caching.
    /// Image import options will use engine defaults only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    public AssetLoaderImageNoGPU(RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        _renderingSystem = renderingSystem;
    }

    /// <summary>
    /// Initializes a new instance with directory cascade and per-file option caching.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    /// <param name="assetSystem">The asset system used for option file discovery and loading.</param>
    /// <param name="typeInfoResolver">
    /// Optional resolver supplying option-file type metadata (a source generated
    /// JsonSerializerContext under NativeAOT); defaults to reflection.
    /// </param>
    public AssetLoaderImageNoGPU(
        RenderingSystem renderingSystem,
        AssetSystem assetSystem,
        IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        ArgumentNullException.ThrowIfNull(assetSystem);

        _renderingSystem = renderingSystem;
        _cache = new ImageOptionCache(assetSystem, typeInfoResolver);
    }

    /// <inheritdoc/>
    public bool CanHandleType(Type type) => type == typeof(Image);

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        var (option, _) = ImageOptionCache.ResolveLoadParameters(_cache, context.Filename, includeSprites: false);
        Texture placeholder = TextureUtility.CreateTexture(
            _renderingSystem,
            1,
            1,
            option.Format,
            option.Usage,
            option.MipLevels);
        try
        {
            return new Image(placeholder, option, sprites: null, ownsResource: true);
        }
        catch
        {
            if (!placeholder.IsDestroyed)
            {
                placeholder.Destroy();
            }
            throw;
        }
    }
}
