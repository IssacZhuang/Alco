using System.Runtime.CompilerServices;

using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUResourceGroup : GPUResourceGroup
{
    #region Properties
    private readonly WGPUBindGroup _native;

    #endregion

    #region Abstract Implementation

    public override IReadOnlyList<IGPUBindableResource> Resources { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public override string Name => throw new NotImplementedException();

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBindGroup Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public WebGPUResourceGroup(WGPUDevice nativeDevice, ResourceGroupDescriptor descriptor)
    {
        WGPUBindGroupEntry* entries = stackalloc WGPUBindGroupEntry[descriptor.Resources.Length];
        for (int i = 0; i < descriptor.Resources.Length; i++)
        {
            ResourceBindingEntry entry = descriptor.Resources[i];
            WGPUBindGroupEntry nativeEntry = new WGPUBindGroupEntry
            {
                binding = entry.Binding,
            };

            switch (entry.Resource.ResourceType)
            {
                case BindableResourceType.Buffer:
                    WebGPUBuffer buffer = (WebGPUBuffer)entry.Resource;
                    nativeEntry.buffer = buffer.Native;
                    nativeEntry.offset = 0;
                    nativeEntry.size = buffer.Size;
                    break;
                case BindableResourceType.Sampler:
                    //TODO: Implement Sampler
                    break;
                case BindableResourceType.Texture:
                    //TODO: Implement Texture
                    break;
            }

            entries[i] = nativeEntry;
        }

        WGPUBindGroupDescriptor nativeDescriptor = new WGPUBindGroupDescriptor
        {
            entryCount = (uint)descriptor.Resources.Length,
            entries = entries,
        };

        _native = wgpuDeviceCreateBindGroup(nativeDevice, &nativeDescriptor);
    }


    #endregion


}