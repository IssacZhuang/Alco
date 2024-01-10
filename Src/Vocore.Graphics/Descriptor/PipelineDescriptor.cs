namespace Vocore.Graphics;

public struct PipelineDescriptor
{
    public PipelineDescriptor(
        ShaderStageSource[] shaderStages,
        RasterizerState rasterizerState,
        BlendState blendState,
        VertexInputLayout[] vertexInputLayouts,
        PixelFormat[] colorFormats,
        PixelFormat depthStencilFormat)
    {
        ShaderStages = shaderStages;
        RasterizerState = rasterizerState;
        BlendState = blendState;
        VertexInputLayouts = vertexInputLayouts;
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
    }


    public ShaderStageSource[] ShaderStages { get; init; }
    public VertexInputLayout[] VertexInputLayouts { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; } = DepthStencilState.DepthNone;
    public PixelFormat[] ColorFormats { get; init; }
    public PixelFormat DepthStencilFormat { get; init; }
}