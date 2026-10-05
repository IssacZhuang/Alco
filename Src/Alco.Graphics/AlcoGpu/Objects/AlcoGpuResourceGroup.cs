using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// A bind group (the C# GPUResourceGroup concept): concrete resources bound
/// against a layout created from <see cref="AlcoGpuBindGroup"/>.
/// </summary>
internal sealed unsafe class AlcoGpuResourceGroup : GPUResourceGroup
{
    #region Properties
    private readonly AlcoHandle _native;
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
        if (!_native.IsNull)
        {
            uint status = AlcoGpuNative.BindGroupDestroy(((AlcoGpuDevice)Device).Native, _native);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuResourceGroup(AlcoGpuDevice device, in ResourceGroupDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        _resources = new IGPUBindableResource[descriptor.Resources.Length];
        for (int i = 0; i < descriptor.Resources.Length; i++)
        {
            _resources[i] = descriptor.Resources[i].Resource;
        }

        AlcoBindGroupEntry* entries = stackalloc AlcoBindGroupEntry[descriptor.Resources.Length];
        for (int i = 0; i < descriptor.Resources.Length; i++)
        {
            ResourceBindingEntry entry = descriptor.Resources[i];
            AlcoBindGroupEntry nativeEntry = new()
            {
                Binding = entry.Binding,
                Resource = default,
                Offset = 0,
                Size = 0,
            };

            switch (entry.Resource.ResourceType)
            {
                case BindableResourceType.Buffer:
                    AlcoGpuBuffer buffer = (AlcoGpuBuffer)entry.Resource;
                    nativeEntry.Resource = buffer.Native;
                    nativeEntry.Kind = 0;
                    if (entry.UseOffset)
                    {
                        nativeEntry.Offset = entry.Offset;
                        nativeEntry.Size = entry.Size;
                    }
                    else
                    {
                        nativeEntry.Offset = 0;
                        nativeEntry.Size = buffer.Size;
                    }
                    break;
                case BindableResourceType.Sampler:
                    AlcoGpuSampler sampler = (AlcoGpuSampler)entry.Resource;
                    nativeEntry.Resource = sampler.Native;
                    nativeEntry.Kind = 2;
                    break;
                case BindableResourceType.TextureView:
                    AlcoGpuTextureViewBase textureView = (AlcoGpuTextureViewBase)entry.Resource;
                    nativeEntry.Resource = textureView.Native;
                    nativeEntry.Kind = 1;
                    break;
            }

            entries[i] = nativeEntry;
        }

        ReadOnlySpan<byte> name = Name.Utf8Z();
        fixed (byte* ptrName = name)
        {
            AlcoBindGroupDesc nativeDescriptor = new()
            {
                Layout = ((AlcoGpuBindGroup)descriptor.Layout).Native,
                Entries = entries,
                EntryCount = (uint)descriptor.Resources.Length,
                Name = ptrName,
            };

            uint status = AlcoGpuNative.BindGroupCreate(device.Native, in nativeDescriptor, out _native);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    #endregion
}
