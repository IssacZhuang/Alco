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
    public ShaderReflection? ReflectionInfo;
    public DepthStencilState DepthStencil;
    public BlendState BlendState;
    public RasterizerState Rasterizer;
    public PrimitiveTopology PrimitiveTopology;
    public uint Version;

    /// <summary>
    /// The specialization arguments the context was built for (set by the
    /// Shader.GetGraphicsPipeline call; TryUpdatePipelineContext keeps them) —
    /// the variant identity of the cached pipeline.
    /// </summary>
    public string[]? Specializations;

    /// <summary>
    /// The size in bytes of the push constants block, or 0 when the pipeline is not set.
    /// </summary>
    public readonly int PushConstantsSize => ReflectionInfo?.PushConstantsSize ?? 0;

    /// <summary>
    /// A default context with no pipeline or reflection info set.
    /// </summary>
    public static readonly GraphicsPipelineContext Default = new GraphicsPipelineContext();

    /// <summary>
    /// Creates a context with default pipeline state and no pipeline set.
    /// </summary>
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

    /// <summary>
    /// Creates a context with the given reflection info and fixed-function state.
    /// </summary>
    /// <param name="reflectionInfo">The shader reflection info, or null when no pipeline is set.</param>
    /// <param name="depthStencil">The depth-stencil state.</param>
    /// <param name="blendState">The blend state.</param>
    /// <param name="rasterizer">The rasterizer state.</param>
    /// <param name="primitiveTopology">The primitive topology.</param>
    public GraphicsPipelineContext(
        ShaderReflection? reflectionInfo,
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

    /// <summary>
    /// Creates a context with the given fixed-function state and no reflection info.
    /// </summary>
    /// <param name="depthStencil">The depth-stencil state.</param>
    /// <param name="blendState">The blend state.</param>
    /// <param name="rasterizer">The rasterizer state.</param>
    /// <param name="primitiveTopology">The primitive topology.</param>
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


    /// <summary>
    /// Tries to resolve the binding id of a shader resource by name.
    /// </summary>
    /// <param name="name">The resource name declared in the shader.</param>
    /// <param name="resourceId">The resolved resource id, or 0 when not found.</param>
    /// <returns><c>true</c> when the resource was found; otherwise <c>false</c>.</returns>
    public readonly bool TryGetResourceId(string name, out uint resourceId)
    {
        if (ReflectionInfo == null)
        {
            resourceId = 0;
            return false;
        }

        return ReflectionInfo.TryGetResourceId(name, out resourceId);
    }

    /// <summary>
    /// Resolves the binding id of a shader resource by name.
    /// </summary>
    /// <param name="name">The resource name declared in the shader.</param>
    /// <returns>The resolved resource id.</returns>
    /// <exception cref="KeyNotFoundException">No resource with the given name exists in the shader.</exception>
    /// <exception cref="Exception">The context has no reflection info.</exception>
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