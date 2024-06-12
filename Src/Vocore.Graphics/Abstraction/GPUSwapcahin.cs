namespace Vocore.Graphics;

/// <summary>
/// The swapchain to present the rendered image to the screen.
/// </summary>
public abstract class GPUSwapchain : BaseGPUObject
{
    public abstract GPUFrameBuffer FrameBuffer { get; }
    public abstract bool IsVSyncEnabled { get; set; }
    public abstract void Resize(uint width, uint height);
    public abstract void Present();
}