// Vulkan command lifecycle, descriptor copies, texture initialization, and shared recording helpers.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class VulkanBackend
{
    internal const uint VkRemainingMipLevels = ~0u;
    internal const uint VkRemainingArrayLayers = ~0u;
    internal const uint VkQueueFamilyIgnored = ~0u;

    private static ulong QueryTimelineValue(VulkanTimelineSemaphore semaphore)
    {
        ulong value;
        Backend.AssertVk(semaphore.Owner.Api.vkGetSemaphoreCounterValue(semaphore.Semaphore, &value));
        return value;
    }

    internal static void RecordImageBarriers(VulkanDeviceBase state, Vk.VkCommandBuffer CommandBuffer, ReadOnlySpan<Vk.VkImageMemoryBarrier2> barriers)
    {
        Debug.Assert(state != null && !CommandBuffer.IsNull && barriers.Length != 0);
        fixed (Vk.VkImageMemoryBarrier2* pointer = barriers)
        {
            Vk.VkDependencyInfo dependency = new()
            {
                imageMemoryBarrierCount = (uint)barriers.Length,
                pImageMemoryBarriers = pointer,
            };
            state.Dispatch.CmdPipelineBarrier2(CommandBuffer, &dependency);
        }
    }

    internal static void RecordBarrier(VulkanDeviceBase state, Vk.VkCommandBuffer commandBuffer, Stage before, Access beforeAccess,
                                       Stage after, Access afterAccess)
    {
        Vk.VkMemoryBarrier2 memoryBarrier = new()
        {
            srcStageMask = Backend.ToVk(before),
            srcAccessMask = Backend.ToVk(beforeAccess, state.DescriptorReadAccess),
            dstStageMask = Backend.ToVk(after),
            dstAccessMask = Backend.ToVk(afterAccess, state.DescriptorReadAccess),
        };
        Vk.VkDependencyInfo dependency = new()
        {
            memoryBarrierCount = 1,
            pMemoryBarriers = &memoryBarrier,
        };
        state.Dispatch.CmdPipelineBarrier2(commandBuffer, &dependency);
    }

    internal static void MakeHeapBindInfo(GPURange heap, ulong reservedAlignment, ulong reservedSize, ref Vk.VkBindHeapInfoEXT output)
    {
        ulong reservedOffset = Backend.AlignUp(heap.Size, reservedAlignment);
        output = new Vk.VkBindHeapInfoEXT
        {
            heapRange = new Vk.VkDeviceAddressRangeKHR
            {
                address = ((VulkanGpuHeapOwner)heap.Buffer!).Backing.Address + heap.Offset,
                size = reservedOffset + reservedSize,
            },
            reservedRangeOffset = reservedOffset,
            reservedRangeSize = reservedSize,
        };
    }

}

