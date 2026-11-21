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

    private readonly ImageUploadQueue _uploadQueue;
    private readonly ImageOptionCache? _cache;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Image";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance without option caching.
    /// Image import options will use engine defaults only.
    /// </summary>
    /// <param name="uploadQueue">The streaming image upload queue used to decode and upload textures.</param>
    public AssetLoaderImage(ImageUploadQueue uploadQueue)
    {
        ArgumentNullException.ThrowIfNull(uploadQueue);
        _uploadQueue = uploadQueue;
    }

    /// <summary>
    /// Initializes a new instance with directory cascade and per-file option caching.
    /// </summary>
    /// <param name="uploadQueue">The streaming image upload queue used to decode and upload textures.</param>
    /// <param name="assetSystem">The asset system used for option file discovery and loading.</param>
    /// <param name="typeInfoResolver">
    /// Optional resolver supplying option-file type metadata (a source generated
    /// JsonSerializerContext under NativeAOT); defaults to reflection.
    /// </param>
    public AssetLoaderImage(
        ImageUploadQueue uploadQueue,
        AssetSystem assetSystem,
        IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(uploadQueue);
        ArgumentNullException.ThrowIfNull(assetSystem);

        _uploadQueue = uploadQueue;
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
                return ImageUtility.CreateStreaming(_uploadQueue, stream, option, sprites);
            }
            catch (ImageDecodeException)
            {
                stream.Dispose();
                return ImageUtility.CreateFromFile(_uploadQueue.RenderingSystem, context.GetData(), option, sprites);
            }
        }

        return ImageUtility.CreateFromFile(_uploadQueue.RenderingSystem, context.GetData(), option, sprites);
    }
}
