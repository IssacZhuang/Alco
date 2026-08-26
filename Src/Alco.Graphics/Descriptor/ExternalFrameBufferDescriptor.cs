namespace Alco.Graphics;

/// <summary>
/// Represents the creation information for a GPU frame buffer composed of externally owned textures and views.
/// <br/> The created frame buffer does not take ownership of the textures and views;
/// the caller is responsible for their lifetime.
/// </summary>
public struct ExternalFrameBufferDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalFrameBufferDescriptor"/> struct.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout of the frame buffer.</param>
    /// <param name="colors">The externally owned color textures.</param>
    /// <param name="colorViews">The externally owned color texture views.</param>
    /// <param name="width">The width of the frame buffer.</param>
    /// <param name="height">The height of the frame buffer.</param>
    /// <param name="name">The name of the frame buffer.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExternalFrameBufferDescriptor(
        GPUAttachmentLayout attachmentLayout,
        GPUTexture[] colors,
        GPUTextureView[] colorViews,
        uint width,
        uint height,
        string name = "unnamed_external_frame_buffer")
    {
        AttachmentLayout = attachmentLayout;
        Colors = colors;
        ColorViews = colorViews;
        DepthStencil = null;
        DepthStencilView = null;
        DepthView = null;
        StencilView = null;
        Width = width;
        Height = height;
        Name = name;
    }

    /// <summary>
    /// The attachment layout of the frame buffer. The color count and formats must match <see cref="Colors"/>.
    /// </summary>
    public required GPUAttachmentLayout AttachmentLayout { get; init; }

    /// <summary>
    /// The externally owned color textures. The length must equal the color count of <see cref="AttachmentLayout"/>.
    /// </summary>
    public required GPUTexture[] Colors { get; init; }

    /// <summary>
    /// The externally owned color texture views used as the render pass color attachments.
    /// </summary>
    public required GPUTextureView[] ColorViews { get; init; }

    /// <summary>
    /// The externally owned depth stencil texture. Must be provided if and only if
    /// <see cref="AttachmentLayout"/> has a depth attachment.
    /// </summary>
    public GPUTexture? DepthStencil { get; init; }

    /// <summary>
    /// The depth stencil texture view used as the render pass depth attachment.
    /// Required when <see cref="DepthStencil"/> is set.
    /// </summary>
    public GPUTextureView? DepthStencilView { get; init; }

    /// <summary>
    /// The depth-only texture view, usually used for sampling.
    /// </summary>
    public GPUTextureView? DepthView { get; init; }

    /// <summary>
    /// The stencil-only texture view, usually used for sampling.
    /// </summary>
    public GPUTextureView? StencilView { get; init; }

    /// <summary>
    /// The width of the frame buffer. Every attachment texture must have this width.
    /// </summary>
    public required uint Width { get; init; }

    /// <summary>
    /// The height of the frame buffer. Every attachment texture must have this height.
    /// </summary>
    public required uint Height { get; init; }

    /// <summary>
    /// The name of the frame buffer.
    /// </summary>
    public string Name { get; init; } = "unnamed_external_frame_buffer";

    /// <summary>
    /// Validates the external frame buffer creation information.
    /// </summary>
    /// <exception cref="GraphicsException">The textures or views do not match the attachment layout or the frame buffer size.</exception>
    public readonly void Validate()
    {
        if (AttachmentLayout == null)
        {
            throw new GraphicsException("The attachment layout of the external frame buffer must be provided.");
        }
        if (Colors == null || ColorViews == null)
        {
            throw new GraphicsException("The color textures and views of the external frame buffer must be provided.");
        }
        if (Width == 0 || Height == 0)
        {
            throw new GraphicsException("The width and height of the external frame buffer must be greater than 0.");
        }
        if (Colors.Length != AttachmentLayout.Colors.Length || ColorViews.Length != Colors.Length)
        {
            throw new GraphicsException($"The color texture count ({Colors.Length}) and color view count ({ColorViews.Length}) must match the attachment layout color count ({AttachmentLayout.Colors.Length}).");
        }
        for (int i = 0; i < Colors.Length; i++)
        {
            GPUTexture texture = Colors[i];
            if (texture == null || ColorViews[i] == null)
            {
                throw new GraphicsException($"The color texture and view at index {i} must not be null.");
            }
            if (texture.Width != Width || texture.Height != Height)
            {
                throw new GraphicsException($"The color texture at index {i} ({texture.Width}x{texture.Height}) does not match the frame buffer size ({Width}x{Height}).");
            }
            if (texture.PixelFormat != AttachmentLayout.Colors[i].Format)
            {
                throw new GraphicsException($"The color texture format at index {i} ({texture.PixelFormat}) does not match the attachment layout format ({AttachmentLayout.Colors[i].Format}).");
            }
        }

        if ((DepthStencil != null) != AttachmentLayout.Depth.HasValue)
        {
            throw new GraphicsException("The depth stencil texture must be provided if and only if the attachment layout has a depth attachment.");
        }
        if (DepthStencil != null)
        {
            if (DepthStencilView == null)
            {
                throw new GraphicsException("The depth stencil view must be provided when a depth stencil texture is set.");
            }
            if (DepthStencil.Width != Width || DepthStencil.Height != Height)
            {
                throw new GraphicsException($"The depth stencil texture ({DepthStencil.Width}x{DepthStencil.Height}) does not match the frame buffer size ({Width}x{Height}).");
            }
            if (DepthStencil.PixelFormat != AttachmentLayout.Depth!.Value.Format)
            {
                throw new GraphicsException($"The depth stencil texture format ({DepthStencil.PixelFormat}) does not match the attachment layout format ({AttachmentLayout.Depth!.Value.Format}).");
            }
        }
    }
}
