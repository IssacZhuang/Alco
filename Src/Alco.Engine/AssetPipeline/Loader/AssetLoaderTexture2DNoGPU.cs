using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Lightweight Texture2D asset loader for NoGPU mode.
/// Skips image decoding and creates a minimal 1x1 dummy texture.
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

    public string Name => "AssetLoader.Texture2D.NoGPU";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderTexture2DNoGPU(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public bool CanHandleType(Type type)
    {
        return type == typeof(Texture2D);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        ImageLoadOption option = ImageLoadOption.Default with
        {
            Name = context.Filename
        };

        Texture2DMeta? metaData = null;
        if (context.AssetSystem.TryLoad<Texture2DMeta>(context.Filename + ".meta", out var meta, out _))
        {
            option = option with
            {
                FilterMode = meta.FilterMode,
                AddressMode = meta.AddressMode,
                SlicePadding = meta.SlicePadding
            };

            metaData = meta;
        }

        Texture2D texture = _renderingSystem.CreateTexture2D(1, 1, option);

        if (metaData != null && metaData.Sprites != null && metaData.Sprites.Count > 0)
        {
            texture.ClearSprites();
            foreach (var kvp in metaData.Sprites)
            {
                RectInt pixelRect = kvp.Value;
                Rect uvRect = pixelRect.Normalize(1, 1);
                texture.SetSprite(kvp.Key, uvRect);
            }
        }

        return texture;
    }
}
