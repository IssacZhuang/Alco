using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// </summary>
public class AssetLoaderTexture2D : BaseAssetLoader<Texture2D, ImageResult>
{
    private static readonly string[] Extensions = new string[] { ".png", ".jpg", ".bmp", ".tga", ".gif", ".hdr" };

    public override string Name => "AssetLoader.Texture2D";

    public override IReadOnlyList<string> FileExtensions => Extensions;

    protected override bool TryAsyncPreprocessCore(string filename, byte[] file, [NotNullWhen(true)] out ImageResult? preprocessed)
    {
        preprocessed = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        return true;
    }

    protected override bool TryCreateAssetCore(string filename, ImageResult preprocessed, [NotNullWhen(true)] out Texture2D? asset)
    {
        asset = Texture2D.CreateFromData(preprocessed.Data, (uint)preprocessed.Width, (uint)preprocessed.Height, Texture2D.GetPixelSize(preprocessed.Comp));
        return true;
    }
}
