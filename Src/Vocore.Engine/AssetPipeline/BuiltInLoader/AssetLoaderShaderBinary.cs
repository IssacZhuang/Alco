using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;


namespace Vocore.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderBinary : IAssetLoader<Shader>
{
    private static readonly string[] Extensions = [FileExt.ShaderBinary];

    private readonly RenderingSystem _renderingSystem;
    public string Name => "AssetLoader.Shader.Binary";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderShaderBinary(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out Shader? asset)
    {
        ShaderCompileResult preprocessed = UtilsShaderSerialization.DecodeCompileResult(file.ToArray());
        asset = _renderingSystem.CreateShader(preprocessed); // create GPU object
        return true;
    }
}
