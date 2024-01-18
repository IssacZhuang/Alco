namespace Vocore.Graphics;


public abstract class GPURenderPass : BaseGPUObject
{
    public abstract IReadOnlyList<ColorAttachment> Colors { get; }
    public abstract DepthAttachment? Depth { get; }
    public abstract GPUFrameBuffer CreateFrameBuffer(uint width, uint height, string? name = null);

    public bool AttachmentsEqual(GPURenderPass other)
    {
        if (Colors.Count != other.Colors.Count) return false;
        for (int i = 0; i < Colors.Count; i++)
        {
            if (Colors[i] != other.Colors[i]) return false;
        }
        if (Depth != other.Depth) return false;
        return true;
    }
}