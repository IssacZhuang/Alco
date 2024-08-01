using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderCompileResult
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

    internal ShaderCompileResult(
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

    public static ShaderCompileResult CreateGraphics(
        ShaderModule vertex,
        ShaderModule fragment,
        ShaderPreproccessResultHLSL preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
        return new ShaderCompileResult(
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

    public static ShaderCompileResult CreateGraphics(
        ShaderModule vertex,
        ShaderModule fragment,
        ShaderStage stages,
        ShaderPreproccessResultSlang preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
        return new ShaderCompileResult(
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

    public static ShaderCompileResult CreateCompute(
        ShaderModule compute,
        ShaderPreproccessResultHLSL preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
        return new ShaderCompileResult(
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

    public static ShaderCompileResult CreateCompute(
        ShaderModule compute,
        ShaderStage stages,
        ShaderPreproccessResultSlang preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        //return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
        return new ShaderCompileResult(
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