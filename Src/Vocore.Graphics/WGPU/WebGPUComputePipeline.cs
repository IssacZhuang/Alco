using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUComputePipeline : GPUPipeline
{
    #region Properties
    private readonly WGPUComputePipeline _native;
    #endregion

    #region Abstract Implementation
    public override ShaderStage Stages
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ShaderStage.Compute;
    }

    public override string Name { get; }

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
        WGPUDevice nativeDevice,
        ComputePipelineDescriptor descriptor)
    {
        Name = descriptor.Name;

        fixed (sbyte* ptrEntry = descriptor.Source.EntryPoint.GetUtf8Span())
        fixed (sbyte* ptrName = Name.GetUtf8Span())
        {
            WGPUShaderModule module = nativeDevice.CreateShaderModule(descriptor.Source);

            WGPUProgrammableStageDescriptor stageDescriptor = new WGPUProgrammableStageDescriptor()
            {
                module = module,
                entryPoint = ptrEntry,
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
                label = null,
                bindGroupLayoutCount = (uint)descriptor.BindGroups.Length,
                bindGroupLayouts = bindGroupLayouts,
            };

            WGPUPipelineLayout pipelineLayout = wgpuDeviceCreatePipelineLayout(nativeDevice, &pipelineLayoutDescriptor);

            WGPUComputePipelineDescriptor nativeDescriptor = new WGPUComputePipelineDescriptor()
            {
                layout = pipelineLayout,
                compute = stageDescriptor,
                label = ptrName,
                nextInChain = null
            };

            _native = wgpuDeviceCreateComputePipeline(nativeDevice, &nativeDescriptor);
        }
    }


    #endregion
}