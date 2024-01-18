namespace Vocore.Graphics
{
    public abstract class GPUPipeline : BaseGPUObject
    {
        public abstract ShaderStage Stages { get; }
    }
}