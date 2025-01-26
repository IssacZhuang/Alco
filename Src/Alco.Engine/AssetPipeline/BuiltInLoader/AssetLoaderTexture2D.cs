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
public class AssetLoaderTexture2D : IAssetLoader<Texture2D>
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

    /// <inheritdoc/>
    public Texture2D CreateAsset(string filename, ReadOnlySpan<byte> file)
    {
        return _renderingSystem.CreateTexture2DFromFile(file, ImageLoadOption.Default with
        {
            Name = filename
        });
    }
}
