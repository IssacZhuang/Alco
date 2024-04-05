namespace Vocore.Graphics;

public struct FrameBufferDescriptor
{
    public FrameBufferDescriptor(
        GPURenderPass renderPass,
        uint width,
        uint height,
        string name = "unnamed_frame_buffer"
    )
    {
        RenderPass = renderPass;
        Width = width;
        Height = height;
        Name = name;
    }

    public GPURenderPass RenderPass { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public string Name { get; init; } = "unnamed_frame_buffer";
}