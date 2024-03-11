
namespace Vocore.Graphics.NoGPU;

internal class NoFrameBuffer : GPUFrameBuffer
{
    private static readonly GPURenderPass noRenderPass = new NoRenderPass();
    public override string Name => "no_gpu_frame_buffer";

    public override GPURenderPass RenderPass => noRenderPass;

    public override IReadOnlyList<GPUTexture> Colors => Array.Empty<GPUTexture>();

    public override GPUTexture? Depth => null;

    public override uint Width => 0;

    public override uint Height => 0;

    protected override void Dispose(bool disposing)
    {
        
    }
}