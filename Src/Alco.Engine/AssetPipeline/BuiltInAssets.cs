

using System;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public partial class BuiltInAssets
{
    private readonly AssetSystem _assets;
    private readonly RenderingSystem _rendering;

    public BuiltInAssets(AssetSystem assets, RenderingSystem rendering)
    {
        _assets = assets;
        _rendering = rendering;
    }

    private Shader GetShader(string moduleName)
    {
        return _rendering.ShaderSystem.GetShader(moduleName);
    }

    private Font GetFont(string path)
    {
        return _assets.Load<Font>(path);
    }

    // the rest parts is auto generated 
}

