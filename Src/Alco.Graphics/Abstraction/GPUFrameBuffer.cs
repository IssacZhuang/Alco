namespace Alco.Graphics;

/// <surmmary>
/// The instance of the color attachments and depth attachment of a <see cref="GPURenderPass"/> 
/// <br/>Used as the render target of a shader
/// </surmmary>
public abstract class GPUFrameBuffer : BaseGPUObject
{
    public static readonly TextureUsage ColorAttachmentUsage =
    TextureUsage.ColorAttachment |
    TextureUsage.TextureBinding |
    TextureUsage.StorageBinding |
    TextureUsage.Write |
    TextureUsage.Read;
    public static readonly TextureUsage DepthAttachmentUsage =
    TextureUsage.ColorAttachment |
    TextureUsage.TextureBinding |
    TextureUsage.Read;

    //it might be a dynamic frame buffer so the width and height might be changed

    /// <summary>
    /// The metadata of the frame buffer which describes the color and depth attachments
    /// </summary>
    /// <value>The render pass of the frame buffer</value>
    public abstract GPURenderPass RenderPass { get; }
    /// <summary>
    /// The list of color textures of the frame buffer
    /// </summary>
    /// <value>The list of color textures of the frame buffer</value>
    public abstract ReadOnlySpan<GPUTexture> Colors { get; }
    /// <summary>
    /// The list of color texture views of the frame buffer
    /// </summary>
    /// <value>The list of color texture views of the frame buffer</value>
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
    /// <br/> [note] Not null only if the pixel format is the <see cref="PixelFormat.Depth24PlusStencil8"/> and <see cref="PixelFormat.Depth32FloatStencil8"/>
    /// </summary>
    /// <value>The stencil texture view of the frame buffer</value>
    public abstract GPUTextureView? StencilView { get; }

    /// <summary>
    /// The width of the frame buffer
    /// </summary>
    /// <value>The width of the frame buffer</value>
    public abstract uint Width { get; }
    /// <summary>
    /// The height of the frame buffer
    /// </summary>
    /// <value>The height of the frame buffer</value>
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