using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

/// <summary>
/// The meta data to describe the color attachment.
/// </summary>
public abstract class GPURenderPass : BaseGPUObject
{
    private ColorAttachment[] _colors;
    public ReadOnlySpan<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }
    public DepthAttachment? Depth { get; }

    protected GPURenderPass(in RenderPassDescriptor descriptor)
    {
        _colors = new ColorAttachment[descriptor.Colors.Length];
        for (int i = 0; i < descriptor.Colors.Length; i++)
        {
            _colors[i] = descriptor.Colors[i];
        }

        Depth = descriptor.Depth;
    }

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