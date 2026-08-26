using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Alco.Graphics;
using Alco.IO;
using Alco;

namespace Alco.Rendering;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// Creates and owns a <see cref="TextureOptionCache"/> internally for directory-level
/// and per-file import option resolution.
/// <br/>File-backed assets stream: the header is probed with minimal per-format reads,
/// the texture is created at its final specification and returned immediately, and its
/// content uploads in place asynchronously, so the texture's identity never changes.
/// Preloaded (in-memory) contexts decode synchronously.
/// </summary>
public class AssetLoaderTexture2D : IAssetLoader
{
    private static readonly string[] Extensions = new string[] {
        FileExt.ImagePNG,
        FileExt.ImageJPG,
        FileExt.ImageBMP,
        FileExt.ImageTGA,
        FileExt.ImageGIF,
        FileExt.ImageHDR,
        FileExt.ImageDDS
    };

    private readonly RenderingSystem _renderingSystem;
    private readonly TextureOptionCache? _cache;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Texture2D";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance without option caching.
    /// Texture import options will use engine defaults only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    public AssetLoaderTexture2D(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <summary>
    /// Initializes a new instance with directory cascade and per-file option caching.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create textures.</param>
    /// <param name="assetSystem">The asset system used for option file discovery and loading.</param>
    public AssetLoaderTexture2D(RenderingSystem renderingSystem, AssetSystem assetSystem)
    {
        _renderingSystem = renderingSystem;
        _cache = new TextureOptionCache(assetSystem);
    }

    /// <inheritdoc/>
    public bool CanHandleType(Type type)
    {
        return type == typeof(Texture2D);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        // Engine defaults
        ImageLoadOption option = ImageLoadOption.Default with { Name = context.Filename };

        // Resolve import options (directory cascade + .meta)
        Texture2DMeta? metaData = null;
        if (_cache != null)
        {
            var (importOption, meta) = _cache.Resolve(context.Filename);
            if (importOption != null)
            {
                if (importOption.FilterMode.HasValue)
                    option = option with { FilterMode = importOption.FilterMode.Value };
                if (importOption.AddressMode.HasValue)
                    option = option with { AddressMode = importOption.AddressMode.Value };
                if (importOption.SlicePadding.HasValue)
                    option = option with { SlicePadding = importOption.SlicePadding.Value };
                if (importOption.PremultiplyAlpha.HasValue)
                    option = option with { PremultiplyAlpha = importOption.PremultiplyAlpha.Value };
            }
            metaData = meta;
        }

        // Create the texture. File-backed assets stream: probe the header, pre-create
        // the texture at its final specification, upload the content in place
        // asynchronously. Headers that cannot be probed fall back to synchronous decode,
        // as do preloaded (in-memory) contexts.
        Texture2D texture;
        if (context.CanGetStream)
        {
            Stream stream = context.GetStream();
            try
            {
                texture = _renderingSystem.CreateTexture2DStreaming(stream, option);
            }
            catch (ImageDecodeException)
            {
                stream.Dispose();
                texture = _renderingSystem.CreateTexture2DFromFile(context.GetData(), option);
            }
        }
        else
        {
            texture = _renderingSystem.CreateTexture2DFromFile(context.GetData(), option);
        }

        // Sprites (only from .meta)
        if (metaData != null && metaData.Sprites != null && metaData.Sprites.Count > 0)
        {
            texture.ClearSprites();
            foreach (var kvp in metaData.Sprites)
            {
                RectInt pixelRect = kvp.Value;
                Rect uvRect = pixelRect.Normalize(texture.Width, texture.Height);
                texture.SetSprite(kvp.Key, uvRect);
            }
        }

        return texture;
    }
}
