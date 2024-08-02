using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;


namespace Vocore.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderHLSL : BaseAssetLoader<Shader, ShaderCompileResult>
{
    private static readonly string[] Extensions = new string[] { FileExt.ShaderHLSL};
    private readonly Func<string, string>? _includeResolver;
    private readonly RenderingSystem _renderingSystem;

    public override string Name => "AssetLoader.Shader.HLSL";

    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderShaderHLSL(RenderingSystem renderingSystem, Func<string, string>? includeResolver = null)
    {
        _includeResolver = includeResolver;
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    protected override bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out ShaderCompileResult? preprocessed)
    {
        //compile to spirv and get reflection info
        preprocessed = UtilsShaderHLSL.Compile(Encoding.UTF8.GetString(file), filename, _includeResolver);
        //debug save spirv
        string appPath = Environment.CurrentDirectory;
        string filePathVetex = Path.Combine(appPath, "spirv", Path.GetFileNameWithoutExtension(filename) + ".hlsl.vert.spv");
        string filePathFragment = Path.Combine(appPath, "spirv", Path.GetFileNameWithoutExtension(filename) + ".hlsl.frag.spv");
        if (!Directory.Exists(Path.Combine(appPath, "spirv")))
        {
            Directory.CreateDirectory(Path.Combine(appPath, "spirv"));
        }



        File.WriteAllBytes(filePathVetex, preprocessed.VertexShader!.Value.Source);
        File.WriteAllBytes(filePathFragment, preprocessed.FragmentShader!.Value.Source);


        return true;
    }

    /// <inheritdoc/>
    protected override bool TryCreateAssetCore(string filename, ShaderCompileResult preprocessed, [NotNullWhen(true)] out Shader? asset)
    {
        asset = _renderingSystem.CreateShader(preprocessed); // create GPU object
        return true;
    }

}
