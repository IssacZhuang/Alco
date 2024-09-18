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

    protected override void Dispose(bool disposing)
    {
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

        fixed (byte* name = descriptor.Name.GetUtf8Span())
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


    }


    #endregion

}