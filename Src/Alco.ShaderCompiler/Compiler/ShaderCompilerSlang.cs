using System.Text;
using Alco.Graphics;
using SlangSharp;

using static SlangSharp.Slang;
using System.Runtime.InteropServices;

namespace Alco.ShaderCompiler;

public static class ShaderCompilerSlang
{
    private readonly static SlangSession _session = spCreateSession("slang_compile_session");

    public static ShaderModule[] CrearteSpirvShaderModules(string slangCode, string filename = "unnamed_shader.slang", ShaderMacroDefine[]? defines = null, BaseSlangFileSystem? fileSystem = null)
    {
        using SlangCompiler compiler = new SlangCompiler(fileSystem);
        SlangCompileOption option = new SlangCompileOption
        {
            SourceLanguage = SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG,
            TargetFlags = SlangTargetFlags.SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY,
            CompileTarget = SlangCompileTarget.SLANG_SPIRV,
            OptimizationLevel = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE,
            MatrixLayoutMode = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
            PreserveParameters = true,
        };

        SlangCompileResult[] results = compiler.Compile(filename, slangCode, option);

        ShaderModule[] modules = new ShaderModule[results.Length];

        for (int i = 0; i < results.Length; i++)
        {
            SlangCompileResult result = results[i];
            ShaderStage stage = ConvertShaderStage(result.Stage);
            modules[i] = new ShaderModule(stage, ShaderLanguage.SPIRV, result.Spirv, result.EntryPoint);
        }

        return modules;
    }

    private static ShaderStage ConvertShaderStage(SlangStage stage)
    {
        return stage switch
        {
            SlangStage.SLANG_STAGE_VERTEX => ShaderStage.Vertex,
            SlangStage.SLANG_STAGE_FRAGMENT => ShaderStage.Fragment,
            SlangStage.SLANG_STAGE_COMPUTE => ShaderStage.Compute,
            SlangStage.SLANG_STAGE_HULL => ShaderStage.Hull,
            SlangStage.SLANG_STAGE_DOMAIN => ShaderStage.Domain,
            _ => throw new NotSupportedException("Unsupported shader stage")
        };
    }


}