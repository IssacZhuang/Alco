using Silk.NET.Shaderc;

namespace Vocore.Graphics;

public unsafe static class ShaderCompilerShaderc
{
    private static readonly Shaderc API = Shaderc.GetApi();

    //glsl

    public static ShaderStageSource CrearteSpirvSourceFromGlsl(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename = "unnamed_shader.glsl",
        ShaderMacroDefine[]? defines = null)
    {
        byte[] spirv = ConvetGlslToSpirv(hlslCode, stage, entry, filename, defines);
        return new ShaderStageSource(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetGlslToSpirv(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename = "unnamed_shader.glsl",
        ShaderMacroDefine[]? defines = null)
    {
        return ConvetShaderToSpirv(hlslCode, stage, entry, SourceLanguage.Glsl, filename, defines);
    }

    //hlsl

    public static ShaderStageSource CrearteSpirvSourceFromHlsl(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename = "unnamed_shader.hlsl",
        ShaderMacroDefine[]? defines = null)
    {
        byte[] spirv = ConvetHlslToSpirv(hlslCode, stage, entry, filename, defines);
        return new ShaderStageSource(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetHlslToSpirv(
        string hlslCode,
        ShaderStage stage,
        string entry,
        string filename = "unnamed_shader.hlsl",
        ShaderMacroDefine[]? defines = null)
    {
        return ConvetShaderToSpirv(hlslCode, stage, entry, SourceLanguage.Hlsl, filename, defines);
    }

    private static byte[] ConvetShaderToSpirv(
        string hlslCode,
        ShaderStage stage,
        string entry,
        SourceLanguage language,
        string filename = "unnamed_shader.glsl",
        ShaderMacroDefine[]? defines = null)
    {
        CompileOptions* options = API.CompileOptionsInitialize();
        API.CompileOptionsSetSourceLanguage(options, language);

        if (defines != null)
        {
            foreach (var define in defines)
            {
                API.CompileOptionsAddMacroDefinition(options, define.name, define.value);
            }
        }

        API.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
        //Api.CompileOptionsSetGenerateDebugInfo(options);
        API.CompileOptionsSetWarningsAsErrors(options);
        API.CompileOptionsSetAutoMapLocations(options, true);
        API.CompileOptionsSetAutoBindUniforms(options, true);
        API.CompileOptionsSetAutoCombinedImageSampler(options, false);
        API.CompileOptionsSetTargetEnv(options, TargetEnv.Webgpu, 0);
        //Api.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc16);

        Compiler* compiler = API.CompilerInitialize();

        CompilationResult* result = API.CompileIntoSpv(compiler, hlslCode, GetShaderKind(stage), filename, entry, options);

        if (API.ResultGetNumErrors(result) > 0)
        {
            GraphicsLogger.Error(API.ResultGetErrorMessageS(result));
        }
        nuint size = API.ResultGetLength(result);
        byte* data = API.ResultGetBytes(result);

        byte[] output = new byte[size];
        for (nuint i = 0; i < size; i++)
        {
            output[i] = data[i];
        }

        API.ResultRelease(result);
        API.CompilerRelease(compiler);
        API.CompileOptionsRelease(options);

        return output;
    }
    private static ShaderKind GetShaderKind(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => ShaderKind.VertexShader,
            ShaderStage.Fragment => ShaderKind.FragmentShader,
            ShaderStage.Compute => ShaderKind.ComputeShader,
            ShaderStage.Hull => ShaderKind.TessControlShader,
            ShaderStage.Domain => ShaderKind.TessEvaluationShader,
            _ => throw new NotSupportedException("Unsupported shader stage")
        };
    }
}