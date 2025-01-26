using System.Runtime.CompilerServices;

namespace Alco.Graphics;


/// <summary>
/// The buffer in the VRAM
/// </summary> 
public unsafe abstract class GPUBuffer : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// The size of the buffer
    /// </summary>
    /// <value></value>
    public uint Size { get; }
    /// <summary>
    /// The usage of the buffer
    /// </summary> 
    public BufferUsage Usage { get; }
    /// <summary>
    /// The type of the resource
    /// </summary>
    public BindableResourceType ResourceType { get; }

    protected GPUBuffer(in BufferDescriptor descriptor): base(descriptor.Name)
    {
        Size = UtilsBuffer.GetAlignedBufferSize(descriptor.Size);
        Usage = descriptor.Usage;
        ResourceType = BindableResourceType.Buffer;
    }

}