internal abstract unsafe partial class VulkanCommandBuffer
{
    /// <summary>Records a timestamp that is written to the first span element once the stage completes.</summary>
    /// <param name="destination">The destination resource or memory range.</param>
    /// <param name="stage">The pipeline stage.</param>
    public override void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands)
    {
        Debug.Assert(this.Owner != null);
        if (this.TimestampPool.IsNull) return;
        Debug.Assert(this.TimestampCount < this.Owner.TimestampQueryCount);
        // The span's memory must stay valid and stationary until readTimestamps; only stack, native, or pinned storage qualifies.
        Debug.Assert(!destination.IsEmpty);
        this.TimestampDestinations![this.TimestampCount] = (ulong*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
        this.Owner.Dispatch.CmdWriteTimestamp2(this.Handle, Backend.ToVk(stage), this.TimestampPool, this.TimestampCount++);
    }

    /// <summary>Initializes a newly placed texture for rendering use.</summary>
    /// <param name="texture">The texture resource.</param>
    public override void InitializeTexture(GPUTexture texture)
    {
        VulkanTexture vulkanTexture = (VulkanTexture)texture;
        Debug.Assert(vulkanTexture != null && this.Owner == vulkanTexture.Owner && vulkanTexture.Swapchain == null);
        Vk.VkImageMemoryBarrier2 imageBarrier = new()
        {
            // Join the caller's dependency without making unrelated image writes available again.
            srcStageMask = Vk.VkPipelineStageFlags2.AllCommands,
            dstStageMask = Vk.VkPipelineStageFlags2.AllCommands,
            dstAccessMask = Vk.VkAccessFlags2.MemoryRead | Vk.VkAccessFlags2.MemoryWrite,
            oldLayout = Vk.VkImageLayout.Undefined,
            newLayout = Vk.VkImageLayout.General,
            srcQueueFamilyIndex = VkQueueFamilyIgnored,
            dstQueueFamilyIndex = VkQueueFamilyIgnored,
            image = vulkanTexture.Image,
            subresourceRange = new Vk.VkImageSubresourceRange
            {
                aspectMask = Backend.ImageAspects(vulkanTexture.Format),
                levelCount = VkRemainingMipLevels,
                layerCount = VkRemainingArrayLayers,
            },
        };
        RecordImageBarriers(this.Owner, this.Handle, new ReadOnlySpan<Vk.VkImageMemoryBarrier2>(in imageBarrier));
    }

    /// <summary>Opens a named debug group nesting all commands until the matching end.</summary>
    /// <param name="name">The group name shown by GPU capture tools.</param>
    public override void BeginDebugGroup(ReadOnlySpan<char> name)
    {
        if (!this.Owner.DebugMarkers) return;
        int capacity = System.Text.Encoding.UTF8.GetMaxByteCount(name.Length) + 1;
        byte* utf8 = stackalloc byte[capacity];
        utf8[System.Text.Encoding.UTF8.GetBytes(name, new Span<byte>(utf8, capacity - 1))] = 0;
        Vk.VkDebugUtilsLabelEXT info = new() { pLabelName = utf8 };
        this.Owner.InstanceApi.vkCmdBeginDebugUtilsLabelEXT(this.Handle, &info);
    }

    /// <summary>Closes the innermost open debug group.</summary>
    public override void EndDebugGroup()
    {
        if (!this.Owner.DebugMarkers) return;
        this.Owner.InstanceApi.vkCmdEndDebugUtilsLabelEXT(this.Handle);
    }

    /// <summary>Inserts a standalone debug marker at the current position.</summary>
    /// <param name="name">The marker name shown by GPU capture tools.</param>
    public override void InsertDebugMarker(ReadOnlySpan<char> name)
    {
        if (!this.Owner.DebugMarkers) return;
        int capacity = System.Text.Encoding.UTF8.GetMaxByteCount(name.Length) + 1;
        byte* utf8 = stackalloc byte[capacity];
        utf8[System.Text.Encoding.UTF8.GetBytes(name, new Span<byte>(utf8, capacity - 1))] = 0;
        Vk.VkDebugUtilsLabelEXT info = new() { pLabelName = utf8 };
        this.Owner.InstanceApi.vkCmdInsertDebugUtilsLabelEXT(this.Handle, &info);
    }

    /// <summary>Finishes recording for submission through GPUDevice.Submit or GPUDevice.SubmitAndPresent.</summary>
    public override void EndCommands()
    {
        Backend.RequireVk(this.Owner.Api.vkEndCommandBuffer(this.Handle), "vkEndCommandBuffer");
    }
}

internal abstract unsafe partial class VulkanDeviceBase
{
    /// <summary>Size and placement alignment a texture occupies in a texture heap.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <returns>The required size and alignment, or zero if unsupported.</returns>
    public override SizeAlign GetTextureSizeAlign(TextureDesc desc)
    {
        if (!Backend.SupportsTextureSamples(in desc)) return default;
        PreparedTexture texture = new();
        PrepareTexture(this, in desc, ref texture);
        fixed (uint* queueFamilies = this.QueueFamilies)
        fixed (Vk.VkFormat* viewFormats = texture.ViewFormats)
        {
            texture.FormatList.pViewFormats = viewFormats;
            texture.ImageInfo.pQueueFamilyIndices = queueFamilies;
            texture.ImageInfo.pNext = texture.FormatList.viewFormatCount > 1 ? &texture.FormatList : null;
            if (!SupportsImageCreateInfo(this, in texture.ImageInfo, out _))
            {
                Gpu.ErrorCallback?.Invoke("The GPU cannot create the requested texture format, usage, extent, or sample count.", false);
                return default;
            }
            Vk.VkMemoryRequirements requirements = ImageMemoryRequirements(this, &texture.ImageInfo);
            return new SizeAlign { Size = requirements.size, Align = requirements.alignment };
        }
    }
}

