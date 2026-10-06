namespace Alco.Graphics;

/// <summary>
/// Binds one resource to a binding slot of a bind group layout inside a
/// resource group.
/// </summary>
public struct ResourceBindingEntry
{
    /// <summary>
    /// Binds the whole resource to a binding slot.
    /// </summary>
    public ResourceBindingEntry(uint binding, IGPUBindableResource resource)
    {
        Binding = binding;
        Resource = resource;
        Offset = 0;
        Size = 0;
        UseOffset = false;
    }

    /// <summary>
    /// Binds a buffer to a binding slot with a sub-range. Note that this only
    /// records <paramref name="offset"/> and <paramref name="size"/>; the
    /// sub-range is not applied unless <see cref="UseOffset"/> is also set to
    /// true (the constructor leaves it false).
    /// </summary>
    /// <param name="binding">The binding slot.</param>
    /// <param name="resource">The buffer to bind.</param>
    /// <param name="offset">Byte offset of the bound sub-range within the buffer.</param>
    /// <param name="size">Size in bytes of the bound sub-range.</param>
    public ResourceBindingEntry(uint binding, GPUBuffer resource, uint offset, uint size)
    {
        Binding = binding;
        Resource = resource;
        Offset = offset;
        Size = size;
    }

    /// <summary>
    /// The binding slot of the layout this resource fills.
    /// </summary>
    public uint Binding { get; init; }
    /// <summary>
    /// The resource to bind (buffer, texture view or sampler).
    /// </summary>
    public IGPUBindableResource Resource { get; init; }
    /// <summary>
    /// Byte offset of the bound sub-range within the buffer; only used when
    /// <see cref="UseOffset"/> is true.
    /// </summary>
    public uint Offset { get; init; }
    /// <summary>
    /// Size in bytes of the bound sub-range; only used when
    /// <see cref="UseOffset"/> is true, otherwise the whole buffer is bound.
    /// </summary>
    public uint Size { get; init; }
    /// <summary>
    /// Whether the <see cref="Offset"/>/<see cref="Size"/> sub-range is bound
    /// instead of the whole buffer.
    /// </summary>
    public bool UseOffset { get ; init; }
}