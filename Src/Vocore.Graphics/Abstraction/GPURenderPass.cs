namespace Vocore.Graphics;

/// <summary>
/// The meta data to describe the color attachment.
/// </summary>
public abstract class GPURenderPass : BaseGPUObject
{
    public abstract ReadOnlySpan<ColorAttachment> Colors { get; }
    public abstract DepthAttachment? Depth { get; }

    public bool AttachmentsEqual(GPURenderPass other)
    {
        if (Colors.Length != other.Colors.Length) return false;
        for (int i = 0; i < Colors.Length; i++)
        {
            if (Colors[i] != other.Colors[i]) return false;
        }
        if (Depth != other.Depth) return false;
        return true;
    }
}