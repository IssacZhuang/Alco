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

    public object OnAsyncPreprocess(string filename, byte[] data)
    {
        throw new NotImplementedException();
    }

    public bool OnLoad(string filename, object? preprocessed, [NotNullWhen(true)] out Texture2D? asset)
    {
        throw new NotImplementedException();
    }
}
