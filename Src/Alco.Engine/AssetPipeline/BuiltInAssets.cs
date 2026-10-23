

using System;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Provides the engine's built-in shaders, fonts, and textures.
/// </summary>
public partial class BuiltInAssets
{
    private readonly AssetSystem _assets;
    private readonly RenderingSystem _rendering;

    /// <summary>
    /// Initializes a new instance of <see cref="BuiltInAssets"/>.
    /// </summary>
    /// <param name="assets">Asset system used to load built-in assets.</param>
    /// <param name="rendering">Rendering system used to obtain built-in shaders.</param>
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

