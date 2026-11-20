// Vulkan recordings that use native descriptor heaps and mesh commands.

using System.Diagnostics;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanDevice
{
    internal override VulkanCommandBuffer AllocateCommands() => new VulkanHeapCommandBuffer { Owner = this };
}

internal sealed unsafe class VulkanHeapCommandBuffer : VulkanCommandBuffer
{
    internal override void PushAddress(uint offset, ulong address)
    {
        Vk.VkPushDataInfoEXT info = new()
        {
            offset = offset,
            data = new() { address = &address, size = sizeof(ulong) },
        };
        Owner.Api.vkCmdPushDataEXT(Handle, &info);
    }

    internal override void BindIndexRange(VulkanGpuHeapOwner buffer, GPURange indices, IndexType type)
    {
        if (!Owner.AddressCommands)
        {
            Owner.Api.vkCmdBindIndexBuffer2(Handle, buffer.Backing.Buffer, indices.Offset, indices.Size, (Vk.VkIndexType)type);
            return;
        }
        Vk.VkBindIndexBuffer3InfoKHR info = new()
        {
            addressRange = new()
            {
                address = buffer.Backing.Address + indices.Offset,
                size = indices.Size,
            },
            addressFlags = Backend.AddressFlags,
            indexType = (Vk.VkIndexType)type,
        };
        Owner.Api.vkCmdBindIndexBuffer3KHR(Handle, &info);
    }

    /// <summary>Binds the texture descriptor heap subsequent draws read bindless slots from.</summary>
    /// <param name="heap">The resource heap.</param>
    public override void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap)
    {
        VulkanTextureDescriptorHeap vulkanHeap = (VulkanTextureDescriptorHeap)heap;
        Debug.Assert(vulkanHeap != null && this.Owner == vulkanHeap.Owner);
        ref readonly Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT properties = ref this.Owner.HeapProperties;
        Vk.VkBindHeapInfoEXT bindInfo = new();
        MakeHeapBindInfo(
            vulkanHeap.Storage.Range.GPURange,
            properties.imageDescriptorAlignment > properties.bufferDescriptorAlignment
                ? properties.imageDescriptorAlignment
                : properties.bufferDescriptorAlignment,
            properties.minResourceHeapReservedRange,
            ref bindInfo);
        this.Owner.Api.vkCmdBindResourceHeapEXT(this.Handle, &bindInfo);
    }

    /// <summary>Binds the sampler descriptor heap subsequent draws read bindless slots from.</summary>
    /// <param name="heap">The resource heap.</param>
    public override void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap)
    {
        VulkanSamplerDescriptorHeap vulkanHeap = (VulkanSamplerDescriptorHeap)heap;
        Debug.Assert(vulkanHeap != null && this.Owner == vulkanHeap.Owner);
        ref readonly Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT properties = ref this.Owner.HeapProperties;
        Vk.VkBindHeapInfoEXT bindInfo = new();
        MakeHeapBindInfo(vulkanHeap.Storage.Range.GPURange, properties.samplerDescriptorAlignment, properties.minSamplerHeapReservedRange, ref bindInfo);
        this.Owner.Api.vkCmdBindSamplerHeapEXT(this.Handle, &bindInfo);
    }

    /// <summary>Records a task/mesh-shading draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="groupCount">Number of task workgroups, or mesh workgroups when no task stage is bound.</param>
    public override void DrawMeshlets(GPURange root, Extent3D groupCount)
    {
        Debug.Assert(this.Owner != null);
        BindRoot(this, root);
        this.Owner.Api.vkCmdDrawMeshTasksEXT(this.Handle, groupCount.X, groupCount.Y, groupCount.Z);
    }

    /// <summary>Records indirect mesh-shading draws; requires <see cref="DeviceCaps.IndirectMeshDraw"/>.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing indirect task/mesh draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public override void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(this.Owner != null);
        Debug.Assert(this.Owner.Capabilities.IndirectMeshDraw);
        BindRoot(this, root);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBuffer buffer = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Buffer;
            this.Owner.Api.vkCmdDrawMeshTasksIndirectEXT(
                this.Handle, buffer, arguments.Offset, drawCount, stride == 0 ? (uint)sizeof(Vk.VkDrawMeshTasksIndirectCommandEXT) : stride);
            return;
        }
        Vk.VkDrawIndirect2InfoKHR info = new()
        {
            addressRange = new()
            {
                address = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Address + arguments.Offset,
                size = arguments.Size,
                stride = stride == 0 ? (ulong)sizeof(Vk.VkDrawMeshTasksIndirectCommandEXT) : stride,
            },
            addressFlags = Backend.AddressFlags,
            drawCount = drawCount,
        };
        this.Owner.Api.vkCmdDrawMeshTasksIndirect2EXT(this.Handle, &info);
    }
}
