using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUBindGroup : GPUBindGroup
{
    #region Properties
    private readonly WGPUBindGroupLayout _native;
    private readonly BindGroupEntry[] _bindings;
    
    #endregion

    #region Abstract Implementation

    public override IReadOnlyList<BindGroupEntry> Bindings
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bindings;
    }

    protected override void Dispose(bool disposing)
    {
        wgpuBindGroupLayoutReference(_native);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBindGroupLayout Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    protected override GPUDevice Device { get; }

    public unsafe WebGPUBindGroup(WebGPUDevice device, BindGroupDescriptor descriptor):base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;
        BindGroupEntry[] entries = descriptor.Bindings;
        WGPUBindGroupLayoutEntry* nativeEntries = stackalloc WGPUBindGroupLayoutEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            nativeEntries[i] = entries[i].ConvertToWebGPU();
        }

        fixed (byte* ptrName = Name.GetUtf8Span())
        {
            WGPUBindGroupLayoutDescriptor nativeDescriptor = new WGPUBindGroupLayoutDescriptor()
            {
                entryCount = (uint)entries.Length,
                entries = nativeEntries,
                label = ptrName,
            };
            _native = wgpuDeviceCreateBindGroupLayout(nativeDevice, &nativeDescriptor);
        }


        _bindings = new BindGroupEntry[entries.Length];
        Array.Copy(entries, _bindings, entries.Length);
    }

    #endregion
}
