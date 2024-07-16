using System.Runtime.CompilerServices;

namespace Vocore.Graphics;


/// <summary>
/// The buffer in the VRAM
/// </summary> 
public abstract class GPUBuffer: BaseGPUObject, IGPUBindableResource
{
    public delegate void AsyncReadBufferCallback(Span<byte> data);

    public abstract uint Size { get; }
    public abstract BufferUsage Usage { get; }
    public abstract BindableResourceType ResourceType { get; }

    public abstract Span<byte> GetData(uint offset, uint size);
    public abstract void GetDataAsync(uint offset, uint size, AsyncReadBufferCallback callback);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetData()
    {
        return GetData(0, Size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetDataAsync(AsyncReadBufferCallback callback)
    {
        GetDataAsync(0, Size, callback);
    }
}