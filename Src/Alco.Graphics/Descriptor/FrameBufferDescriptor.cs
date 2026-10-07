namespace Alco.Graphics;

/// <summary>
/// Represents the creation information for a GPU frame buffer that owns its
/// attachments: the device creates one texture per attachment of the layout and
/// the frame buffer disposes them with itself. Use
/// <see cref="ExternalFrameBufferDescriptor"/> to wrap externally owned textures
/// instead.
/// </summary>
public struct FrameBufferDescriptor
{
    /// <summary>
    /// Initializes the descriptor with an attachment layout and the frame buffer size.
    /// </summary>
    public FrameBufferDescriptor(
        GPUAttachmentLayout attachmentLayout,
        uint width,
        uint height,
        string name = "unnamed_frame_buffer"
    )
    {
        AttachmentLayout = attachmentLayout;
        Width = width;
        Height = height;
        Name = name;
    }

    /// <summary>
    /// The attachment layout of the frame buffer; its attachments define the
    /// textures the device creates.
    /// </summary>
    public GPUAttachmentLayout AttachmentLayout { get; init; }
    /// <summary>
    /// The width of the frame buffer; every created attachment has this width.
    /// </summary>
    public uint Width { get; init; }
    /// <summary>
    /// The height of the frame buffer; every created attachment has this height.
    /// </summary>
    public uint Height { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_frame_buffer";
}