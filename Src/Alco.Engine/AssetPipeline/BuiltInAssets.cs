

using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Provides the engine's built-in shaders, fonts, and textures.
/// </summary>
public partial class BuiltInAssets
{
    private readonly AssetSystem _assets;
    private readonly ShaderSystem _shaderSystem;

    /// <summary>
    /// Initializes a new instance of <see cref="BuiltInAssets"/>.
    /// </summary>
    /// <param name="assets">Asset system used to load built-in assets.</param>
    /// <param name="shaderSystem">Shader system used to obtain built-in shaders.</param>
    public BuiltInAssets(AssetSystem assets, ShaderSystem shaderSystem)
    {
        _assets = assets;
        _shaderSystem = shaderSystem;
    }

    private Shader GetShader(string moduleName)
    {
        return _shaderSystem.GetShader(moduleName);
    }

    private Font GetFont(string path)
    {
        return _assets.Load<Font>(path);
    }

    // the rest parts is auto generated 
}

