using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StbImageSharp;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for Texture2D assets.
/// </summary>
public class AssetLoaderTexture2D : IAssetLoader<Texture2D>
{
    private static readonly string[] Extensions = new string[] { ".png", ".jpg", ".bmp", ".tga", ".gif", ".hdr" };

    /// <summary>
    /// Gets the name of the asset loader.
    /// </summary>
    public string Name => "AssetLoader.Texture2D";

    /// <summary>
    /// Gets the file extensions supported by the asset loader.
    /// </summary>
    public IEnumerable<string> FileExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Extensions;
    }

    /// <summary>
    /// Attempts to load a Texture2D asset from the specified file.
    /// </summary>
    /// <param name="filename">The name of the file.</param>
    /// <param name="data">The byte array containing the file data.</param>
    /// <param name="asset">When this method returns, contains the loaded Texture2D asset, if the loading was successful; otherwise, null.</param>
    /// <returns>true if the loading was successful; otherwise, false.</returns>
    public bool TryLoad(string filename, byte[] data, [NotNullWhen(true)] out Texture2D? asset)
    {
        asset = null;
        if (data == null)
        {
            return false;
        }

        ColorComponents targetComponents = ColorComponents.RedGreenBlueAlpha;
        ImageResult? image = null;

        image = ImageResult.FromMemory(data, targetComponents);

        asset = Texture2D.CreateFromFile(data, new ImageLoadOption
        {
            IsSRGB = false,
            MipLevels = 1,
            Usage = TextureUsage.Standard,
            Name = filename
        });
        return true;
    }
}
