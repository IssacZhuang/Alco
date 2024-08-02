using System.Diagnostics.CodeAnalysis;
using System.Text;
using SlangSharp;
using Vocore.Graphics;
using Vocore.ShaderCompiler;

namespace Vocore.Rendering;

public static class UtilsShaderSlang
{
    public static ShaderCompileResult Compile(string code, string filename, BaseSlangFileSystem? fileSystem = null)
    {
        ShaderPreproccessResultSlang preproccessed = PreprocessSlang(code, filename);
        return Compile(preproccessed, fileSystem);
    }

    public static ShaderCompileResult Compile(ShaderPreproccessResultSlang preproccessed, BaseSlangFileSystem? fileSystem = null)
    {
        ShaderModule? vertexShader = null;
        ShaderModule? fragmentShader = null;
        ShaderModule? computeShader = null;

        ShaderModule[] modules = ShaderCompilerSlang.CrearteSpirvShaderModules(preproccessed.ShaderText, preproccessed.Filename, null, fileSystem);
        ShaderStage stages = ShaderStage.None;

        for (int i = 0; i < modules.Length; i++)
        {
            ShaderModule module = modules[i];
            if (module.Stage == ShaderStage.Vertex)
            {
                vertexShader = module;
                stages |= ShaderStage.Vertex;
            }
            else if (module.Stage == ShaderStage.Fragment)
            {
                fragmentShader = module;
                stages |= ShaderStage.Fragment;
            }
            else if (module.Stage == ShaderStage.Compute)
            {
                computeShader = module;
                stages |= ShaderStage.Compute;
            }
        }

        if (stages.IsGraphicsShader())
        {
            ShaderReflectionInfo reflection = UtilsShaderRelfection.GetSpirvReflection(vertexShader!.Value.Source, fragmentShader!.Value.Source, true);
            return new ShaderCompileResult(preproccessed.Filename, vertexShader, fragmentShader, computeShader, stages, preproccessed.RasterizerState, preproccessed.BlendState, preproccessed.DepthStencilState, preproccessed.PrimitiveTopology, reflection);
        }
        else if (stages.IsComputeShader())
        {
            ShaderReflectionInfo reflection = UtilsShaderRelfection.GetSpirvReflection(computeShader!.Value.Source);
            return new ShaderCompileResult(preproccessed.Filename, vertexShader, fragmentShader, computeShader, stages, preproccessed.RasterizerState, preproccessed.BlendState, preproccessed.DepthStencilState, preproccessed.PrimitiveTopology, reflection);
        }

        throw new Exception("Missing entry point for shader.");
    }

    public static ShaderPreproccessResultSlang PreprocessSlang(string code, string filename)
    {
        ShaderPreproccessResultSlang result = new ShaderPreproccessResultSlang()
        {
            ShaderText = code,
            Filename = filename,
        };

        ShaderPragma[] pragmas = UtilsShaderText.GetPragmas(code);
        result.Pragmas = pragmas;

        for(int i = 0; i < pragmas.Length; i++)
        {
            ShaderPragma pragma = pragmas[i];
            
            if (UtilsShaderText.TryGetBlendState(pragma, out BlendState blendState))
            {
                result.BlendState = blendState;
            }

            if (UtilsShaderText.TryGetRasterizerState(pragma, out RasterizerState rasterizerState))
            {
                result.RasterizerState = rasterizerState;
            }

            if (UtilsShaderText.TryGetDepthStencilState(pragma, out DepthStencilState depthStencilState))
            {
                result.DepthStencilState = depthStencilState;
            }

            if (UtilsShaderText.TryGetPrimitiveTopology(pragma, out PrimitiveTopology? primitiveTopology))
            {
                result.PrimitiveTopology = primitiveTopology;
            }
        }

        if (!result.RasterizerState.HasValue)
        {
            result.RasterizerState = RasterizerState.CullNone;
        }

        if (!result.DepthStencilState.HasValue)
        {
            result.DepthStencilState = DepthStencilState.Default;
        }

        if (!result.BlendState.HasValue)
        {
            result.BlendState = BlendState.Opaque;
        }

        if (!result.PrimitiveTopology.HasValue)
        {
            result.PrimitiveTopology = PrimitiveTopology.TriangleList;
        }

        return result;
    }

}