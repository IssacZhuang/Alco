namespace Vocore.Graphics;

public struct GraphicsPipelineDescriptor
{
    public GraphicsPipelineDescriptor(
        ResourceBindingLayout[] resourceLayouts,
        ShaderStageSource[] shaderStages,
        RasterizerState rasterizerState,
        BlendState blendState,
        VertexInputLayout[] vertexInputLayouts,
        PixelFormat[] colorFormats,
        PixelFormat depthStencilFormat,
        string name = "Unnamed Graphics Pipeline"
        )
    {
        ResourceLayouts = resourceLayouts;
        ShaderStages = shaderStages;
        RasterizerState = rasterizerState;
        BlendState = blendState;
        VertexInputLayouts = vertexInputLayouts;
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
        Name = name;
    }

    public ResourceBindingLayout[] ResourceLayouts { get; init; }
    public ShaderStageSource[] ShaderStages { get; init; }
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public PrimitiveTopology PrimitiveTopology { get; init; } = PrimitiveTopology.TriangleList;
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.DepthNone;
    public PixelFormat[] ColorFormats { get; init; }
    public PixelFormat DepthStencilFormat { get; init; }
    public string Name { get; init; } = "Unnamed Graphics Pipeline";
}