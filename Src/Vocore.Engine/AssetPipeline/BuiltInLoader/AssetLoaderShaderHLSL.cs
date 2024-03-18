using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderHLSL : BaseAssetLoader<Shader, ShaderCompileResult>
{
    private static readonly string[] Extensions = new string[] { ".hlsl"};

    public override string Name => "AssetLoader.HLSL";

    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc/>
    protected override bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out ShaderCompileResult? preprocessed)
    {
        //compile to spirv and get reflection info
        preprocessed = ShaderCompiler.Compile(Encoding.UTF8.GetString(file), filename, IncludeResolver);
        return true;
    }

    /// <inheritdoc/>
    protected override bool TryCreateAssetCore(string filename, ShaderCompileResult preprocessed, [NotNullWhen(true)] out Shader? asset)
    {
        asset = Shader.CreateFromCompileResult(preprocessed); // create GPU object
        return true;
    }

    private string IncludeResolver(string includeFilename)
    {
        if(GameEngine.Instance.Assets.TryLoad(includeFilename, out string? included)){
            return included;
        }
        throw new AssetLoadException($"Failed to load include file '{includeFilename}'");
    }
}
