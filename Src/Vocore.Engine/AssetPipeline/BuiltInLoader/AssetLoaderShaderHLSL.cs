using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;


namespace Vocore.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderHLSL : IAssetLoader<Shader>
{
    private static readonly string[] Extensions = [FileExt.ShaderHLSL];
    private readonly Func<string, string>? _includeResolver;
    private readonly RenderingSystem _renderingSystem;

    public string Name => "AssetLoader.Shader.HLSL";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderShaderHLSL(RenderingSystem renderingSystem, Func<string, string>? includeResolver = null)
    {
        _includeResolver = includeResolver;
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out Shader? asset)
    {
        ShaderCompileResultDeprecated preprocessed = UtilsShaderHLSL.Compile(Encoding.UTF8.GetString(file), filename, _includeResolver);
        asset = _renderingSystem.CreateShader(preprocessed); // create GPU object
        return true;
    }

}