internal sealed unsafe partial class VulkanTextureDescriptorHeap
{
    /// <summary>Copies heap slots into another heap.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination resource or memory range.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count)
    {
        VulkanTextureDescriptorHeap vulkanDestination = (VulkanTextureDescriptorHeap)destination;
        Debug.Assert(vulkanDestination != null && this.Owner == vulkanDestination.Owner);
        Debug.Assert(sourceIndex <= this.Capacity && count <= this.Capacity - sourceIndex);
        Debug.Assert(destinationIndex <= vulkanDestination.Capacity && count <= vulkanDestination.Capacity - destinationIndex);
        if (count == 0 || (this == vulkanDestination && sourceIndex == destinationIndex)) return;
        ulong stride = this.Owner.HeapProperties.imageDescriptorSize;
        int byteCount = (int)(count * stride);
        new Span<byte>(this.Descriptors, (int)(sourceIndex * stride), byteCount)
            .CopyTo(new Span<byte>(vulkanDestination.Descriptors, (int)(destinationIndex * stride), byteCount));
        fixed (byte* staging = vulkanDestination.Descriptors)
        {
            Backend.NativeCopy(vulkanDestination.Storage.Range.Cpu + destinationIndex * stride,
                                staging + sourceIndex * stride * 0 + destinationIndex * stride,
                                (nuint)byteCount);
        }
    }
}

internal sealed unsafe partial class VulkanSamplerDescriptorHeap
{
    /// <summary>Copies heap slots into another heap.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination resource or memory range.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count)
    {
        VulkanSamplerDescriptorHeap vulkanDestination = (VulkanSamplerDescriptorHeap)destination;
        Debug.Assert(vulkanDestination != null && this.Owner == vulkanDestination.Owner);
        Debug.Assert(sourceIndex <= this.Capacity && count <= this.Capacity - sourceIndex);
        Debug.Assert(destinationIndex <= vulkanDestination.Capacity && count <= vulkanDestination.Capacity - destinationIndex);
        if (count == 0 || (this == vulkanDestination && sourceIndex == destinationIndex)) return;
        ulong stride = this.Owner.HeapProperties.samplerDescriptorSize;
        int byteCount = (int)(count * stride);
        new Span<byte>(this.Descriptors, (int)(sourceIndex * stride), byteCount)
            .CopyTo(new Span<byte>(vulkanDestination.Descriptors, (int)(destinationIndex * stride), byteCount));
        fixed (byte* staging = vulkanDestination.Descriptors)
        {
            Backend.NativeCopy(vulkanDestination.Storage.Range.Cpu + destinationIndex * stride,
                                staging + destinationIndex * stride,
                                (nuint)byteCount);
        }
    }
}

internal sealed unsafe partial class VulkanCommandPool
{
    /// <summary>Recycles every command buffer while leaving acquired images owned by their swapchains.</summary>
    public override void Reset()
    {
        Debug.Assert(this.Owner != null && !this.Handle.IsNull);
        for (VulkanCommandBuffer? commands = this.First; commands != null; commands = commands.Next)
        {
            if (commands.Swapchain != null)
            {
                commands.Swapchain.AcquireCommands = null;
                commands.Swapchain = null;
            }
            commands.Submitted = false;
            commands.TimestampCount = 0;
        }
        Backend.AssertVk(this.Owner.Api.vkResetCommandPool(this.Handle, Vk.VkCommandPoolResetFlags.None));
        this.NextBuffer = this.First;
    }

