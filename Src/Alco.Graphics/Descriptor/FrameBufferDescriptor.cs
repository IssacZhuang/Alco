namespace Alco.Graphics;

public struct FrameBufferDescriptor
{
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

    public GPUAttachmentLayout AttachmentLayout { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public string Name { get; init; } = "unnamed_frame_buffer";
}