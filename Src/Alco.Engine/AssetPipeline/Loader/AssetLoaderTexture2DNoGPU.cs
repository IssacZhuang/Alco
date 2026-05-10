using Alco.Rendering;
using Alco.IO;
using Alco.Graphics;
using Alco;

namespace Alco.Engine;

/// <summary>
/// Lightweight Texture2D asset loader for NoGPU mode.
/// Skips image decoding and creates a minimal 1x1 dummy texture.
/// Supports directory-level import options via <see cref="DirectoryOptionCache{T}"/>.
/// </summary>
public class AssetLoaderTexture2DNoGPU : IAssetLoader
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
    public string Name => "AssetLoader.Texture2D.NoGPU";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderTexture2DNoGPU(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public AssetLoaderTexture2DNoGPU(RenderingSystem renderingSystem, DirectoryOptionCache<Texture2DImportOption> optionCache)
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
        if (context.AssetSystem.TryLoad<Texture2DMeta>(context.Filename + ".meta", out var meta, out _))
        {
            if (meta.FilterMode.HasValue)
                option = option with { FilterMode = meta.FilterMode.Value };
            if (meta.AddressMode.HasValue)
                option = option with { AddressMode = meta.AddressMode.Value };
            if (meta.SlicePadding.HasValue)
                option = option with { SlicePadding = meta.SlicePadding.Value };
        }

        return _renderingSystem.CreateTexture2D(1, 1, option);
    }

    private static string GetDirectory(string filename)
    {
        string? dir = Path.GetDirectoryName(filename);
        if (string.IsNullOrEmpty(dir))
            return string.Empty;
        return dir.Replace('\\', '/').TrimEnd('/');
    }
}
