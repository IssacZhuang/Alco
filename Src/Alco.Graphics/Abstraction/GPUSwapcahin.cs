namespace Alco.Graphics;

/// <summary>
/// The swapchain to present the rendered image to the screen.
/// </summary>
public abstract class GPUSwapchain : BaseGPUObject
{
    /// <summary>
    /// The render target of the swapchain.
    /// </summary>
    public abstract GPUFrameBuffer FrameBuffer { get; }
    /// <summary>
    /// Enable or disable VSync
    /// </summary>
    public abstract bool IsVSyncEnabled { get; set; }
    /// <summary>
    /// Resize the swapchain. It actually resizes after the buffer is swapped
    /// </summary>
    /// <param name="width"> The new width of the swapchain </param>
    /// <param name="height"> The new height of the swapchain </param>
    public abstract void Resize(uint width, uint height);

    /// <summary>
    /// Request the surface texture, should be called in the beginning of the frame.
    /// It might be failed if the surface is not ready. 
    /// Do net render to the swapchain in this frame if it returns false.
    /// </summary>
    /// <returns> True if the texture is usable, false otherwise. </returns>
    public abstract bool RequestSurfaceTexture();

    /// <summary>
    /// Present the rendered image to the screen. Should be called in the end of the frame.
    /// </summary>
    public abstract void Present();

    protected GPUSwapchain(in SwapchainDescriptor descriptor): base(descriptor.Name)
    {
    }
}