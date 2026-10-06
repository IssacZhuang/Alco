using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Owns a native compute pipeline created from managed shader and bind group descriptors.</summary>
internal sealed unsafe class AlcoGpuComputePipeline : GPUPipeline
{
    #region Properties
    private AlcoComputePipelineHandle _native;
    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoComputePipelineHandle handle = _native;
            _native = AlcoComputePipelineHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.ComputePipelineDestroy(handle);
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
    /// <summary>Gets the native compute pipeline handle.</summary>
    public AlcoComputePipelineHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuComputePipeline(AlcoGpuDevice device, in ComputePipelineDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;
            AlcoDeviceHandle nativeDevice = device.Native;

            ReadOnlySpan<byte> entryPoint = descriptor.Source.EntryPoint.Utf8Z();
            ReadOnlySpan<byte> name = Name.Utf8Z();

            fixed (byte* ptrEntry = entryPoint)
            fixed (byte* ptrName = name)
            {
                AlcoShaderModuleHandle module = device.CreateShaderModule(descriptor.Source);

                try
                {
                    GPUBindGroup[] bindGroups = descriptor.BindGroups;
                    Span<AlcoBindGroupLayoutHandle> bindGroupLayoutStorage = bindGroups.Length <= 64
                        ? stackalloc AlcoBindGroupLayoutHandle[bindGroups.Length] : new AlcoBindGroupLayoutHandle[bindGroups.Length];
                    for (int i = 0; i < bindGroups.Length; i++)
                    {
                        bindGroupLayoutStorage[i] = ((AlcoGpuBindGroup)bindGroups[i]).Native;
                    }

                    fixed (AlcoBindGroupLayoutHandle* bindGroupLayouts = bindGroupLayoutStorage)
                    {
                    AlcoComputePipelineDesc desc = new()
                    {
                        BindGroupLayouts = bindGroupLayouts,
                        BindGroupLayoutCount = (uint)bindGroups.Length,
                        ComputeModule = module,
                        ComputeEntry = ptrEntry,
                        ImmediateSize = descriptor.PushConstantsSize,
                        Name = ptrName,
                    };

                    AlcoGpuNative.ComputePipelineCreate(nativeDevice, in desc, out _native);
                    }
                }
                finally
                {
                    device.DestroyShaderModule(module);
                }
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
            GC.KeepAlive(descriptor.BindGroups);
        }
    }

    #endregion
}
