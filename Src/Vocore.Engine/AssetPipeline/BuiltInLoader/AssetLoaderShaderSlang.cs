using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;
using SlangSharp;


namespace Vocore.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderSlang : BaseAssetLoader<Shader, ShaderCompileResult>
{
    private static readonly string[] Extensions = new string[] { FileExt.ShaderSlang };

    private readonly RenderingSystem _renderingSystem;

    public override string Name => "AssetLoader.Shader.Slang";

    public override IReadOnlyList<string> FileExtensions => Extensions;

    private SlangAssetFileSystem _fileSystem;

    public AssetLoaderShaderSlang(RenderingSystem renderingSystem, AssetSystem assetSystem)
    {
        _fileSystem = new SlangAssetFileSystem(assetSystem);
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    protected override bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out ShaderCompileResult? preprocessed)
    {
        preprocessed = UtilsShaderSlang.Compile(Encoding.UTF8.GetString(file), filename, _fileSystem);
        //debug save spirv
        string appPath = Environment.CurrentDirectory;
        string filePathVetex = Path.Combine(appPath, "spirv", Path.GetFileNameWithoutExtension(filename) + ".slang.vert.spv");
        string filePathFragment = Path.Combine(appPath, "spirv", Path.GetFileNameWithoutExtension(filename) + ".slang.frag.spv");
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
        asset = _renderingSystem.CreateShader(preprocessed); 
        return true;
    }

    private class SlangAssetFileSystem : BaseSlangFileSystem
    {
        private readonly AssetSystem _assetSystem;
        public SlangAssetFileSystem(AssetSystem assetSystem)
        {
            _assetSystem = assetSystem;
        }
        public override bool TryLoadFile(string path, out ReadOnlySpan<byte> data)
        {
            return _assetSystem.TryLoadRaw(path, out data);
        }
    }

}
