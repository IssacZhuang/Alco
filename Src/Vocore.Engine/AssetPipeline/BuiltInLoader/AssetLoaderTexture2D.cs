using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;


namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// </summary>
public class AssetLoaderTexture2D : BaseAssetLoader<Texture2D, ImageResultBuffer>
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

    public override string Name => "AssetLoader.Texture2D";

    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderTexture2D(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    protected override bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out ImageResultBuffer? preprocessed)
    {
        preprocessed = ImageResultBuffer.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        return true;
    }

    /// <inheritdoc/>
    protected unsafe override bool TryCreateAssetCore(string filename, ImageResultBuffer preprocessed, [NotNullWhen(true)] out Texture2D? asset)
    {
        try
        {
            asset = _renderingSystem.CreateTexture2D(preprocessed.Memory.Pointer, (uint)preprocessed.Memory.Length, (uint)preprocessed.Width, (uint)preprocessed.Height, RenderingSystem.GetPixelSize(preprocessed.Comp));
        }
        catch (Exception e)
        {
            Log.Error(e);
            asset = null;
            return false;
        }
        finally
        {
            preprocessed.Dispose();
        }

        return true;
    }
}
