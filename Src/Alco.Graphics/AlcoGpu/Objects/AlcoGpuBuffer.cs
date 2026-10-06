using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuBuffer.</summary>
internal sealed unsafe class AlcoGpuBuffer : GPUBuffer
{
    #region Properties
    private AlcoBufferHandle _buffer;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoBufferHandle handle = _buffer;
            _buffer = AlcoBufferHandle.Null;
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

    /// <summary>Gets or stores the native ABI value.</summary>
    public AlcoBufferHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    /// <summary>Provides the AlcoGpuBuffer operation.</summary>
    public AlcoGpuBuffer(AlcoGpuDevice device, in BufferDescriptor descriptor) : base(descriptor)
    {
        try
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
