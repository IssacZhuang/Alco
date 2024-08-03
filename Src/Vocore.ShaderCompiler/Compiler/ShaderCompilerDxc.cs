using System.Text;
using Vocore.Graphics;
using DirectXShaderCompiler.NET;

namespace Vocore.ShaderCompiler;

public static class ShaderCompilerDxc
{
    public static ShaderModule CrearteSpirvShaderModule(string hlslCode, ShaderStage stage, string entry, string filename = "unnamed_shader.hlsl", ShaderMacroDefine[]? defines = null)
    {
        byte[] spirv = ConvetHlslToSpirv(hlslCode, filename, entry, stage, defines);
        return new ShaderModule(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetHlslToSpirv(string hlslCode, string filename, string entry, ShaderStage stage, ShaderMacroDefine[]? defines = null)
    {
        // use custom optimization recipe to prevent layout changes
        // IDxcResult result = DxcCompiler.Compile(ConvertShaderStage(stage), hlslCode, entry, new DxcCompilerOptions()
        // {
        //     GenerateSpirv = true,
        //     SkipOptimizations = false,
        //     OptimizationLevel = 3,
        //     SpvPreserveInterface = true,
        //     SpvPreserveBindings = true,
        // }, filename, ShaderMacroDefine.ToDxcMacro(defines));

        // if (result.GetStatus().Failure)
        // {
        //     DxcResultCode dxcResult = DxcResultCode.GetDxcResult(result.GetStatus().Code);
        //     if (dxcResult.Code != DxcResultCode.Unknown.Code)
        //     {
        //         throw new ShaderCompilationException($"{dxcResult}  Message: {result.GetErrors()}");
        //     }
        //     else
        //     {
        //         throw new ShaderCompilationException(result.GetErrors());
        //     }

        // }

        // return result.GetObjectBytecodeArray();

        CompilerOptions options = new CompilerOptions(ConvertShaderStage(stage).ToProfile(6, 0))
        {
            entryPoint = entry,
            generateAsSpirV = true,
            optimization = OptimizationLevel.O3,
            preserveInterface = true,
            preserveBindings = true,
        };

        CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(hlslCode, options);

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