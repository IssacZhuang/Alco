using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUComputePipeline : GPUPipeline
{
    #region Properties
    private readonly WGPUComputePipeline _native;
    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        wgpuComputePipelineRelease(_native);
    }

    #endregion

    #region WebGPU Implementation
    public WGPUComputePipeline Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public unsafe WebGPUComputePipeline(
        WebGPUDevice device,
        in ComputePipelineDescriptor descriptor): base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;

        ReadOnlySpan<byte> entryPoint = descriptor.Source.EntryPoint.GetUtf8Span();
        ReadOnlySpan<byte> name = Name.GetUtf8Span();

        fixed (byte* ptrEntry = entryPoint)
        fixed (byte* ptrName = name)
        {
            WGPUShaderModule module = nativeDevice.CreateShaderModule(descriptor.Source);

            WGPUProgrammableStageDescriptor stageDescriptor = new WGPUProgrammableStageDescriptor()
            {
                module = module,
                entryPoint = new WGPUStringView(ptrEntry, entryPoint.Length),
                constantCount = 0,
                constants = null
            };


            WGPUBindGroupLayout* bindGroupLayouts = stackalloc WGPUBindGroupLayout[descriptor.BindGroups.Length];

            for (int i = 0; i < descriptor.BindGroups.Length; i++)
            {
                bindGroupLayouts[i] = ((WebGPUBindGroup)descriptor.BindGroups[i]).Native;
            }

            WGPUPipelineLayoutDescriptor pipelineLayoutDescriptor = new WGPUPipelineLayoutDescriptor
            {
                nextInChain = null,
                label = new WGPUStringView(ptrName, name.Length),
                bindGroupLayoutCount = (uint)descriptor.BindGroups.Length,
                bindGroupLayouts = bindGroupLayouts,
            };

            WGPUPipelineLayout pipelineLayout = wgpuDeviceCreatePipelineLayout(nativeDevice, &pipelineLayoutDescriptor);

            WGPUComputePipelineDescriptor nativeDescriptor = new WGPUComputePipelineDescriptor()
            {
                layout = pipelineLayout,
                compute = stageDescriptor,
                label = new WGPUStringView(ptrName, name.Length),
                nextInChain = null
            };

            _native = wgpuDeviceCreateComputePipeline(nativeDevice, &nativeDescriptor);
        }
    }


    #endregion
}