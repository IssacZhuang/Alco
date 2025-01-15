using System.Text;
using System.Text.Unicode;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine;

public class AssetHotReloaderShaderHLSL : IAssetHotReloader
{
    private readonly Func<string, string>? _includeResolver;

    public AssetHotReloaderShaderHLSL(Func<string, string>? includeResolver = null)
    {
        _includeResolver = includeResolver;
    }

    public void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        Shader shader = (Shader)asset;
        string shaderText = Encoding.UTF8.GetString(data);

        if (_includeResolver != null)
        {
            IncludeHelper includeHelper = new();
            shaderText = includeHelper.ProcessInclude(shaderText, shader.Name, _includeResolver);
        }

        shader.UnsafeHotReload(shaderText);
    }
}
