using System.Text;
using System.Text.Unicode;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public class AssetHotReloaderTexture2D : BaseAssetHotReloader<Texture2D>
{
    private readonly RenderingSystem _renderingSystem;

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
