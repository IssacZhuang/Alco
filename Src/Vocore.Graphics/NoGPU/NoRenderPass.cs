
namespace Vocore.Graphics.NoGPU;

internal class NoRenderPass : GPURenderPass
{
    private static readonly NoFrameBuffer noFrameBuffer = new NoFrameBuffer();
    public override IReadOnlyList<ColorAttachment> Colors => Array.Empty<ColorAttachment>();

    public override DepthAttachment? Depth => null;

    public override string Name => "no_gpu_render_pass";

    public override GPUFrameBuffer CreateFrameBuffer(uint width, uint height, string? name = null)
    {
        return noFrameBuffer;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}