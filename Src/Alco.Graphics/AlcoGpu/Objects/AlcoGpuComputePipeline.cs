using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Owns a native compute pipeline created from managed shader and bind group descriptors.</summary>
internal sealed unsafe class AlcoGpuComputePipeline : GPUPipeline
{
    #region Properties
    private readonly AlcoHandle _native;
    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_native.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
        {
            AlcoGpuNative.PipelineDestroy(((AlcoGpuDevice)Device).Native, _native);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    /// <summary>Gets the native compute pipeline handle.</summary>
    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuComputePipeline(AlcoGpuDevice device, in ComputePipelineDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        AlcoHandle nativeDevice = device.Native;

        ReadOnlySpan<byte> entryPoint = descriptor.Source.EntryPoint.Utf8Z();
        ReadOnlySpan<byte> name = Name.Utf8Z();

        fixed (byte* ptrEntry = entryPoint)
        fixed (byte* ptrName = name)
        {
            AlcoHandle module = device.CreateShaderModule(descriptor.Source);

            GPUBindGroup[] bindGroups = descriptor.BindGroups;
            AlcoHandle* bindGroupLayouts = stackalloc AlcoHandle[bindGroups.Length];
            for (int i = 0; i < bindGroups.Length; i++)
            {
                bindGroupLayouts[i] = ((AlcoGpuBindGroup)bindGroups[i]).Native;
            }

            AlcoComputePipelineDesc desc = new()
            {
                BindGroupLayouts = bindGroupLayouts,
                BindGroupLayoutCount = (uint)bindGroups.Length,
                ComputeModule = module,
                ComputeEntry = ptrEntry,
                ImmediateSize = descriptor.PushConstantsSize,
                Name = ptrName,
            };

            try
            {
                AlcoGpuNative.ComputePipelineCreate(nativeDevice, in desc, out _native);
                GC.KeepAlive(bindGroups);
            }
            finally
            {
                device.DestroyShaderModule(module);
            }
        }
    }

    #endregion
}
