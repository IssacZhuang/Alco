// Vulkan 1.2 push constants, descriptor sets, and index-buffer recording.

using System.Diagnostics;
using Vk = Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanCompactDevice
{
    internal override VulkanCommandBuffer AllocateCommands() => new VulkanCompactCommandBuffer { Owner = this };
}

internal sealed unsafe class VulkanCompactCommandBuffer : VulkanCommandBuffer
{
    internal override void PushAddress(uint offset, ulong address)
    {
        Owner.Api.vkCmdPushConstants(Handle, Owner.ShaderLayout, VulkanCompactDevice.BindingStages, offset, sizeof(ulong), &address);
    }

    internal override void BindIndexRange(VulkanGpuHeapOwner buffer, GPURange indices, IndexType type)
    {
        Owner.Api.vkCmdBindIndexBuffer(Handle, buffer.Backing.Buffer, indices.Offset, (Vk.VkIndexType)type);
    }

    /// <summary>Binds the texture descriptor heap for subsequent draws and dispatches.</summary>
    /// <param name="heap">The texture descriptor heap.</param>
    public override void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap)
    {
        VulkanCompactTextureDescriptorHeap textures = (VulkanCompactTextureDescriptorHeap)heap;
        Debug.Assert(textures.Owner == Owner);
        Vk.VkDescriptorSet* sets = stackalloc Vk.VkDescriptorSet[2] { textures.SampledSet, textures.StorageSet };
        BindSets(1, 2, sets);
    }

    /// <summary>Binds the sampler descriptor heap for subsequent draws and dispatches.</summary>
    /// <param name="heap">The sampler descriptor heap.</param>
    public override void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap)
    {
        VulkanCompactSamplerDescriptorHeap samplers = (VulkanCompactSamplerDescriptorHeap)heap;
        Debug.Assert(samplers.Owner == Owner);
        Vk.VkDescriptorSet set = samplers.Set;
        BindSets(3, 1, &set);
    }

    private void BindSets(uint firstSet, uint count, Vk.VkDescriptorSet* sets)
    {
        Debug.Assert(QueueIndex < Owner.Caps.GeneralQueueCount + Owner.Caps.ComputeQueueCount);
        if (QueueIndex < Owner.Caps.GeneralQueueCount)
            Owner.Api.vkCmdBindDescriptorSets(Handle, Vk.VkPipelineBindPoint.Graphics, Owner.ShaderLayout, firstSet, count, sets, 0, null);
        Owner.Api.vkCmdBindDescriptorSets(Handle, Vk.VkPipelineBindPoint.Compute, Owner.ShaderLayout, firstSet, count, sets, 0, null);
    }

    /// <summary>Rejects mesh draws, which the compact backend does not support.</summary>
    /// <param name="root">The requested root range.</param>
    /// <param name="groupCount">The requested workgroup count.</param>
    public override void DrawMeshlets(GPURange root, Extent3D groupCount) =>
        throw new GPUException(Error.Unsupported, "vulkan-compact does not support mesh shaders.");

    /// <summary>Rejects indirect mesh draws, which the compact backend does not support.</summary>
    /// <param name="root">The requested root range.</param>
    /// <param name="arguments">The requested argument records.</param>
    /// <param name="drawCount">The requested record count.</param>
    /// <param name="stride">The requested record stride.</param>
    public override void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0) =>
        throw new GPUException(Error.Unsupported, "vulkan-compact does not support mesh shaders.");
}
