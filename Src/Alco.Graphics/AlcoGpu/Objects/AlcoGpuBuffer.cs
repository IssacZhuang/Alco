using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>A GPU memory block wrapping a native alco-gpu buffer.</summary>
internal sealed unsafe class AlcoGpuBuffer : GPUBuffer
{
    #region Properties
    private AlcoGPU.BufferHandle _buffer;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGPU.BufferHandle handle = _buffer;
            _buffer = AlcoGPU.BufferHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.BufferDestroy(handle);
                }
                finally
                {
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <summary>Gets the native buffer handle.</summary>
    public AlcoGPU.BufferHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    /// <summary>Creates a native buffer.</summary>
    public AlcoGpuBuffer(AlcoGpuDevice device, in BufferDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            {
                AlcoGPU.BufferDesc desc = new()
                {
                    Size = Size,
                    Usage = descriptor.Usage,
                    Name = ptrName,
                };

                AlcoGpuNative.BufferCreate(device.Native, in desc, out _buffer);
            }

        }
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(device);
        }
    }

    #endregion
}
