
namespace Vocore.Graphics.NoGPU;

internal class NoRenderPass : GPURenderPass
{
    public override IReadOnlyList<ColorAttachment> Colors => Array.Empty<ColorAttachment>();

    public override DepthAttachment? Depth => null;

    public override string Name => "no_gpu_render_pass";

    protected override void Dispose(bool disposing)
    {
        
    }
}