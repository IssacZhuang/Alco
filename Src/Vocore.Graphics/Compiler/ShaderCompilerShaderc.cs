using Silk.NET.Shaderc;

using static Silk.NET.Shaderc.Shaderc;

namespace Vocore.Graphics;

public unsafe static class ShaderCompilerShaderc
{
    private static readonly Shaderc Api = Shaderc.GetApi();

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
        CompileOptions* options = Api.CompileOptionsInitialize();
        Api.CompileOptionsSetSourceLanguage(options, language);

        if (defines != null)
        {
            foreach (var define in defines)
            {
                Api.CompileOptionsAddMacroDefinition(options, define.name, define.value);
            }
        }

        Api.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
        //Api.CompileOptionsSetGenerateDebugInfo(options);
        Api.CompileOptionsSetWarningsAsErrors(options);
        Api.CompileOptionsSetAutoMapLocations(options, true);
        Api.CompileOptionsSetAutoBindUniforms(options, true);
        Api.CompileOptionsSetAutoCombinedImageSampler(options, false);
        Api.CompileOptionsSetTargetEnv(options, TargetEnv.Webgpu, 0);
        //Api.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc16);

        Compiler* compiler = Api.CompilerInitialize();

        CompilationResult* result = Api.CompileIntoSpv(compiler, hlslCode, GetShaderKind(stage), filename, entry, options);

        if (Api.ResultGetNumErrors(result) > 0)
        {
            GraphicsLogger.Error(Api.ResultGetErrorMessageS(result));
        }
        nuint size = Api.ResultGetLength(result);
        byte* data = Api.ResultGetBytes(result);

        byte[] output = new byte[size];
        for (nuint i = 0; i < size; i++)
        {
            output[i] = data[i];
        }

        Api.ResultRelease(result);
        Api.CompilerRelease(compiler);
        Api.CompileOptionsRelease(options);

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