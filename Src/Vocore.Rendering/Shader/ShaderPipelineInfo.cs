using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The GPU pipeline with the data that might be used in recording the command buffer.
/// </summary>
public struct ShaderPipelineInfo
{
    public GPUPipeline Pipeline;
    public ShaderReflectionInfo ReflectionInfo;
    public ShaderStage PushConstantsStages;
    public int PushConstantsSize;
}
