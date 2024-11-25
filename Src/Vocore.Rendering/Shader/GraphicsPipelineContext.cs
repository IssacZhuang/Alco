using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The state cache for the GPu pipeline.
/// Must be used with <see cref="Shader.TryUpdatePipelineContext"/> to set the pipeline. Otherwise, the pipeline will be null.
/// </summary>
public struct GraphicsPipelineContext
{
    public GPUPipeline? Pipeline;
    public GPURenderPass? RenderPass;
    public ShaderReflectionInfo? ReflectionInfo;
    public DepthStencilState DepthStencil;
    public BlendState BlendState;
    public RasterizerState Rasterizer;
    public PrimitiveTopology PrimitiveTopology;
    public string[] Defines;

    public static readonly GraphicsPipelineContext Default = new GraphicsPipelineContext();

    public GraphicsPipelineContext()
    {
        Pipeline = null;
        RenderPass = null;
        ReflectionInfo = null;
        DepthStencil = DepthStencilState.Default;
        BlendState = BlendState.Opaque;
        Rasterizer = RasterizerState.CullNone;
        PrimitiveTopology = PrimitiveTopology.TriangleList;
        Defines = Array.Empty<string>();
    }

    public GraphicsPipelineContext(
        DepthStencilState depthStencil,
        BlendState blendState,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology,
        string[] defines)
    {
        DepthStencil = depthStencil;
        BlendState = blendState;
        Rasterizer = rasterizer;
        PrimitiveTopology = primitiveTopology;
        Defines = defines;
    }

    public GraphicsPipelineContext(
        DepthStencilState depthStencil,
        BlendState blendState,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology)
    {
        DepthStencil = depthStencil;
        BlendState = blendState;
        Rasterizer = rasterizer;
        PrimitiveTopology = primitiveTopology;
        Defines = Array.Empty<string>();
    }


    public readonly bool TryGetResourceId(string name, out uint resourceId)
    {
        if (ReflectionInfo == null)
        {
            resourceId = 0;
            return false;
        }

        return ReflectionInfo.TryGetResourceId(name, out resourceId);
    }

    public readonly uint GetResourceId(string name)
    {
        if (ReflectionInfo == null)
        {
            throw new Exception("ReflectionInfo is null");
        }

        if (ReflectionInfo.TryGetResourceId(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader {Pipeline!.Name}");
    }

    public readonly GraphicsPipelineContext Clone()
    {
        return new GraphicsPipelineContext(DepthStencil, BlendState, Rasterizer, PrimitiveTopology, Defines);
    }
}