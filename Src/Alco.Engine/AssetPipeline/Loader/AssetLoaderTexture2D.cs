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

    public string Name => "AssetLoader.Texture2D";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderTexture2D(RenderingSystem renderingSystem)
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
        if (context.AssetSystem.TryLoad<Texture2DMeta>(context.Filename + ".meta", out var meta, out string? failedReason))
        {
            option = option with
            {
                FilterMode = meta.FilterMode,
                AddressMode = meta.AddressMode,
                SlicePadding = meta.SlicePadding
            };

            metaData = meta;
        }

        Texture2D texture = _renderingSystem.CreateTexture2DFromFile(context.Data, option);

        // Populate sprites from meta if available
        if (metaData != null && metaData.Sprites != null && metaData.Sprites.Count > 0)
        {
            texture.ClearSprites();
            foreach (var kvp in metaData.Sprites)
            {
                // Texture2DMeta.Rect -> RectInt (implicit), then normalize to Rect UVs
                RectInt pixelRect = kvp.Value;
                Rect uvRect = pixelRect.Normalize(texture.Width, texture.Height);
                texture.SetSprite(kvp.Key, uvRect);
            }
        }

        return texture;
    }
}
