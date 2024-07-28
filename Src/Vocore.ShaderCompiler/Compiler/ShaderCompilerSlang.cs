using System.Text;
using Vocore.Graphics;
using SlangSharp;

using static SlangSharp.Slang;

namespace Vocore.ShaderCompiler;

public static class ShaderCompilerSlang
{
    public static ShaderModule CrearteSpirvShaderSource(string slangCode, ShaderStage stage, string filename = "unnamed_shader.hlsl", ShaderMacroDefine[]? defines = null)
    {
        byte[] spirv = ConvetHlslToSpirv(slangCode, filename, stage, defines, out string entry);
        return new ShaderModule(stage, ShaderLanguage.SPIRV, spirv, entry);
    }

    public static byte[] ConvetHlslToSpirv(string slangCode, string filename, ShaderStage stage, ShaderMacroDefine[]? defines, out string entryName)
    {
        SlangSession session = spCreateSession("slang_compile_session");
        SlangCompileRequest request = spCreateCompileRequest(session);

        spSetCodeGenTarget(request, SlangCompileTarget.SLANG_SPIRV);
        int translationUnitIndex = spAddTranslationUnit(request, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, filename);
        spAddTranslationUnitSourceString(request, translationUnitIndex, filename, slangCode);

        if (defines != null)
        {
            for (int i = 0; i < defines.Length; i++)
            {
                spAddPreprocessorDefine(request, defines[i].name, defines[i].value);
            }
        }

        SlangResult result = spCompile(request);
        if (result.IsError)
        {
            throw new ShaderCompilationException(request.GetDiagnosticString());
        }

        SlangReflection reflection = spGetReflection(request);
        var entryCount = spReflection_getEntryPointCount(reflection);

        SlangStage requiredStage = ConvertShaderStage(stage);
        int entryIndex = -1;
        entryName = string.Empty;

        for (uint i = 0; i < entryCount; i++)
        {
            SlangReflectionEntryPoint entryPoint = spReflection_getEntryPointByIndex(reflection, i);
            SlangStage entryStage = spReflectionEntryPoint_getStage(entryPoint);
            if (entryStage == requiredStage)
            {
                entryName = entryPoint.GetName();
                entryIndex = (int)i;
                break;
            }
        }

        if (entryIndex == -1)
        {
            throw new ShaderCompilationException($"Failed to find entry point for the required stage {stage}");
        }

        byte[] spirv = request.GetBytesByEntryPointIndex(entryIndex);

        spDestroyCompileRequest(request);
        spDestroySession(session);

        return spirv;
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

    private static SlangStage ConvertShaderStage(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => SlangStage.SLANG_STAGE_VERTEX,
            ShaderStage.Fragment => SlangStage.SLANG_STAGE_FRAGMENT,
            ShaderStage.Compute => SlangStage.SLANG_STAGE_COMPUTE,
            ShaderStage.Hull => SlangStage.SLANG_STAGE_HULL,
            ShaderStage.Domain => SlangStage.SLANG_STAGE_DOMAIN,
            _ => throw new NotSupportedException("Unsupported shader stage")
        };
    }

}