namespace Vocore.Graphics;

/// <surmmary>
/// The instance of the color attachments and depth attachment of a <see cref="GPURenderPass"/> 
/// <br/>Used as the render target of a shader
/// </surmmary>
public abstract class GPUFrameBuffer : BaseGPUObject
{
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
    /// The depth texture of the frame buffer
    /// </summary>
    /// <value>The depth texture of the frame buffer</value>
    public abstract GPUTexture? Depth { get; }
    /// <summary>
    /// The depth texture view of the frame buffer
    /// <br/> Not null if the frame buffer has a depth texture
    /// </summary>
    /// <value>The depth texture view of the frame buffer</value>
    public abstract GPUTextureView? DepthView { get; }
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
    }

    protected GPUFrameBuffer(string name): base(name)
    {
    }
}