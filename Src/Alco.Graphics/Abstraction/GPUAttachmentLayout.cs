using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// The meta data to describe the GPU framebuffer.
/// </summary>
public abstract class GPUAttachmentLayout : BaseGPUObject
{
    private readonly ColorAttachment[] _colors;
    public ReadOnlySpan<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }
    public DepthAttachment? Depth { get; }

    protected GPUAttachmentLayout(in AttachmentLayoutDescriptor descriptor) : base(descriptor.Name)
    {
        _colors = new ColorAttachment[descriptor.Colors.Length];
        for (int i = 0; i < descriptor.Colors.Length; i++)
        {
            _colors[i] = descriptor.Colors[i];
        }

        Depth = descriptor.Depth;
    }

    public bool AttachmentsEqual(GPUAttachmentLayout other)
    {
        if (Colors.Length != other.Colors.Length) return false;
        for (int i = 0; i < Colors.Length; i++)
        {
            if (Colors[i] != other.Colors[i]) return false;
        }
        if (Depth != other.Depth) return false;
        return true;
    }

    public override int GetHashCode()
    {
        return GetAttachmentHash(Colors, Depth);
    }

    public override bool Equals(object? obj)
    {
        if (obj is GPUAttachmentLayout other)
        {
            return AttachmentsEqual(other);
        }
        return false;
    }

    public static int GetAttachmentHash(in ReadOnlySpan<ColorAttachment> colors, in DepthAttachment? depth)
    {
        int hash = 19;
        for (int i = 0; i < colors.Length; i++)
        {
            hash = hash * 37 + colors[i].GetHashCode();
        }
        if (depth != null)
        {
            hash = hash * 37 + depth.GetHashCode();
        }
        return hash;
    }
}