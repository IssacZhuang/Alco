using System.Text;
using System.Text.Unicode;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine;

public class AssetHotReloaderShaderHLSL : IAssetHotReloader
{
    public void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        Shader shader = (Shader)asset;
        shader.UnsafeHotReload(Encoding.UTF8.GetString(data));

    }
}
