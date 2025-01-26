using System.Text;
using Alco.Graphics;
using DirectXShaderCompiler.NET;

namespace Alco.ShaderCompiler;

public static class ShaderCompilerDxc
{
    public static ShaderModule CrearteSpirvShaderModule(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename,
        FileIncludeHandler? includeHandler = null)

    {
        byte[] spirv = ConvetHlslToSpirv(hlslCode, filename, entry, stage, Span<ShaderMacroDefine>.Empty);
        return new ShaderModule(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static ShaderModule CrearteSpirvShaderModule(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename,
        Span<ShaderMacroDefine> defines,
        FileIncludeHandler? includeHandler = null)

    {
        byte[] spirv = ConvetHlslToSpirv(hlslCode, filename, entry, stage, defines);
        return new ShaderModule(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetHlslToSpirv(
        string hlslCode, 
        string filename, 
        string entry, 
        ShaderStage stage,
        Span<ShaderMacroDefine> defines,
        FileIncludeHandler? includeHandler = null)
    {
        CompilerOptions options = new CompilerOptions(ConvertShaderStage(stage).ToProfile(6, 0))
        {
            entryPoint = entry,
            generateAsSpirV = true,
            optimization = OptimizationLevel.O3,
            preserveInterface = true,
            preserveBindings = true,
        };

        for(int i = 0; i < defines.Length; i++)
        {
            options.SetMacro(defines[i].Name, defines[i].Value);
        }
    

        CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(hlslCode, options, includeHandler);

        if (result.compilationErrors != null)
        {
            throw new ShaderCompilationException($"Error on compiling shader '{filename}': {result.compilationErrors}");
        }

        return result.objectBytes;
    }

    private static ShaderType ConvertShaderStage(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => ShaderType.Vertex,
            ShaderStage.Fragment => ShaderType.Fragment,
            ShaderStage.Compute => ShaderType.Compute,
            ShaderStage.Hull => ShaderType.Hull,
            ShaderStage.Domain => ShaderType.Domain,
            _ => throw new NotSupportedException("Unsupported shader stage")
        };
    }
}