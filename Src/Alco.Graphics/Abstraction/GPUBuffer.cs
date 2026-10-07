using System.Runtime.CompilerServices;

namespace Alco.Graphics;


/// <summary>
/// A block of GPU memory for vertex, index, uniform, or storage data.
/// </summary>
public unsafe abstract class GPUBuffer : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// The size of the buffer
    /// </summary>
    public uint Size { get; }
    /// <summary>
    /// Gets the usage flags the buffer was created with; governs which operations (copy source/destination, binding, indexing) are legal on it.
    /// </summary>
    public BufferUsage Usage { get; }
    /// <summary>
    /// Gets the bindable-resource discriminator; always <see cref="BindableResourceType.Buffer"/>.
    /// </summary>
    public BindableResourceType ResourceType { get; }

    protected GPUBuffer(in BufferDescriptor descriptor): base(descriptor.Name)
    {
        Size = BufferUtility.GetAlignedBufferSize(descriptor.Size);
        Usage = descriptor.Usage;
        ResourceType = BindableResourceType.Buffer;
    }

}