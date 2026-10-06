using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// Describes the color and depth attachments of a render pass / frame buffer:
/// their formats and clear values.
/// </summary>
public abstract class GPUAttachmentLayout : BaseGPUObject
{
    private readonly ColorAttachment[] _colors;
    /// <summary>Gets the color attachments of the layout, in attachment-slot order.</summary>
    public ReadOnlySpan<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    /// <summary>Gets the depth/stencil attachment of the layout, or null when the layout has no depth attachment.</summary>
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

    /// <summary>
    /// Compares this layout's attachments with another layout's: same color attachments in order
    /// and an equal depth attachment.
    /// </summary>
    /// <param name="other">The layout to compare against.</param>
    /// <returns>Whether the two layouts have equal attachments.</returns>
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

    /// <summary>
    /// Computes a hash over the given attachments; consistent with attachment equality, so layouts
    /// with equal attachments hash equally.
    /// </summary>
    /// <param name="colors">The color attachments to hash.</param>
    /// <param name="depth">The optional depth attachment to hash.</param>
    /// <returns>The computed attachment hash.</returns>
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