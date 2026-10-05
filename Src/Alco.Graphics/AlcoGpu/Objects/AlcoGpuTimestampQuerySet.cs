using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuTimestampQuerySet : GPUTimestampQuerySet
{
    private readonly AlcoHandle _querySet;

    protected override GPUDevice Device { get; }

    /// <summary>Gets the native query-set handle.</summary>
    public AlcoHandle Native => _querySet;

    /// <summary>Creates a native timestamp query set.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="count">The timestamp slot count.</param>
    /// <param name="name">The diagnostic name.</param>
    public AlcoGpuTimestampQuerySet(AlcoGpuDevice device, uint count, string name) : base(count, name)
    {
        Device = device;

        ReadOnlySpan<byte> nameBytes = name.Utf8Z();
        fixed (byte* namePointer = nameBytes)
        {
            AlcoGpuNative.QuerySetCreate(device.Native, count, namePointer, out _querySet);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_querySet.IsNull || !((AlcoGpuDevice)Device).IsNativeAlive)
        {
            return;
        }

        AlcoGpuNative.QuerySetDestroy(((AlcoGpuDevice)Device).Native, _querySet);
    }
}
