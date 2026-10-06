namespace Alco.Graphics;

/// <summary>
/// The creation information for a graphics pipeline: shaders, vertex input,
/// rasterization, blending, depth-stencil state and the target attachment formats.
/// </summary>
public struct GraphicsPipelineDescriptor
{
    public GraphicsPipelineDescriptor(
        GPUBindGroup[] bindGroups,
        ShaderModule[] shaderModules,
        VertexInputLayout[] vertexInputLayouts,
        RasterizerState rasterizerState,
        BlendState blendState,
        DepthStencilState depthStencilState,
        PixelFormat[] colorFormats,
        PixelFormat? depthStencilFormat,
        uint pushConstantsSize = 0,
        string name = "unnamed_graphics_pipeline"
        )
    {
        BindGroups = bindGroups;
        ShaderModules = shaderModules;
        RasterizerState = rasterizerState;
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        VertexInputLayouts = vertexInputLayouts;
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
        PushConstantsSize = pushConstantsSize;
        Name = name;
    }

    public GraphicsPipelineDescriptor(
        GPUBindGroup[] bindGroups,
        ShaderModule[] shaderModules,
        VertexInputLayout[] vertexInputLayouts,
        RasterizerState rasterizerState,
        BlendState blendState,
        DepthStencilState depthStencilState,
        PrimitiveTopology primitiveTopology,
        PixelFormat[] colorFormats,
        PixelFormat? depthStencilFormat,
        uint pushConstantsSize = 0,
        string name = "unnamed_graphics_pipeline"
        )
    {
        BindGroups = bindGroups;
        ShaderModules = shaderModules;
        RasterizerState = rasterizerState;
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        VertexInputLayouts = vertexInputLayouts;
        PrimitiveTopology = primitiveTopology;
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
        PushConstantsSize = pushConstantsSize;
        Name = name;
    }

    /// <summary>
    /// The bind group layouts the pipeline was created with; resource groups used
    /// with it must match these layouts.
    /// </summary>
    public GPUBindGroup[] BindGroups { get; init; }
    /// <summary>
    /// The shader modules feeding the pipeline stages (vertex, fragment, ...).
    /// </summary>
    public ShaderModule[] ShaderModules { get; init; }
    /// <summary>
    /// The vertex input layouts describing vertex buffer bindings.
    /// </summary>
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    /// <summary>
    /// The rasterization state (fill mode, culling, front-face winding).
    /// </summary>
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    /// <summary>
    /// How vertices are assembled into primitives (triangles, lines, ...).
    /// </summary>
    public PrimitiveTopology PrimitiveTopology { get; init; } = PrimitiveTopology.TriangleList;
    /// <summary>
    /// The blending applied to color targets.
    /// </summary>
    public BlendState BlendState { get; init; }
    /// <summary>
    /// The depth and stencil test configuration.
    /// </summary>
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.None;
    /// <summary>
    /// The pixel formats of the color targets the pipeline renders to, in
    /// render-target order.
    /// </summary>
    public PixelFormat[] ColorFormats { get; init; }
    /// <summary>
    /// The pixel format of the depth-stencil target, or null when the render pass
    /// has no depth attachment.
    /// </summary>
    public PixelFormat? DepthStencilFormat { get; init; }
    /// <summary>
    /// The number of color targets the fragment shader writes to. Color targets at or
    /// beyond this index have no matching fragment output and are created with a zero
    /// write mask, which wgpu-core requires instead of failing pipeline validation.
    /// Defaults to writing every target.
    /// </summary>
    public int FragmentOutputCount { get; init; } = int.MaxValue;
    /// <summary>
    /// Total size in bytes of the push constants (immediates) block used by the shaders, 0 when unused.
    /// Per-stage visibility is declared by the shaders themselves.
    /// </summary>
    public uint PushConstantsSize { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_graphics_pipeline";
}