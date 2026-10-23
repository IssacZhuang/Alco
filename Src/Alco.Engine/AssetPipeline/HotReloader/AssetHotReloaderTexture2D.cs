using System.Text;
using System.Text.Unicode;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Hot reloads 2D textures in place.
/// </summary>
public class AssetHotReloaderTexture2D : BaseAssetHotReloader<Texture2D>
{
    private readonly RenderingSystem _renderingSystem;

    /// <summary>
    /// Initializes a new instance of <see cref="AssetHotReloaderTexture2D"/>.
    /// </summary>
    /// <param name="renderingSystem">Rendering system used to reload texture data.</param>
    public AssetHotReloaderTexture2D(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public override void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        Texture2D texture2d = (Texture2D)asset;

        // Use the optimized hot reload method from RenderingSystem
        _renderingSystem.UnsafeHotReloadTexture2DByFile(texture2d, data, ImageLoadOption.Default);
    }
}
