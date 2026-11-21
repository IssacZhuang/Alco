using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Hot reloads 2D textures in place.
/// </summary>
public class AssetHotReloaderTexture2D : BaseAssetHotReloader<Texture2D>
{
    /// <summary>
    /// Initializes a new instance of <see cref="AssetHotReloaderTexture2D"/>.
    /// </summary>
    public AssetHotReloaderTexture2D()
    {
    }

    public override void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        Texture2D texture2d = (Texture2D)asset;
        Texture2DUtility.UnsafeHotReloadTexture2DByFile(texture2d, data, ImageLoadOption.Default);
    }
}
