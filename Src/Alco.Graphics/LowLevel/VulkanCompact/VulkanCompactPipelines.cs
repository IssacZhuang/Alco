// Vulkan 1.2 pipelines using shader modules and a shared descriptor-set layout.

using System.Runtime.InteropServices;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanCompactDevice
{
    /// <summary>Builds a vertex/pixel pipeline from compact SPIR-V stages.</summary>
    /// <param name="desc">The graphics pipeline description.</param>
    /// <returns>The graphics pipeline.</returns>
    public override GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        Vk.VkPipelineShaderStageCreateInfo* stages = stackalloc Vk.VkPipelineShaderStageCreateInfo[2];
        stages[0] = CreateStage(desc.Vertex, Vk.VkShaderStageFlags.Vertex);
        stages[1] = default;
        try
        {
            bool hasPixel = !desc.Pixel.Code.IsEmpty;
            if (hasPixel) stages[1] = CreateStage(desc.Pixel, Vk.VkShaderStageFlags.Fragment);
            return CreateRasterPso(this, stages, hasPixel ? 2u : 1u, null, desc.ColorTargets, desc.DepthFormat,
                desc.StencilFormat, desc.Rasterization, desc.Topology, false, desc.SampleCount);
        }
        finally
        {
            DestroyStage(stages[1]);
            DestroyStage(stages[0]);
        }
    }

    /// <summary>Rejects mesh pipelines, which the compact backend does not support.</summary>
    /// <param name="desc">The requested mesh pipeline.</param>
    /// <returns>No pipeline is created.</returns>
    public override GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc) =>
        throw new GPUException(Error.Unsupported, "vulkan-compact does not support mesh pipelines.");

    /// <summary>Builds a compute pipeline from a compact SPIR-V stage.</summary>
    /// <param name="compute">The compute shader stage.</param>
    /// <returns>The compute pipeline.</returns>
    public override GPUPipeline CreateComputePipeline(ShaderStage compute)
    {
        Vk.VkComputePipelineCreateInfo info = new()
        {
            stage = CreateStage(compute, Vk.VkShaderStageFlags.Compute),
            layout = ShaderLayout,
            basePipelineIndex = -1,
        };
        Vk.VkPipeline pipeline = default;
        try
        {
            Backend.CheckVk(Api.vkCreateComputePipelines(PipelineCache, 1, &info, null, &pipeline), "vkCreateComputePipelines");
            if (!PipelineCache.IsNull) Volatile.Write(ref PipelineCacheDirty, true);
            VulkanPso result = new() { Owner = this, Pipeline = pipeline, BindPoint = Vk.VkPipelineBindPoint.Compute };
            pipeline = default;
            return result;
        }
        finally
        {
            if (!pipeline.IsNull) Api.vkDestroyPipeline(pipeline, null);
            DestroyStage(info.stage);
        }
    }

    private Vk.VkPipelineShaderStageCreateInfo CreateStage(ShaderStage stage, Vk.VkShaderStageFlags kind)
    {
        AssertSpirv(stage.Code);
        fixed (byte* code = stage.Code)
        {
            if (stage.Code.Length < 5 * sizeof(uint) || ((uint*)code)[1] > 0x00010500)
                throw new GPUException(Error.Unsupported, "vulkan-compact requires Vulkan 1.2 SPIR-V (1.5 or earlier).");
            Vk.VkShaderModuleCreateInfo info = new() { codeSize = (nuint)stage.Code.Length, pCode = (uint*)code };
            Backend.CheckVk(Api.vkCreateShaderModule(&info, null, out Vk.VkShaderModule module), "vkCreateShaderModule");
            return new Vk.VkPipelineShaderStageCreateInfo
            {
                module = module,
                stage = kind,
                pName = Backend.AllocateUtf8(stage.EntryPoint),
            };
        }
    }

    private void DestroyStage(Vk.VkPipelineShaderStageCreateInfo stage)
    {
        if (!stage.module.IsNull) Api.vkDestroyShaderModule(stage.module, null);
        NativeMemory.Free(stage.pName);
    }
}
