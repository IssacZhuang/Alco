using System.Text.Json.Serialization.Metadata;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Asset loader for immutable <see cref="Image"/> assets with optional import-option resolution
/// and asynchronous file streaming uploads.
/// </summary>
public class AssetLoaderImage : IAssetLoader
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
    public string Name => "AssetLoader.Image";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance without option caching.
    /// Image import options will use engine defaults only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create GPU textures.</param>
    public AssetLoaderImage(RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        _renderingSystem = renderingSystem;
    }

    /// <summary>
    /// Initializes a new instance with directory cascade and per-file option caching.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create GPU textures.</param>
    /// <param name="assetSystem">The asset system used for option file discovery and loading.</param>
    /// <param name="typeInfoResolver">
    /// Optional resolver supplying option-file type metadata (a source generated
    /// JsonSerializerContext under NativeAOT); defaults to reflection.
    /// </param>
    public AssetLoaderImage(
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
        var (option, sprites) = ImageOptionCache.ResolveLoadParameters(_cache, context.Filename, includeSprites: true);

        if (context.CanGetStream)
        {
            Stream stream = context.GetStream();
            try
            {
                return ImageUtility.CreateStreaming(_renderingSystem, stream, option, sprites);
            }
            catch (ImageDecodeException)
            {
                stream.Dispose();
                return ImageUtility.CreateFromFile(_renderingSystem, context.GetData(), option, sprites);
            }
        }

        return ImageUtility.CreateFromFile(_renderingSystem, context.GetData(), option, sprites);
    }
}
