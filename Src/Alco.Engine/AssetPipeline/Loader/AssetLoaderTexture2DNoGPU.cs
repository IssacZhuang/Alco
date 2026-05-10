using Alco.Rendering;
using Alco.IO;
using Alco.Graphics;
using Alco;

namespace Alco.Engine;

/// <summary>
/// Lightweight Texture2D asset loader for NoGPU mode.
/// Skips image decoding and creates a minimal 1x1 dummy texture.
/// Creates and owns a <see cref="TextureOptionCache"/> internally.
/// </summary>
public class AssetLoaderTexture2DNoGPU : IAssetLoader
{
    private static readonly string[] Extensions = new string[] {
        FileExt.ImagePNG, FileExt.ImageJPG, FileExt.ImageBMP,
        FileExt.ImageTGA, FileExt.ImageGIF, FileExt.ImageHDR
    };

    private readonly RenderingSystem _renderingSystem;
    private readonly TextureOptionCache? _cache;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Texture2D.NoGPU";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance without option caching.
    /// Texture import options will use engine defaults only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    public AssetLoaderTexture2DNoGPU(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <summary>
    /// Initializes a new instance with directory cascade and per-file option caching.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    /// <param name="assetSystem">The asset system used for option file discovery and loading.</param>
    public AssetLoaderTexture2DNoGPU(RenderingSystem renderingSystem, AssetSystem assetSystem)
    {
        _renderingSystem = renderingSystem;
        _cache = new TextureOptionCache(assetSystem);
    }

    /// <inheritdoc/>
    public bool CanHandleType(Type type) => type == typeof(Texture2D);

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        // 1. Engine defaults
        ImageLoadOption option = ImageLoadOption.Default with { Name = context.Filename };

        // 2. Resolve import options (directory cascade + .meta)
        if (_cache != null)
        {
            var (importOption, _) = _cache.Resolve(context.Filename);
            if (importOption != null)
            {
                if (importOption.FilterMode.HasValue)
                    option = option with { FilterMode = importOption.FilterMode.Value };
                if (importOption.AddressMode.HasValue)
                    option = option with { AddressMode = importOption.AddressMode.Value };
                if (importOption.SlicePadding.HasValue)
                    option = option with { SlicePadding = importOption.SlicePadding.Value };
            }
        }

        return _renderingSystem.CreateTexture2D(1, 1, option);
    }
}
