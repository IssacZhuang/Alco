using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuBuffer : GPUBuffer
{
    #region Properties
    private readonly AlcoHandle _buffer;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_buffer.IsNull)
        {
            AlcoGpuNative.BufferDestroy(((AlcoGpuDevice)Device).Native, _buffer);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public AlcoGpuBuffer(AlcoGpuDevice device, in BufferDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        ReadOnlySpan<byte> name = Name.Utf8Z();
        fixed (byte* ptrName = name)
        {
            AlcoBufferDesc desc = new()
            {
                Size = Size,
                Usage = (uint)descriptor.Usage,
                Name = ptrName,
            };

            AlcoGpuNative.BufferCreate(device.Native, in desc, out _buffer);
        }
    }

    #endregion
}
