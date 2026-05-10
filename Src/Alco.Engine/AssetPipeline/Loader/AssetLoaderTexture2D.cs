using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;
using Alco;

namespace Alco.Engine;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// Supports directory-level import options via <see cref="DirectoryOptionCache{T}"/>.
/// </summary>
public class AssetLoaderTexture2D : IAssetLoader
{
    private static readonly string[] Extensions = new string[] {
        FileExt.ImagePNG,
        FileExt.ImageJPG,
        FileExt.ImageBMP,
        FileExt.ImageTGA,
        FileExt.ImageGIF,
        FileExt.ImageHDR
        };

    private readonly RenderingSystem _renderingSystem;
    private readonly DirectoryOptionCache<Texture2DImportOption>? _optionCache;

    /// <inheritdoc/>
    public string Name => "AssetLoader.Texture2D";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderTexture2D(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public AssetLoaderTexture2D(RenderingSystem renderingSystem, DirectoryOptionCache<Texture2DImportOption> optionCache)
    {
        _renderingSystem = renderingSystem;
        _optionCache = optionCache;
    }

    /// <inheritdoc/>
    public bool CanHandleType(Type type)
    {
        return type == typeof(Texture2D);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        // 1. Engine defaults
        ImageLoadOption option = ImageLoadOption.Default with { Name = context.Filename };

        // 2. Directory option (only non-null fields override)
        if (_optionCache != null)
        {
            string directory = GetDirectory(context.Filename);
            if (_optionCache.TryGetOption(directory, out var dirOption))
            {
                if (dirOption.FilterMode.HasValue)
                    option = option with { FilterMode = dirOption.FilterMode.Value };
                if (dirOption.AddressMode.HasValue)
                    option = option with { AddressMode = dirOption.AddressMode.Value };
                if (dirOption.SlicePadding.HasValue)
                    option = option with { SlicePadding = dirOption.SlicePadding.Value };
            }
        }

        // 3. .meta file (only explicitly declared fields override)
        Texture2DMeta? metaData = null;
        if (context.AssetSystem.TryLoad<Texture2DMeta>(context.Filename + ".meta", out var meta, out _))
        {
            if (meta.FilterMode.HasValue)
                option = option with { FilterMode = meta.FilterMode.Value };
            if (meta.AddressMode.HasValue)
                option = option with { AddressMode = meta.AddressMode.Value };
            if (meta.SlicePadding.HasValue)
                option = option with { SlicePadding = meta.SlicePadding.Value };
            metaData = meta;
        }

        // 4. Create texture
        Texture2D texture = _renderingSystem.CreateTexture2DFromFile(context.Data, option);

        // 5. Sprites (only from .meta)
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

    private static string GetDirectory(string filename)
    {
        string? dir = Path.GetDirectoryName(filename);
        if (string.IsNullOrEmpty(dir))
            return string.Empty;
        return dir.Replace('\\', '/').TrimEnd('/');
    }
}
