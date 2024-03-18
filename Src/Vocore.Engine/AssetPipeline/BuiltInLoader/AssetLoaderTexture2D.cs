using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// </summary>
public class AssetLoaderTexture2D : BaseAssetLoader<Texture2D, ImageResultBuffer>
{
    private static readonly string[] Extensions = new string[] { ".png", ".jpg", ".bmp", ".tga", ".gif", ".hdr" };

    public override string Name => "AssetLoader.Texture2D";

    public override IReadOnlyList<string> FileExtensions => Extensions;

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
            asset = Texture2D.CreateFromData(preprocessed.Memory.Pointer, preprocessed.Memory.Length, (uint)preprocessed.Width, (uint)preprocessed.Height, Texture2D.GetPixelSize(preprocessed.Comp));
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
