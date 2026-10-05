using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuComputePipeline : GPUPipeline
{
    #region Properties
    private readonly AlcoHandle _native;
    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_native.IsNull)
        {
            AlcoGpuNative.PipelineDestroy(((AlcoGpuDevice)Device).Native, _native);
        }
    }

    #endregion

    #region AlcoGpu Implementation
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

            AlcoHandle* bindGroupLayouts = stackalloc AlcoHandle[descriptor.BindGroups.Length];
            for (int i = 0; i < descriptor.BindGroups.Length; i++)
            {
                bindGroupLayouts[i] = ((AlcoGpuBindGroup)descriptor.BindGroups[i]).Native;
            }

            AlcoComputePipelineDesc desc = new()
            {
                BindGroupLayouts = bindGroupLayouts,
                BindGroupLayoutCount = (uint)descriptor.BindGroups.Length,
                ComputeModule = module,
                ComputeEntry = ptrEntry,
                ImmediateSize = descriptor.PushConstantsSize,
                Name = ptrName,
            };

            try
            {
                AlcoGpuNative.ComputePipelineCreate(nativeDevice, in desc, out _native);
            }
            finally
            {
                device.DestroyShaderModule(module);
            }
        }
    }

    #endregion
}
