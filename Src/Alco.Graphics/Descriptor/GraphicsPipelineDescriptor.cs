namespace Alco.Graphics;

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

    public GPUBindGroup[] BindGroups { get; init; }
    public ShaderModule[] ShaderModules { get; init; }
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public PrimitiveTopology PrimitiveTopology { get; init; } = PrimitiveTopology.TriangleList;
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.None;
    public PixelFormat[] ColorFormats { get; init; }
    public PixelFormat? DepthStencilFormat { get; init; }
    /// <summary>
    /// The number of color targets the fragment shader writes to. Color targets at or
    /// beyond this index have no matching fragment output and are created with a zero
    /// write mask, which WebGPU requires instead of failing pipeline validation.
    /// Defaults to writing every target.
    /// </summary>
    public int FragmentOutputCount { get; init; } = int.MaxValue;
    /// <summary>
    /// Total size in bytes of the push constants (immediates) block used by the shaders, 0 when unused.
    /// Per-stage visibility is declared by the shaders themselves.
    /// </summary>
    public uint PushConstantsSize { get; init; }
    public string Name { get; init; } = "unnamed_graphics_pipeline";
}