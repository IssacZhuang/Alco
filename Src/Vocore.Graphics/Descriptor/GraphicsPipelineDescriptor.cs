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
        string name = "Unnamed Graphics Pipeline"
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
        Name = name;
    }

    public GPUBindGroup[] BindGroups { get; init; }
    public ShaderStageSource[] ShaderStages { get; init; }
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public PrimitiveTopology PrimitiveTopology { get; init; } = PrimitiveTopology.TriangleList;
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.DepthNone;
    public PixelFormat[] ColorFormats { get; init; }
    public PixelFormat? DepthStencilFormat { get; init; }
    public string Name { get; init; } = "Unnamed Graphics Pipeline";
}