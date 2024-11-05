using Vocore.Graphics;

namespace Vocore.Rendering;


//deprecated
public class ShaderCompileResultDeprecated
{
    public bool IsGraphicsShader
    {
        get
        {
            return VertexShader.HasValue && FragmentShader.HasValue;
        }
    }

    public bool IsComputeShader
    {
        get
        {
            return ComputeShader.HasValue;
        }
    }
    public string Filename { get; }

    public ShaderModule? VertexShader { get; }
    public ShaderModule? FragmentShader { get; }
    public ShaderModule? ComputeShader { get; }

    public ShaderStage Stages { get; }

    public RasterizerState? RasterizerState { get; }
    public BlendState? BlendState { get; }
    public DepthStencilState? DepthStencilState { get;  }
    public PrimitiveTopology? PrimitiveTopology { get;  }

    public ShaderReflectionInfo ReflectionInfo { get; }

    internal ShaderCompileResultDeprecated(
        string filename,
        ShaderModule? vertex,
        ShaderModule? fragment,
        ShaderModule? compute,
        
        ShaderStage stages,

        RasterizerState? rasterizerState,
        BlendState? blendState,
        DepthStencilState? depthStencilState,
        PrimitiveTopology? primitiveTopology,

        ShaderReflectionInfo reflectionInfo)
    {
        Filename = filename;

        VertexShader = vertex;
        FragmentShader = fragment;
        ComputeShader = compute;
        ReflectionInfo = reflectionInfo;

        Stages = stages;

        RasterizerState = rasterizerState;
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        PrimitiveTopology = primitiveTopology;

    }

    public static ShaderCompileResultDeprecated CreateGraphics(
        ShaderModule vertex,
        ShaderModule fragment,
        ShaderPreproccessResultHLSLDeprecated preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
        return new ShaderCompileResultDeprecated(
            preproccessResult.Filename, 
            vertex, 
            fragment, 
            null, 
            preproccessResult.Stages, 
            preproccessResult.RasterizerState, 
            preproccessResult.BlendState, 
            preproccessResult.DepthStencilState, 
            preproccessResult.PrimitiveTopology, 
            reflectionInfo
            );
    }

    public static ShaderCompileResultDeprecated CreateGraphics(
        ShaderModule vertex,
        ShaderModule fragment,
        ShaderStage stages,
        ShaderPreproccessResultSlang preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
        return new ShaderCompileResultDeprecated(
            preproccessResult.Filename, 
            vertex, 
            fragment, 
            null,
            stages, 
            preproccessResult.RasterizerState, 
            preproccessResult.BlendState, 
            preproccessResult.DepthStencilState, 
            preproccessResult.PrimitiveTopology, 
            reflectionInfo
            );
    }

    public static ShaderCompileResultDeprecated CreateCompute(
        ShaderModule compute,
        ShaderPreproccessResultHLSLDeprecated preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
        return new ShaderCompileResultDeprecated(
            preproccessResult.Filename, 
            null, 
            null, 
            compute, 
            preproccessResult.Stages, 
            preproccessResult.RasterizerState, 
            preproccessResult.BlendState, 
            preproccessResult.DepthStencilState, 
            preproccessResult.PrimitiveTopology, 
            reflectionInfo
            );
    }

    public static ShaderCompileResultDeprecated CreateCompute(
        ShaderModule compute,
        ShaderStage stages,
        ShaderPreproccessResultSlang preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
        return new ShaderCompileResultDeprecated(
            preproccessResult.Filename, 
            null, 
            null, 
            compute, 
            stages, 
            preproccessResult.RasterizerState, 
            preproccessResult.BlendState, 
            preproccessResult.DepthStencilState, 
            preproccessResult.PrimitiveTopology, 
            reflectionInfo
            );
    }
}