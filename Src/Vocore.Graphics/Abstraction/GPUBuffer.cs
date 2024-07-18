using System.Runtime.CompilerServices;

namespace Vocore.Graphics;


/// <summary>
/// The buffer in the VRAM
/// </summary> 
public unsafe abstract class GPUBuffer : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// The size of the buffer
    /// </summary>
    /// <value></value>
    public abstract uint Size { get; }
    /// <summary>
    /// The usage of the buffer
    /// </summary> 
    public abstract BufferUsage Usage { get; }
    /// <summary>
    /// The type of the resource
    /// </summary>
    public abstract BindableResourceType ResourceType { get; }

}