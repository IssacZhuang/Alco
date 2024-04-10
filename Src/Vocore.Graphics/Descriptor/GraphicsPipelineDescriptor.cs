namespace Vocore.Graphics;

public struct GraphicsPipelineDescriptor
{
    public GraphicsPipelineDescriptor(
        GPUBindGroup[] bindGroups,
        ShaderStageSource[] shaderStages,
        VertexInputLayout[] vertexInputLayouts,
        RasterizerState rasterizerState,
        BlendState blendState,
        DepthStencilState depthStencilState,
        PixelFormat[] colorFormats,
        PixelFormat? depthStencilFormat,
        PushConstantsRange[]? pushConstantsRanges = null,
        string name = "unnamed_graphics_pipeline"
        )
    {
        BindGroups = bindGroups;
        ShaderStages = shaderStages;
        RasterizerState = rasterizerState;
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        VertexInputLayouts = vertexInputLayouts;
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
        PushConstantsRanges = pushConstantsRanges;
        Name = name;
    }

    public GPUBindGroup[] BindGroups { get; init; }
    public ShaderStageSource[] ShaderStages { get; init; }
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public PrimitiveTopology PrimitiveTopology { get; init; } = PrimitiveTopology.TriangleList;
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.None;
    public PixelFormat[] ColorFormats { get; init; }
    public PixelFormat? DepthStencilFormat { get; init; }
    public PushConstantsRange[]? PushConstantsRanges { get; init; }
    public string Name { get; init; } = "unnamed_graphics_pipeline";
}