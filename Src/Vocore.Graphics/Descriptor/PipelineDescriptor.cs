namespace Vocore.Graphics;

public struct PipelineDescriptor
{
    public PipelineDescriptor(
        ShaderStageSource[] shaderStages,
        RasterizerState rasterizerState,
        BlendState blendState
    )
    {
        ShaderStages = shaderStages;
        RasterizerState = rasterizerState;
        BlendState = blendState;
    }

    public ShaderStageSource[] ShaderStages { get; init; }
    public RasterizerState RasterizerState { get; init; } = RasterizerState.CullNone;
    public BlendState BlendState { get; init; }

}