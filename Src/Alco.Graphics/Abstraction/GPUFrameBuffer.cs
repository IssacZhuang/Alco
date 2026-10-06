namespace Alco.Graphics;

/// <summary>
/// The color/depth attachment views instantiated from a <see cref="GPUAttachmentLayout"/>; the target of a render pass.
/// </summary>
public abstract class GPUFrameBuffer : BaseGPUObject
{
    /// <summary>
    /// The usage flags a texture needs to serve as a frame buffer color attachment:
    /// render-target usage plus texture and storage binding and copy read/write.
    /// </summary>
    public static readonly TextureUsage ColorAttachmentUsage =
    TextureUsage.ColorAttachment |
    TextureUsage.TextureBinding |
    TextureUsage.StorageBinding |
    TextureUsage.Write |
    TextureUsage.Read;
    /// <summary>
    /// The usage flags a texture needs to serve as a frame buffer depth/stencil attachment:
    /// render-attachment usage plus texture binding and copy read/write.
    /// </summary>
    public static readonly TextureUsage DepthAttachmentUsage =
    TextureUsage.ColorAttachment |
    TextureUsage.TextureBinding |
    TextureUsage.Read |
    TextureUsage.Write;

    // May be a dynamic (surface) frame buffer: its width and height may change when the surface is resized or reconfigured.

    /// <summary>
    /// The metadata of the frame buffer which describes the color and depth attachments
    /// </summary>
    /// <value>The attachment layout of the frame buffer</value>
    public abstract GPUAttachmentLayout AttachmentLayout { get; }
    /// <summary>
    /// The list of color textures of the frame buffer
    /// </summary>
    public abstract ReadOnlySpan<GPUTexture> Colors { get; }
    /// <summary>
    /// The list of color texture views of the frame buffer
    /// </summary>
    public abstract ReadOnlySpan<GPUTextureView> ColorViews { get; }
    /// <summary>
    /// The depth stencil texture of the frame buffer
    /// </summary>
    /// <value>The depth texture of the frame buffer</value>
    public abstract GPUTexture? DepthStencil { get; }
    /// <summary>
    /// The depth stencil texture view of the frame buffer with aspect of depth and stencil. This view is usually used for depth attachment
    /// <br/> Not null if the frame buffer has a depth stencil texture
    /// </summary>
    /// <value>The depth stencil texture view of the frame buffer</value>
    public abstract GPUTextureView? DepthStencilView { get; }

    /// <summary>
    /// The depth texture view of the frame buffer with aspect of depth. This view is usually used for sampling
    /// <br/> [note] Not null if the frame buffer has a depth texture
    /// </summary>
    /// <value>The depth texture view of the frame buffer</value>
    public abstract GPUTextureView? DepthView { get; }

    /// <summary>
    /// The stencil texture view of the frame buffer with aspect of stencil. This view is usually used for sampling
    /// <br/> [note] Not null only if the pixel format is one of <see cref="PixelFormat.Depth24PlusStencil8"/> or <see cref="PixelFormat.Depth32FloatStencil8"/>
    /// </summary>
    /// <value>The stencil texture view of the frame buffer</value>
    public abstract GPUTextureView? StencilView { get; }

    /// <summary>
    /// The width of the frame buffer
    /// </summary>
    public abstract uint Width { get; }
    /// <summary>
    /// The height of the frame buffer
    /// </summary>
    public abstract uint Height { get; }

    protected GPUFrameBuffer(in FrameBufferDescriptor descriptor): base(descriptor.Name)
    {
        if (descriptor.Width <= 0)
        {
            throw new GraphicsException("The width of the frame buffer must be greater than 0");
        }
        if (descriptor.Height <= 0)
        {
            throw new GraphicsException("The height of the frame buffer must be greater than 0");
        }
    }

    protected GPUFrameBuffer(string name): base(name)
    {
    }
}