    /// <summary>Collects completed timestamp results for the pool's command buffers.</summary>
    public override void ReadTimestamps()
    {
        Debug.Assert(this.Owner != null);
        for (VulkanCommandBuffer? commands = this.First; commands != null; commands = commands.Next)
        {
            if (!commands.Submitted || commands.TimestampCount == 0) continue;
            fixed (ulong* results = commands.TimestampResults)
            {
                Backend.AssertVk(this.Owner.Api.vkGetQueryPoolResults(commands.TimestampPool, 0, commands.TimestampCount,
                    (nuint)(sizeof(ulong) * commands.TimestampCount), results, sizeof(ulong), Vk.VkQueryResultFlags.Bit64));
            }
            for (uint index = 0; index < commands.TimestampCount; ++index)
                *commands.TimestampDestinations![index] = commands.TimestampResults![index];
            commands.TimestampCount = 0;
        }
    }

    /// <summary>Begins recording a primary command buffer; end it with <see cref="GPUCommandBuffer.EndCommands"/>.</summary>
    /// <returns>The recording command buffer.</returns>
    public override GPUCommandBuffer BeginCommands()
    {
        Debug.Assert(this.Owner != null && !this.Handle.IsNull);
        VulkanCommandBuffer? commands = this.NextBuffer;
        bool reused = commands != null;
        if (commands == null)
        {
            commands = this.Owner.AllocateCommands();
            commands.CommandPool = this.Handle;
            commands.QueueIndex = this.QueueIndex;
            if (this.Timestamps)
            {
                commands.TimestampDestinations = new ulong*[this.Owner.TimestampQueryCount];
                commands.TimestampResults = new ulong[this.Owner.TimestampQueryCount];
            }
        }
        Debug.Assert(commands.Swapchain == null);
        bool began = false;
        try
        {
            if (commands.Handle.IsNull)
            {
                Vk.VkCommandBufferAllocateInfo allocateInfo = new()
                {
                    commandPool = this.Handle,
                    level = Vk.VkCommandBufferLevel.Primary,
                    commandBufferCount = 1,
                };
                Vk.VkCommandBuffer allocatedBuffer;
                Backend.CheckVk(this.Owner.Api.vkAllocateCommandBuffers(&allocateInfo, &allocatedBuffer), "vkAllocateCommandBuffers");
                commands.Handle = allocatedBuffer;
            }
            if (this.Timestamps && commands.TimestampPool.IsNull)
            {
                Vk.VkQueryPoolCreateInfo queryInfo = new()
                {
                    queryType = Vk.VkQueryType.Timestamp,
                    queryCount = this.Owner.TimestampQueryCount,
                };
                Backend.CheckVk(this.Owner.Api.vkCreateQueryPool(&queryInfo, null, out commands.TimestampPool), "vkCreateQueryPool");
            }
            Vk.VkCommandBufferBeginInfo beginInfo = new()
            {
                flags = Vk.VkCommandBufferUsageFlags.OneTimeSubmit,
            };
            Backend.CheckVk(this.Owner.Api.vkBeginCommandBuffer(commands.Handle, &beginInfo), "vkBeginCommandBuffer");
            began = true;
        }
        finally
        {
            if (!began)
            {
                // A failed begin invalidates the native buffer; keep a reused node and its query pool available for retry.
                if (!commands.Handle.IsNull) this.Owner.Api.vkFreeCommandBuffers(this.Handle, commands.Handle);
                commands.Handle = default;
                if (!reused && !commands.TimestampPool.IsNull) this.Owner.Api.vkDestroyQueryPool(commands.TimestampPool, null);
            }
        }
        if (reused)
        {
            this.NextBuffer = commands.Next;
        }
        else
        {
            if (this.Last != null)
                this.Last.Next = commands;
            else
                this.First = commands;
            this.Last = commands;
        }
        commands.TimestampCount = 0;
        commands.Submitted = false;
        commands.HasIndexBinding = false;
        if (this.QueueIndex < this.Owner.Caps.GeneralQueueCount + this.Owner.Caps.ComputeQueueCount)
            commands.PushAddress(sizeof(ulong), this.Owner.BufferViewTable.Address);
        if (!commands.TimestampPool.IsNull)
            this.Owner.Api.vkResetQueryPool(commands.TimestampPool, 0, this.Owner.TimestampQueryCount);
        return commands;
    }
}
