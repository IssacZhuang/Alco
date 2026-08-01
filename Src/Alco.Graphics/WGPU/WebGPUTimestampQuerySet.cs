using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed unsafe class WebGPUTimestampQuerySet : GPUTimestampQuerySet
{
    private readonly WGPUQuerySet _querySet;

    protected override GPUDevice Device { get; }

    /// <summary>Gets the native WebGPU query-set handle.</summary>
    public WGPUQuerySet Native => _querySet;

    /// <summary>Creates a native WebGPU timestamp query set.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="count">The timestamp slot count.</param>
    /// <param name="name">The diagnostic name.</param>
    public WebGPUTimestampQuerySet(WebGPUDevice device, uint count, string name) : base(count, name)
    {
        Device = device;
        ReadOnlySpan<byte> nameBytes = name.GetUtf8Span();
        fixed (byte* namePointer = nameBytes)
        {
            WGPUQuerySetDescriptor descriptor = new()
            {
                label = new WGPUStringView(namePointer, nameBytes.Length),
                type = WGPUQueryType.Timestamp,
                count = count,
            };
            _querySet = wgpuDeviceCreateQuerySet(device.Native, &descriptor);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_querySet == WGPUQuerySet.Null)
        {
            return;
        }
        wgpuQuerySetDestroy(_querySet);
        wgpuQuerySetRelease(_querySet);
    }
}
