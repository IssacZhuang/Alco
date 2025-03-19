using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal unsafe sealed class WebGPUResourceGroup : GPUResourceGroup
{
    #region Properties
    private readonly WGPUBindGroup _native;
    private readonly IGPUBindableResource[] _resources;

    #endregion

    #region Abstract Implementation

    public override IReadOnlyList<IGPUBindableResource> Resources
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resources;
    }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        wgpuBindGroupRelease(_native);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUBindGroup Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public WebGPUResourceGroup(WebGPUDevice device, in ResourceGroupDescriptor descriptor): base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;
        _resources = new IGPUBindableResource[descriptor.Resources.Length];

        for (int i = 0; i < descriptor.Resources.Length; i++)
        {
            _resources[i] = descriptor.Resources[i].Resource;
        }

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
                    if (entry.UseOffset)
                    {
                        nativeEntry.offset = entry.Offset;
                        nativeEntry.size = entry.Size;
                    }
                    else
                    {
                        nativeEntry.offset = 0;
                        nativeEntry.size = buffer.Size;
                    }
                    break;
                case BindableResourceType.Sampler:
                    WebGPUSampler sampler = (WebGPUSampler)entry.Resource;
                    nativeEntry.sampler = sampler.Native;
                    break;
                case BindableResourceType.TextureView:
                    WebGPUTextureViewBase textureView = (WebGPUTextureViewBase)entry.Resource;
                    nativeEntry.textureView = textureView.Native;
                    break;
            }

            entries[i] = nativeEntry;
        }

        ReadOnlySpan<byte> name = Name.GetUtf8Span();
        fixed (byte* ptrName = name)
        {
            WGPUBindGroupDescriptor nativeDescriptor = new WGPUBindGroupDescriptor
            {
                entryCount = (uint)descriptor.Resources.Length,
                entries = entries,
                layout = ((WebGPUBindGroup)descriptor.Layout).Native,
                label = new WGPUStringView(ptrName, name.Length),
            };
            _native = wgpuDeviceCreateBindGroup(nativeDevice, &nativeDescriptor);
        }



    }


    #endregion


}