namespace Alco.Graphics;

/// <summary>
/// Declares the depth-stencil attachment of a frame buffer layout: its format,
/// clear values and whether it is read-only during passes.
/// </summary>
public struct DepthAttachment
{
    /// <summary>
    /// Initializes the attachment with a format and depth/stencil clear values.
    /// </summary>
    public DepthAttachment(PixelFormat format, float clearDepth = 1.0f, uint clearStencil = 0)
    {
        Format = format;
        ClearDepth = clearDepth;
        ClearStencil = clearStencil;
        ReadOnly = false;
    }

    /// <summary>
    /// The texel format of the attachment.
    /// </summary>
    public PixelFormat Format { get; set; }
    /// <summary>
    /// The depth value the attachment is cleared to at pass start.
    /// </summary>
    public float ClearDepth { get; init; } = 1.0f;
    /// <summary>
    /// The stencil value the attachment is cleared to at pass start.
    /// </summary>
    public uint ClearStencil { get; init; } = 0;

    /// <summary>
    /// Whether render passes attach the depth-stencil attachment as read-only.
    /// Required when the same depth texture is sampled by the pass; such passes cannot
    /// clear depth/stencil and their pipelines must not write them.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>Determines whether two depth attachments have equal fields.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>True when both attachments are equal.</returns>
    public static bool operator ==(DepthAttachment left, DepthAttachment right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two depth attachments differ in any field.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>True when the attachments are not equal.</returns>
    public static bool operator !=(DepthAttachment left, DepthAttachment right)
    {
        return !(left == right);
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is DepthAttachment attachment && Equals(attachment);
    }

    /// <summary>Determines whether this attachment equals another in all fields.</summary>
    /// <param name="other">The attachment to compare with.</param>
    /// <returns>True when the attachments are equal.</returns>
    public readonly bool Equals(DepthAttachment other)
    {
        return Format == other.Format &&
        ClearDepth == other.ClearDepth &&
        ClearStencil == other.ClearStencil &&
        ReadOnly == other.ReadOnly;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(Format, ClearDepth, ClearStencil, ReadOnly);
    }
}