// Vulkan graphics pipelines and heaps using native descriptor-heap bindings.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanDevice
{
    private static void BufferMappings(Vk.VkDescriptorSetAndBindingMappingEXT* mappings)
    {
        mappings[0] = new Vk.VkDescriptorSetAndBindingMappingEXT
        {
            bindingCount = 1,
            resourceMask = Vk.VkSpirvResourceTypeFlagsEXT.UniformBuffer | Vk.VkSpirvResourceTypeFlagsEXT.ReadOnlyStorageBuffer,
            source = Vk.VkDescriptorMappingSourceEXT.PushAddress,
        };
        mappings[1] = new Vk.VkDescriptorSetAndBindingMappingEXT
        {
            firstBinding = 3,
            bindingCount = 1,
            resourceMask = Vk.VkSpirvResourceTypeFlagsEXT.ReadOnlyStorageBuffer,
            source = Vk.VkDescriptorMappingSourceEXT.PushAddress,
            sourceData = new Vk.VkDescriptorMappingSourceDataEXT { pushAddressOffset = 8 },
        };
    }

    private static VulkanPso CreateHeapRasterPso(VulkanDevice device, ShaderStage firstStage, ShaderStage pixel, ReadOnlySpan<ColorTargetDesc> ColorTargets,
                                         Format DepthFormat, Format StencilFormat, RasterizationState rasterizationState,
                                         PrimitiveTopology topology, bool mesh, ShaderStage task, uint sampleCount)
    {
        Debug.Assert(device != null, "GPUPipeline creation called with a null device");
        AssertSpirv(firstStage.Code);
        if (task.Code.Length != 0) AssertSpirv(task.Code);
        if (pixel.Code.Length != 0) AssertSpirv(pixel.Code);

        fixed (byte* taskCode = task.Code, firstStageCode = firstStage.Code, pixelCode = pixel.Code)
        {
            byte* taskName = task.Code.Length != 0 ? Backend.AllocateUtf8(task.EntryPoint) : null;
            byte* firstStageName = Backend.AllocateUtf8(firstStage.EntryPoint);
            byte* pixelName = pixel.Code.Length != 0 ? Backend.AllocateUtf8(pixel.EntryPoint) : null;
            Vk.VkShaderModuleCreateInfo taskModuleInfo = new()
            {
                codeSize = (nuint)task.Code.Length,
                pCode = (uint*)taskCode,
            };
            Vk.VkShaderModuleCreateInfo firstStageModuleInfo = new()
            {
                codeSize = (nuint)firstStage.Code.Length,
                pCode = (uint*)firstStageCode,
            };
            Vk.VkShaderModuleCreateInfo pixelModuleInfo = new()
            {
                codeSize = (nuint)pixel.Code.Length,
                pCode = (uint*)pixelCode,
            };
            Vk.VkDescriptorSetAndBindingMappingEXT* mappings = stackalloc Vk.VkDescriptorSetAndBindingMappingEXT[2];
            BufferMappings(mappings);
            Vk.VkShaderDescriptorSetAndBindingMappingInfoEXT taskMapping = new() { pNext = &taskModuleInfo, mappingCount = 2, pMappings = mappings };
            Vk.VkShaderDescriptorSetAndBindingMappingInfoEXT firstStageMapping = new()
            {
                pNext = &firstStageModuleInfo,
                mappingCount = 2,
                pMappings = mappings,
            };
            Vk.VkShaderDescriptorSetAndBindingMappingInfoEXT pixelMapping = new() { pNext = &pixelModuleInfo, mappingCount = 2, pMappings = mappings };
            Vk.VkPipelineShaderStageCreateInfo* stages = stackalloc Vk.VkPipelineShaderStageCreateInfo[3];
            stages[0] = new Vk.VkPipelineShaderStageCreateInfo
            {
                pNext = &taskMapping,
                stage = Vk.VkShaderStageFlags.TaskEXT,
                pName = taskName,
            };
            stages[1] = new Vk.VkPipelineShaderStageCreateInfo
            {
                pNext = &firstStageMapping,
                stage = mesh ? Vk.VkShaderStageFlags.MeshEXT : Vk.VkShaderStageFlags.Vertex,
                pName = firstStageName,
            };
            stages[2] = new Vk.VkPipelineShaderStageCreateInfo
            {
                pNext = &pixelMapping,
                stage = Vk.VkShaderStageFlags.Fragment,
                pName = pixelName,
            };
            Vk.VkPipelineCreateFlags2CreateInfo flagsInfo = new() { flags = Vk.VkPipelineCreateFlags2.DescriptorHeapEXT };
            try
            {
                return CreateRasterPso(device, task.Code.Length != 0 ? &stages[0] : &stages[1],
                    1u + (task.Code.Length != 0 ? 1u : 0u) + (pixel.Code.Length != 0 ? 1u : 0u), &flagsInfo,
                    ColorTargets, DepthFormat, StencilFormat, rasterizationState, topology, mesh, sampleCount);
            }
            finally
            {
                NativeMemory.Free(taskName);
                NativeMemory.Free(firstStageName);
                NativeMemory.Free(pixelName);
            }
        }
    }

    /// <summary>Creates a bindless texture descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The texture descriptor heap.</returns>
    public override GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity)
    {
        Debug.Assert(capacity != 0);
        return new VulkanTextureDescriptorHeap
        {
            Owner = this,
            Capacity = capacity,
            Descriptors = new byte[(nint)((ulong)capacity * this.HeapProperties.imageDescriptorSize)],
            Storage = this.AllocateDescriptorHeap((ulong)capacity * this.HeapProperties.imageDescriptorSize, DescriptorHeapType.Texture),
        };
    }

    /// <summary>Creates a sampler descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The sampler descriptor heap.</returns>
    public override GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity)
    {
        Debug.Assert(capacity != 0);
        return new VulkanSamplerDescriptorHeap
        {
            Owner = this,
            Capacity = capacity,
            Descriptors = new byte[(nint)((ulong)capacity * this.HeapProperties.samplerDescriptorSize)],
            Storage = this.AllocateDescriptorHeap((ulong)capacity * this.HeapProperties.samplerDescriptorSize, DescriptorHeapType.Sampler),
        };
    }

    /// <summary>Builds a vertex/pixel pipeline.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <returns>The graphics pipeline.</returns>
    public override GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        return CreateHeapRasterPso(this, desc.Vertex, desc.Pixel, desc.ColorTargets, desc.DepthFormat,
                                 desc.StencilFormat, desc.Rasterization, desc.Topology, false, default, desc.SampleCount);
    }

    /// <summary>Builds a task/mesh/pixel pipeline.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <returns>The mesh pipeline.</returns>
    public override GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc)
    {
        return CreateHeapRasterPso(this, desc.Mesh, desc.Pixel, desc.ColorTargets, desc.DepthFormat,
                                 desc.StencilFormat, desc.Rasterization, default, true, desc.Task, desc.SampleCount);
    }

    /// <summary>Builds a compute pipeline.</summary>
    /// <param name="compute">The compute shader stage.</param>
    /// <returns>The compute pipeline.</returns>
    public override GPUPipeline CreateComputePipeline(ShaderStage compute)
    {
        AssertSpirv(compute.Code);

        fixed (byte* computeCode = compute.Code)
        {
            byte* entryPoint = Backend.AllocateUtf8(compute.EntryPoint);
            Vk.VkShaderModuleCreateInfo moduleInfo = new()
            {
                codeSize = (nuint)compute.Code.Length,
                pCode = (uint*)computeCode,
            };
            Vk.VkDescriptorSetAndBindingMappingEXT* mappings = stackalloc Vk.VkDescriptorSetAndBindingMappingEXT[2];
            BufferMappings(mappings);
            Vk.VkShaderDescriptorSetAndBindingMappingInfoEXT mappingInfo = new() { pNext = &moduleInfo, mappingCount = 2, pMappings = mappings };
            Vk.VkPipelineShaderStageCreateInfo stage = new()
            {
                pNext = &mappingInfo,
                stage = Vk.VkShaderStageFlags.Compute,
                pName = entryPoint,
            };
            Vk.VkPipelineCreateFlags2CreateInfo flagsInfo = new()
            {
                flags = Vk.VkPipelineCreateFlags2.DescriptorHeapEXT,
            };
            Vk.VkComputePipelineCreateInfo psoInfo = new()
            {
                pNext = &flagsInfo,
                stage = stage,
                basePipelineIndex = -1,
            };
            VulkanPso result = new()
            {
                Owner = this,
                BindPoint = Vk.VkPipelineBindPoint.Compute,
            };
            Vk.VkPipeline pipeline = default;
            try
            {
                Backend.CheckVk(this.Api.vkCreateComputePipelines(this.PipelineCache, 1, &psoInfo, null, &pipeline), "vkCreateComputePipelines");
                if (!this.PipelineCache.IsNull) Volatile.Write(ref this.PipelineCacheDirty, true);
                result.Pipeline = pipeline;
                pipeline = default;
                return result;
            }
            finally
            {
                if (!pipeline.IsNull) this.Api.vkDestroyPipeline(pipeline, null);
                NativeMemory.Free(entryPoint);
            }
        }
    }
}
