using System.Text;
using Vortice.Dxc;

namespace Vocore.Graphics;

public static class ShaderCompilerDxc
{
    public static ShaderStageSource CrearteSpirvShaderSource(string hlslCode, ShaderStage stage, string entry, string filename = "unnamed_shader.hlsl", ShaderMacroDefine[]? defines = null)
    {
        byte[] spirv = ConvetHlslToSpirv(hlslCode, filename, entry, stage, defines);
        return new ShaderStageSource(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetHlslToSpirv(string hlslCode, string filename, string entry, ShaderStage stage, ShaderMacroDefine[]? defines = null)
    {
        // use custom optimization recipe to prevent layout changes
        IDxcResult result = DxcCompiler.Compile(ConvertShaderStage(stage), hlslCode, entry, new DxcCompilerOptions()
        {
            GenerateSpirv = true,
            SkipOptimizations = false,
            OptimizationLevel = 3,
            SpvPreserveInterface = true,
            SpvPreserveBindings = true,
        }, filename, ShaderMacroDefine.ToDxcMacro(defines));

        if (result.GetStatus() != SharpGen.Runtime.Result.Ok)
        {
            throw new ShaderCompilationException($"Result: {result.GetStatus()}, Desc: {result.GetStatus().Description},  Message: {result.GetErrors()}");
        }

        return result.GetObjectBytecodeArray();
    }
   
    private static DxcShaderStage ConvertShaderStage(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => DxcShaderStage.Vertex,
            ShaderStage.Fragment => DxcShaderStage.Pixel,
            ShaderStage.Compute => DxcShaderStage.Compute,
            ShaderStage.Hull => DxcShaderStage.Hull,
            ShaderStage.Domain => DxcShaderStage.Domain,
            _ => throw new NotSupportedException("Unsupported shader stage")
        };
    }
}