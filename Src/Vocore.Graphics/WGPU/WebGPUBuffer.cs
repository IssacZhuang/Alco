using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUBuffer : GPUBuffer
{
    private const uint MAP_STATE_UNMAPPED = 0;
    private const uint MAP_STATE_PENDING = 1;
    private const uint MAP_STATE_MAPPED = 2;


    #region Properties
    private readonly WGPUBuffer _buffer;
    private readonly uint _size;
    private readonly BufferUsage _usage;
    private uint _mapState;

    #endregion

    #region Abstract Implementation
    public override uint Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size;
    }

    public override BufferUsage Usage
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _usage;
    }

    public override BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.Buffer;
    }

    public override string Name { get; }

    protected override GPUDevice Device { get; }

    public override unsafe void GetData(void* dest, uint offset, uint size)
    {
        if ((_usage & BufferUsage.MapRead) == 0)
        {
            throw new GraphicsException("The GPUBuffer must be created with BufferUsage.MapRead to read data from the GPU");
        }

        if (Interlocked.Exchange(ref _mapState, MAP_STATE_PENDING) == MAP_STATE_UNMAPPED)
        {
            GCHandle handle = GCHandle.Alloc(this);
            wgpuBufferMapAsync(_buffer, WGPUMapMode.Read, offset, size, &OnMapReadCallback, GCHandle.ToIntPtr(handle));
            WaitForMapComplete();
            void* data = (void*)wgpuBufferGetMappedRange(_buffer, offset, size);
            Unsafe.CopyBlock(dest, data, size);
            wgpuBufferUnmap(_buffer);
            return;
        }

        throw new GraphicsException("The GPUBuffer.GetData is not concurrent access safe");
    }

    protected override void Dispose(bool disposing)
    {
        WaitForMapComplete();
        wgpuBufferDestroy(_buffer);
        wgpuBufferRelease(_buffer);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public unsafe WebGPUBuffer(WebGPUDevice device, in BufferDescriptor descriptor)
    {
        Device = device;
        Name = descriptor.Name;

        WGPUDevice nativeDevice = device.Native;

        _size = UtilsBuffer.GetBufferSize(descriptor.Size);
        //_size = (uint)descriptor.Size;
        _usage = descriptor.Usage;
        
        fixed (sbyte* name = descriptor.Name.GetUtf8Span())
        {
            WGPUBufferDescriptor bufferDescriptor = new()
            {
                nextInChain = null,
                label = name,
                size = _size,
                usage = UtilsWebGPU.ConvertBufferUsage(descriptor.Usage),
                mappedAtCreation = false,
            };

            _buffer = wgpuDeviceCreateBuffer(nativeDevice, &bufferDescriptor);
        }

        _mapState = MAP_STATE_UNMAPPED;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetMapState(uint state)
    {
        Volatile.Write(ref _mapState, state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WaitForMapComplete()
    {
        while (Volatile.Read(ref _mapState) == MAP_STATE_PENDING) ;
    }

    #endregion

    [UnmanagedCallersOnly]
    private static void OnMapReadCallback(WGPUBufferMapAsyncStatus status, nint data)
    {
        GCHandle handle = GCHandle.FromIntPtr(data);

        WebGPUBuffer buffer = (WebGPUBuffer)handle.Target!;
        if (status == WGPUBufferMapAsyncStatus.Success)
        {
            buffer.SetMapState(MAP_STATE_MAPPED);
        }
        else
        {
            buffer.SetMapState(MAP_STATE_UNMAPPED);
            GraphicsLogger.Error($"Failed to map the buffer, Status: {status}");
        }

        handle.Free();
    }
}