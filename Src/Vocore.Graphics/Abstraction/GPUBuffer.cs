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
    /// [Thread Safe]Try to get the mapped data pointer
    /// </summary>
    /// <param name="ptr">The pointer of the mapped data</param>
    /// <returns><c>true</c> if the GPU data is mapped, <c>false</c> otherwise</returns>
    public abstract bool TryGetMappedDataPointer(out void* ptr);
    /// <summary>
    /// Try to map the buffer to the CPU
    /// </summary>
    /// <param name="offset">The offset of the buffer</param>
    /// <param name="size">The size of the buffer</param>
    /// <returns><c>false</c> if the buffer is already mapped or pending, <c>true</c> otherwise</returns>
    public abstract bool TryMapAsync(uint offset, uint size);

    /// <summary>
    /// Wait for the map completion
    /// </summary>
    public abstract void WaitForMapCompletion();

    /// <summary>
    /// Try to unmap the buffer
    /// </summary>
    /// <returns><c>false</c> if the buffer is not mapped or pending, <c>true</c> otherwise</returns>
    public abstract bool TryUnmap();

    /// <summary>
    /// [Thread Safe]Try to get the data from the GPU
    /// </summary>
    /// <param name="dest">The destination pointer of copying data</param>
    /// <param name="offset">The offset of the buffer</param>
    /// <param name="size">The size of the data</param>
    /// <returns><c>true</c> if the GPU data is mapped, <c>false</c> otherwise</returns>
    public bool TryGetData(void* dest, uint offset, uint size)
    {
        if (TryGetMappedDataPointer(out void* ptr))
        {
            Unsafe.CopyBlock(dest, (byte*)ptr + offset, size);
            return true;
        }
        return false;
    }

    /// <summary>
    /// [Thread Safe]Try to get the data from the GPU
    /// </summary>
    /// <param name="dest">The destination pointer of copying data</param>
    /// <param name="size">The size of the data</param>
    /// <returns><c>true</c> if the GPU data is mapped, <c>false</c> otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetData(void* dest, uint size)
    {
        return TryGetData(dest, 0, size);
    }


}