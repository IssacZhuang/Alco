using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed unsafe class WebGPUBuffer : GPUBuffer
{
    private const uint MAP_STATE_UNMAPPED = 0;
    private const uint MAP_STATE_PENDING = 1;
    private const uint MAP_STATE_MAPPED = 2;


    #region Properties
    private readonly WGPUBuffer _buffer;

    #endregion

    #region Abstract Implementation
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

    public unsafe WebGPUBuffer(WebGPUDevice device, in BufferDescriptor descriptor):base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;

        fixed (byte* name = descriptor.Name.GetUtf8Span())
        {
            WGPUBufferDescriptor bufferDescriptor = new()
            {
                nextInChain = null,
                label = name,
                size = Size,
                usage = UtilsWebGPU.ConvertBufferUsage(descriptor.Usage),
                mappedAtCreation = false,
            };

            _buffer = wgpuDeviceCreateBuffer(nativeDevice, &bufferDescriptor);
        }


    }


    #endregion

}