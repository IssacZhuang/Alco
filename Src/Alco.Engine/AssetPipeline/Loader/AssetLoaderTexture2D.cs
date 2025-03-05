using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;


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
        return type == typeof(Texture2D) || type == typeof(Sprite);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        if (context.AssetType == typeof(Texture2D))
        {
            return _renderingSystem.CreateTexture2DFromFile(context.Data, ImageLoadOption.Default with
            {
                Name = context.Filename
            });
        }
        //todo: create sprite

        throw new InvalidOperationException($"Cannot create asset of type {context.AssetType.Name}");
    }

    

}
