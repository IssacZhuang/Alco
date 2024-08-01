namespace Vocore.Graphics;

public struct BlendComponent
{
    public BlendComponent(BlendFactor source, BlendFactor destination, BlendOperation operation)
    {
        SrcFactor = source;
        DstFactor = destination;
        Operation = operation;
    }
    public BlendFactor SrcFactor { get; set; }
    public BlendFactor DstFactor { get; set; }
    public BlendOperation Operation { get; set; }

    //operator ==
    public static bool operator ==(BlendComponent left, BlendComponent right)
    {
        return left.SrcFactor == right.SrcFactor && left.DstFactor == right.DstFactor && left.Operation == right.Operation;
    }

    //operator !=
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
        return SrcFactor.GetHashCode() ^ DstFactor.GetHashCode() ^ Operation.GetHashCode();
    }
}