using System.Diagnostics.CodeAnalysis;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Loads a ".slang" asset as a native slang module shader (plan Phase 2): the
/// asset path's file name is the module identity (dashed form), and the shader
/// is produced — with every module it imports — by the RenderingSystem's
/// module shader service. There is no flattening and no
/// legacy compiler involvement; the file's <c>module</c> declaration carries the imports.
/// </summary>
public class AssetLoaderShaderSlang : IAssetLoader
{
    private static readonly string[] Extensions = [FileExt.ShaderSlang];
    private readonly RenderingSystem _renderingSystem;

    public string Name => "AssetLoader.Shader.SlangModule";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderShaderSlang(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public bool CanHandleType(Type type)
    {
        return type == typeof(Shader);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        string moduleName = Path.GetFileNameWithoutExtension(context.Filename).Replace('_', '-');
        return _renderingSystem.GetShaderForAsset(context.Filename, moduleName);
    }
}
