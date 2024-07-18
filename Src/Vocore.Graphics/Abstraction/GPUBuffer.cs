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


    /// <summary>
    /// Try to get the data from the GPU
    /// </summary>
    /// <param name="dest">The destination pointer of copying data</param>
    /// <param name="offset">The offset of the buffer</param>
    /// <param name="size">The size of the data</param>
    /// <returns><c>true</c> if the data is obtained successfully, <c>false</c> is usually returned in concurrent access</returns>
    public abstract void GetData(void* dest, uint offset, uint size);

    //variant

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetData(void* dest, uint size)
    {
        GetData(dest, 0, size);
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetData<T>(Span<T> dest, uint length) where T : unmanaged
    {
        fixed (void* ptr = dest)
        {
            GetData(ptr, 0, length * (uint)sizeof(T));
        }
    }

}