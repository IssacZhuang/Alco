using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The state cache for the GPU pipeline.
/// Must be used with <see cref="Shader.TryUpdatePipelineContext"/> to set the pipeline. Otherwise, the pipeline will be null.
/// </summary>
public struct GraphicsPipelineContext
{
    public GPUPipeline? Pipeline;
    public GPUAttachmentLayout? AttachmentLayout;
    public ShaderReflectionInfo? ReflectionInfo;
    public DepthStencilState DepthStencil;
    public BlendState BlendState;
    public RasterizerState Rasterizer;
    public PrimitiveTopology PrimitiveTopology;
    public uint Version;

    /// <summary>
    /// The specialization arguments the context was built for (set by the
    /// Shader.GetGraphicsPipeline call; TryUpdatePipelineContext keeps them) —
    /// the variant identity of the cached pipeline, where defines used to live.
    /// </summary>
    public string[]? Specializations;

    /// <summary>
    /// The size in bytes of the push constants block, or 0 when the pipeline is not set.
    /// </summary>
    public readonly int PushConstantsSize => ReflectionInfo?.PushConstantsSize ?? 0;

    public static readonly GraphicsPipelineContext Default = new GraphicsPipelineContext();

    public GraphicsPipelineContext()
    {
        Pipeline = null;
        AttachmentLayout = null;
        ReflectionInfo = null;
        DepthStencil = DepthStencilState.Default;
        BlendState = BlendState.Opaque;
        Rasterizer = RasterizerState.CullNone;
        PrimitiveTopology = PrimitiveTopology.TriangleList;
        Version = 0;
    }

    public GraphicsPipelineContext(
        ShaderReflectionInfo? reflectionInfo,
        DepthStencilState depthStencil,
        BlendState blendState,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology)

    {
        ReflectionInfo = reflectionInfo;
        DepthStencil = depthStencil;
        BlendState = blendState;
        Rasterizer = rasterizer;
        PrimitiveTopology = primitiveTopology;
        Version = 0;
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
        Version = 0;
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
        throw new KeyNotFoundException($"Resource '{name}' not found in shader");
    }


}