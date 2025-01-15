using System.Text;
using System.Text.Unicode;
using Vocore.Graphics;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine;

public class AssetHotReloaderTexture2D : IAssetHotReloader
{
    private readonly RenderingSystem _renderingSystem;

    public AssetHotReloaderTexture2D(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        Texture2D texture2d = (Texture2D)asset;

        _renderingSystem.CreateTextureCore(
            texture2d.Width, 
            texture2d.Height, 
            ImageLoadOption.Default, 
            out GPUTexture texture, out GPUTextureView textureView);

        _renderingSystem.WriteImageFileToTexture(data, texture);
        texture2d.UnsafeHotReload(texture, textureView);
    }
}
