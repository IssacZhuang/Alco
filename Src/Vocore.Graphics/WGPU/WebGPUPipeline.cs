
using WebGPU;

namespace Vocore.Graphics;

public class WebGPUPipeline : GPUPipeline
{
    public override string Name => throw new NotImplementedException();

    public override IReadOnlyList<ShaderStage> Stages => throw new NotImplementedException();

    private readonly WGPURenderPipeline _pipeline;

    public WebGPUPipeline(in PipelineDescriptor descriptor)
    {

    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }
}