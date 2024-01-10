namespace Vocore.Graphics
{
    public abstract class GPUPipeline : BaseGPUObject
    {
        public abstract string Name { get; }
        public abstract IReadOnlyList<ShaderStage> Stages { get; }
        
    }
}