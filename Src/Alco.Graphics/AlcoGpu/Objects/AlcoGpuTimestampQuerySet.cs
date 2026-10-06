using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuTimestampQuerySet.</summary>
internal sealed unsafe class AlcoGpuTimestampQuerySet : GPUTimestampQuerySet
{
    private AlcoQuerySetHandle _querySet;

    protected override GPUDevice Device { get; }

    /// <summary>Gets the native query-set handle.</summary>
    public AlcoQuerySetHandle Native => _querySet;

    /// <summary>Creates a native timestamp query set.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="count">The timestamp slot count.</param>
    /// <param name="name">The diagnostic name.</param>
    public AlcoGpuTimestampQuerySet(AlcoGpuDevice device, uint count, string name) : base(count, name)
    {
        try
        {
            Device = device;

            ReadOnlySpan<byte> nameBytes = name.Utf8Z();
            fixed (byte* namePointer = nameBytes)
            {
                AlcoGpuNative.QuerySetCreate(device.Native, count, namePointer, out _querySet);
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

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoQuerySetHandle handle = _querySet;
            _querySet = AlcoQuerySetHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.QuerySetDestroy(handle);
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
}
