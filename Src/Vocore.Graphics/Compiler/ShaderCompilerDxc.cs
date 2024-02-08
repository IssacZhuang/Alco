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
            SkipOptimizations = false
        }, filename, ShaderMacroDefine.ToDxcMacro(defines), null, new string[] { BuildOptArgs() });

        if (result.GetStatus() != SharpGen.Runtime.Result.Ok)
        {

            throw new ShaderCompilationException(result.GetErrors());
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


    public static string BuildOptArgs()
    {
        return "-Oconfig=" + string.Join(",", OptimizationQueue);
    }

    // modified from spirv-opt -O
    private static readonly string[] OptimizationQueue = new string[]{
        "--wrap-opkill",
        "--eliminate-dead-branches",
        "--merge-return",
        "--inline-entry-points-exhaustive",
        "--eliminate-dead-functions",
        "--eliminate-dead-code-aggressive",
        "--private-to-local",
        "--eliminate-local-single-block",
        "--eliminate-local-single-store",
        "--eliminate-dead-code-aggressive",
        "--scalar-replacement=100",
        "--convert-local-access-chains",
        "--eliminate-local-single-block",
        "--eliminate-local-single-store",
        "--eliminate-dead-code-aggressive",
        "--ssa-rewrite",
        "--eliminate-dead-code-aggressive",
        "--ccp",
        "--eliminate-dead-code-aggressive",
        "--loop-unroll",
        "--eliminate-dead-branches",
        "--redundancy-elimination",
        "--combine-access-chains",
        "--simplify-instructions",
        "--scalar-replacement=100",
        "--convert-local-access-chains",
        "--eliminate-local-single-block",
        "--eliminate-local-single-store",
        // "--eliminate-dead-code-aggressive",
        "--ssa-rewrite",
        // "--eliminate-dead-code-aggressive",
        "--vector-dce",
        "--eliminate-dead-inserts",
        "--eliminate-dead-branches",
        "--simplify-instructions",
         "--if-conversion",
        "--copy-propagate-arrays",
        "--reduce-load-size",
        // "--eliminate-dead-code-aggressive",
        "--merge-blocks",
        "--redundancy-elimination",
        "--eliminate-dead-branches",
        "--merge-blocks",
        "--simplify-instructions"
    };
}