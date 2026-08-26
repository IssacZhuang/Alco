namespace Alco.Graphics;

/// <summary>A fixed-size set of GPU timestamp query slots.</summary>
public abstract class GPUTimestampQuerySet : BaseGPUObject
{
    /// <summary>Gets the number of timestamp slots.</summary>
    public uint Count { get; }

    /// <summary>Initializes a timestamp query set.</summary>
    /// <param name="count">The number of query slots.</param>
    /// <param name="name">The diagnostic resource name.</param>
    protected GPUTimestampQuerySet(uint count, string name) : base(name)
    {
        Count = count;
    }
}
