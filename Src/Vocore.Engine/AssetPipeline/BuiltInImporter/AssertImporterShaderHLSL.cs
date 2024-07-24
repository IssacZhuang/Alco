using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;

namespace Vocore.Engine;

public class AssertImporterShaderHLSL : IAssetImporter
{

    private static readonly string[] Extensions = new string[] { FileExt.ShaderHLSL };
    private readonly Func<string, string>? _includeResolver;
    public string Name => "AssetImporter.Shader.HLSL";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssertImporterShaderHLSL(Func<string, string>? includeResolver = null)
    {
        _includeResolver = includeResolver;
    }

    public bool TryImport(string filename, byte[] file, [NotNullWhen(true)] out byte[] importedFile, [NotNullWhen(true)] out string? importedFilename)
    {
        importedFilename = Path.ChangeExtension(filename, FileExt.ShaderBinary);
        ShaderCompileResult compileResult = Rendering.ShaderCompiler.Compile(Encoding.UTF8.GetString(file), filename, _includeResolver);
        importedFile = UtilsShaderSerialization.EncodeCompileResult(compileResult);
        return true;
    }


}