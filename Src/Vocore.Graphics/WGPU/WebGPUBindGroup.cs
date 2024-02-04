using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public class WebGPUBindGroup : GPUBindGroup
{
    #region Properties
    private readonly WGPUBindGroupLayout _native;
    private readonly BindGroupEntry[] _bindings;

    #endregion

    #region Abstract Implementation

    public override IReadOnlyList<BindGroupEntry> Bindings => _bindings;

    public override string Name => throw new NotImplementedException();

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBindGroupLayout Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public unsafe WebGPUBindGroup(WGPUDevice nativeDevice, BindGroupDescriptor descriptor)
    {
        BindGroupEntry[] entries = descriptor.Bindings;
        WGPUBindGroupLayoutEntry* nativeEntries = stackalloc WGPUBindGroupLayoutEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            nativeEntries[i] = entries[i].ConvertToWebGPU();
        }

        WGPUBindGroupLayoutDescriptor nativeDescriptor = new WGPUBindGroupLayoutDescriptor()
        {
            entryCount = (uint)entries.Length,
            entries = nativeEntries,
        };

        _native = wgpuDeviceCreateBindGroupLayout(nativeDevice, &nativeDescriptor);

        _bindings = new BindGroupEntry[entries.Length];
        Array.Copy(entries, _bindings, entries.Length);
    }

    #endregion
}
