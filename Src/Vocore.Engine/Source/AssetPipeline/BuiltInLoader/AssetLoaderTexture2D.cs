using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class AssetLoaderTexture2D : IAssetLoader<Texture2D>
{
    private static readonly string[] Extensions = new string[] { ".png", ".jpg", ".bmp", ".tga", ".gif", ".hdr" };
    public string Name => "AssetLoaderTexture2D";

    public IEnumerable<string> FileExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Extensions;
    }

    public bool TryLoad(string filename, byte[] data, out Texture2D asset)
    {
#pragma warning disable CS8625
        asset = null;
#pragma warning restore CS8625
        if (data == null)
        {
            return false;
        }

        try
        {
            asset = Texture2D.CreateFromFile(data, new ImageLoadOption
            {
                IsSRGB = false,
                MipLevels = 1,
                Usage = TextureUsage.Standard,
                Name = filename
            });
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load texture2d from file: {filename}, {e.Message}");
            return false;
        }

        return true;
    }
}



