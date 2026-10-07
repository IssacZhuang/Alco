namespace Alco.Graphics;

/// <summary>
/// The blend equation for one channel: how source and destination factors are
/// combined for a render target.
/// </summary>
public struct BlendComponent
{
    /// <summary>
    /// Initializes the component with its source and destination factors and the
    /// combine operation.
    /// </summary>
    public BlendComponent(BlendFactor source, BlendFactor destination, BlendOperation operation)
    {
        SrcFactor = source;
        DstFactor = destination;
        Operation = operation;
    }
    /// <summary>
    /// The factor the source color is multiplied by.
    /// </summary>
    public BlendFactor SrcFactor { get; set; }
    /// <summary>
    /// The factor the destination color is multiplied by.
    /// </summary>
    public BlendFactor DstFactor { get; set; }
    /// <summary>
    /// How the two scaled colors are combined.
    /// </summary>
    public BlendOperation Operation { get; set; }

    public static bool operator ==(BlendComponent left, BlendComponent right)
    {
        return left.SrcFactor == right.SrcFactor && left.DstFactor == right.DstFactor && left.Operation == right.Operation;
    }

    public static bool operator !=(BlendComponent left, BlendComponent right)
    {
        return left.SrcFactor != right.SrcFactor || left.DstFactor != right.DstFactor || left.Operation != right.Operation;
    }

    public override bool Equals(object? obj)
    {
        return obj is BlendComponent component && this == component;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + SrcFactor.GetHashCode();
        hash = hash * 31 + DstFactor.GetHashCode();
        hash = hash * 31 + Operation.GetHashCode();
        return hash;
    }
}