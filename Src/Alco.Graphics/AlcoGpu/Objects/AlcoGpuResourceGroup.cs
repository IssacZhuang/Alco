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
    private AlcoBindGroupHandle _native;
    private readonly IGPUBindableResource[] _resources;
    private readonly GPUBindGroup _layout;

    #endregion

    #region Abstract Implementation
    /// <inheritdoc />
    public override IReadOnlyList<IGPUBindableResource> Resources
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resources;
    }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoBindGroupHandle handle = _native;
            _native = AlcoBindGroupHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.BindGroupDestroy(handle);
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
    /// <summary>Gets the native resource bind group handle.</summary>
    public AlcoBindGroupHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuResourceGroup(AlcoGpuDevice device, in ResourceGroupDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;
            _layout = descriptor.Layout;

            _resources = new IGPUBindableResource[descriptor.Resources.Length];
            for (int i = 0; i < descriptor.Resources.Length; i++)
            {
                _resources[i] = descriptor.Resources[i].Resource;
            }

            Span<AlcoBindGroupEntry> entryStorage = descriptor.Resources.Length <= 32
                ? stackalloc AlcoBindGroupEntry[descriptor.Resources.Length] : new AlcoBindGroupEntry[descriptor.Resources.Length];
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
                        nativeEntry.Resource = buffer.Native.Value;
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
                        nativeEntry.Resource = sampler.Native.Value;
                        nativeEntry.Kind = 2;
                        break;
                    case BindableResourceType.TextureView:
                        AlcoGpuTextureViewBase textureView = (AlcoGpuTextureViewBase)entry.Resource;
                        nativeEntry.Resource = textureView.Native.Value;
                        nativeEntry.Kind = 1;
                        break;
                }

                entryStorage[i] = nativeEntry;
            }

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            fixed (AlcoBindGroupEntry* entries = entryStorage)
            {
                AlcoBindGroupDesc nativeDescriptor = new()
                {
                    Layout = ((AlcoGpuBindGroup)descriptor.Layout).Native,
                    Entries = entries,
                    EntryCount = (uint)descriptor.Resources.Length,
                    Name = ptrName,
                };

                AlcoGpuNative.BindGroupCreate(device.Native, in nativeDescriptor, out _native);
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
            GC.KeepAlive(descriptor.Layout);
        }
    }

    #endregion
}
