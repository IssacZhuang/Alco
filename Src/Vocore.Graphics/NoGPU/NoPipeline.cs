namespace Vocore.Graphics.NoGPU;

internal class NoPipeline : GPUPipeline
{
    public override string Name => "no_gpu_pipeline";

    public override ShaderStage Stages => ShaderStage.None;

    protected override void Dispose(bool disposing)
    {
        
    }
}