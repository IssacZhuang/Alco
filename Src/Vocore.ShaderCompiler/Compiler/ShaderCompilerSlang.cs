using System.Text;
using Vocore.Graphics;
using SlangSharp;

using static SlangSharp.Slang;
using System.Runtime.InteropServices;

namespace Vocore.ShaderCompiler;

public static class ShaderCompilerSlang
{
    private readonly static SlangSession _session = spCreateSession("slang_compile_session");

    public static ShaderModule[] CrearteSpirvShaderModules(string slangCode, string filename = "unnamed_shader.hlsl", ShaderMacroDefine[]? defines = null, BaseSlangFileSystem? fileSystem = null)
    {
        using SlangCompiler compiler = new SlangCompiler(fileSystem);
        SlangCompileOption option = new SlangCompileOption
        {
            SourceLanguage = SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG,
            CompileTarget = SlangCompileTarget.SLANG_SPIRV
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