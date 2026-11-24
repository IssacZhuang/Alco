// Vulkan command recording and allocation-free submission, including independently acquired swapchain waits.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class VulkanBackend
{
    internal static void SubmitCommands(VulkanDeviceBase device, SubmitDesc desc, uint queueIndex, VulkanSwapchain? present = null)
    {
        Debug.Assert(queueIndex < device.QueueCount);
        Queue queue = device.Queues![queueIndex];
        VulkanTimelineSemaphore? completion = (VulkanTimelineSemaphore?)desc.Completion.Semaphore;
        Debug.Assert(completion != null && completion.Owner == device);
        uint commandCount = (uint)desc.Commands.Length + (present != null ? 1u : 0u);
        uint waitCount = (uint)desc.Waits.Length;
        for (int index = 0; index < desc.Commands.Length; ++index)
        {
            VulkanCommandBuffer commands = (VulkanCommandBuffer)desc.Commands[index];
            Debug.Assert(commands.Owner == device && !commands.Submitted &&
                device.Queues[commands.QueueIndex].FamilyIndex == queue.FamilyIndex);
            if (commands.Swapchain == null)
                continue;
            Debug.Assert(queueIndex == 0 && commands.Swapchain.Owner == device && commands.Swapchain.Acquired &&
                commands.Swapchain.AcquireWaitPending && commands.Swapchain.AcquireCommands == commands);
            ++waitCount;
        }
        if (commandCount > queue.CommandSubmitCapacity)
        {
            queue.CommandSubmitCapacity = Math.Max(commandCount, Math.Max(4u, queue.CommandSubmitCapacity * 2));
            queue.CommandSubmitInfos = (Vk.VkCommandBufferSubmitInfo*)NativeMemory.Realloc(queue.CommandSubmitInfos,
                (nuint)queue.CommandSubmitCapacity * (nuint)sizeof(Vk.VkCommandBufferSubmitInfo));
        }
        if (waitCount > queue.WaitSubmitCapacity)
        {
            queue.WaitSubmitCapacity = Math.Max(waitCount, Math.Max(4u, queue.WaitSubmitCapacity * 2));
            queue.WaitSubmitInfos = (Vk.VkSemaphoreSubmitInfo*)NativeMemory.Realloc(queue.WaitSubmitInfos,
                (nuint)queue.WaitSubmitCapacity * (nuint)sizeof(Vk.VkSemaphoreSubmitInfo));
        }
        for (int index = 0; index < desc.Waits.Length; ++index)
        {
            TimelinePoint point = desc.Waits[index];
            VulkanTimelineSemaphore? semaphore = (VulkanTimelineSemaphore?)point.Semaphore;
            Debug.Assert(semaphore != null && semaphore.Owner == device);
            queue.WaitSubmitInfos[index] = new Vk.VkSemaphoreSubmitInfo
            {
                semaphore = semaphore.Semaphore,
                value = point.Value,
                stageMask = Vk.VkPipelineStageFlags2.AllCommands,
            };
        }
        uint waitIndex = (uint)desc.Waits.Length;
        for (int index = 0; index < desc.Commands.Length; ++index)
        {
            VulkanCommandBuffer commands = (VulkanCommandBuffer)desc.Commands[index];
            queue.CommandSubmitInfos[index] = new Vk.VkCommandBufferSubmitInfo { commandBuffer = commands.Handle, deviceMask = 1 };
            if (commands.Swapchain != null)
            {
                queue.WaitSubmitInfos[waitIndex++] = new Vk.VkSemaphoreSubmitInfo
                {
                    semaphore = commands.Swapchain.Context!.Acquired,
                    stageMask = Vk.VkPipelineStageFlags2.AllCommands,
                };
            }
        }
        Vk.VkSemaphoreSubmitInfo* signals = stackalloc Vk.VkSemaphoreSubmitInfo[3];
        signals[0] = new Vk.VkSemaphoreSubmitInfo
        {
            semaphore = completion.Semaphore,
            value = desc.Completion.Value,
            stageMask = Vk.VkPipelineStageFlags2.AllCommands,
        };
        uint signalCount = 1;
        if (present != null)
        {
            Debug.Assert(queueIndex == 0 && present.Owner == device && present.Context != null);
            queue.CommandSubmitInfos[desc.Commands.Length] = new Vk.VkCommandBufferSubmitInfo
            {
                commandBuffer = present.Context.Tail,
                deviceMask = 1,
            };
            signals[signalCount++] = new Vk.VkSemaphoreSubmitInfo
            {
                semaphore = present.Context.Rendered,
                stageMask = Vk.VkPipelineStageFlags2.AllCommands,
            };
            ulong value = present.NextRetirementValue();
            signals[signalCount++] = new Vk.VkSemaphoreSubmitInfo
            {
                semaphore = present.RenderRetirement,
                value = value,
                stageMask = Vk.VkPipelineStageFlags2.AllCommands,
            };
            present.Context.RetirementValue = value;
        }
        Vk.VkSubmitInfo2 submitInfo = new()
        {
            waitSemaphoreInfoCount = waitCount,
            pWaitSemaphoreInfos = queue.WaitSubmitInfos,
            commandBufferInfoCount = commandCount,
            pCommandBufferInfos = queue.CommandSubmitInfos,
            signalSemaphoreInfoCount = signalCount,
            pSignalSemaphoreInfos = signals,
        };
        Backend.RequireVk(device.Dispatch.QueueSubmit2(queue.Handle, 1, &submitInfo, default), "vkQueueSubmit2");
        for (int index = 0; index < desc.Commands.Length; ++index)
        {
            VulkanCommandBuffer commands = (VulkanCommandBuffer)desc.Commands[index];
            commands.Submitted = true;
            if (commands.Swapchain == null)
                continue;
            commands.Swapchain.AcquireWaitPending = false;
            commands.Swapchain.AcquireCommands = null;
            commands.Swapchain = null;
        }
    }

    internal static Vk.VkDeviceMemoryImageCopyKHR MakeTextureCopyRegion(VulkanTexture texture, TextureCopyDesc copy, GPURange memory)
    {
        uint mipWidth = texture.Width >> (int)copy.MipLevel;
        uint mipHeight = texture.Height >> (int)copy.MipLevel;
        uint mipDepth = texture.Depth >> (int)copy.MipLevel;
        if (mipWidth == 0) mipWidth = 1;
        if (mipHeight == 0) mipHeight = 1;
        if (mipDepth == 0) mipDepth = 1;
        Backend.AssertTextureCopyRegion(texture.Format, new Extent3D(mipWidth, mipHeight, mipDepth), copy.Offset, copy.Extent);

        uint width = copy.Extent.X == 0 ? mipWidth - copy.Offset.X : copy.Extent.X;
        TextureFormatInfo formatInfo = Gpu.GetTextureFormatInfo(texture.Format);
        ulong RowPitch = copy.RowPitchBytes == 0
                              ? Backend.DivideUp(width, formatInfo.BlockExtent.X) * formatInfo.BytesPerBlock
                              : copy.RowPitchBytes;
        Debug.Assert((copy.SlicePitchBytes == 0 || RowPitch != 0), "slice pitch conversion requires a non-zero row pitch");
        return new Vk.VkDeviceMemoryImageCopyKHR
        {
            addressRange = new()
            {
                address = ((VulkanGpuHeapOwner)memory.Buffer!).Backing.Address + memory.Offset,
                size = memory.Size,
            },
            addressFlags = Backend.AddressFlags,
            addressRowLength = (uint)(copy.RowPitchBytes / formatInfo.BytesPerBlock * formatInfo.BlockExtent.X),
            addressImageHeight = copy.SlicePitchBytes == 0 ? 0u : (uint)(copy.SlicePitchBytes / RowPitch * formatInfo.BlockExtent.Y),
            imageSubresource = new()
            {
                aspectMask = Backend.ImageAspects(texture.Format),
                mipLevel = copy.MipLevel,
                baseArrayLayer = copy.BaseSlice,
                layerCount = copy.SliceCount == 0 ? texture.LayerCount - copy.BaseSlice : copy.SliceCount,
            },
            imageLayout = Vk.VkImageLayout.General,
            imageOffset = new Vk.VkOffset3D((int)copy.Offset.X, (int)copy.Offset.Y, (int)copy.Offset.Z),
            imageExtent = new Vk.VkExtent3D(
                width,
                copy.Extent.Y == 0 ? mipHeight - copy.Offset.Y : copy.Extent.Y,
                copy.Extent.Z == 0 ? mipDepth - copy.Offset.Z : copy.Extent.Z),
        };
    }

    internal static Vk.VkBufferImageCopy2 BufferTextureCopyRegion(in Vk.VkDeviceMemoryImageCopyKHR region, ulong offset) => new()
    {
        bufferOffset = offset,
        bufferRowLength = region.addressRowLength,
        bufferImageHeight = region.addressImageHeight,
        imageSubresource = region.imageSubresource,
        imageOffset = region.imageOffset,
        imageExtent = region.imageExtent,
    };

    internal static void BindIndices(VulkanCommandBuffer commands, GPURange indices, IndexType type)
    {
        if (commands.HasIndexBinding && commands.BoundIndices.Buffer == indices.Buffer && commands.BoundIndices.Offset == indices.Offset &&
            commands.BoundIndices.Size == indices.Size && commands.BoundIndexType == type) return;
        VulkanGpuHeapOwner buffer = (VulkanGpuHeapOwner)indices.Buffer!;
        commands.BindIndexRange(buffer, indices, type);
        commands.BoundIndices = indices;
        commands.BoundIndexType = type;
        commands.HasIndexBinding = true;
    }

    internal static void BindRoot(VulkanCommandBuffer commands, GPURange root)
    {
        if (root.Buffer == null) return;
        Debug.Assert((root.Offset & 15) == 0);
        ulong address = ((VulkanGpuHeapOwner)root.Buffer).Backing.Address + root.Offset;
        commands.PushAddress(0, address);
    }

}

internal abstract unsafe partial class VulkanDeviceBase
{
    internal abstract VulkanCommandBuffer AllocateCommands();

    /// <summary>Submits recorded command buffers without presenting.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <param name="queueIndex">The queue index.</param>
    public override void Submit(SubmitDesc desc, uint queueIndex = 0)
    {
        Debug.Assert(queueIndex < this.QueueCount, "submit requires an available queue index");
        SubmitCommands(this, desc, queueIndex);
    }
}

internal abstract unsafe partial class VulkanCommandBuffer
{
    internal abstract void PushAddress(uint offset, ulong address);
    internal abstract void BindIndexRange(VulkanGpuHeapOwner buffer, GPURange indices, IndexType type);

    /// <summary>Binds a pipeline for subsequent draws and dispatches.</summary>
    /// <param name="pipeline">The pipeline to bind.</param>
    public override void BindPipeline(GPUPipeline pipeline)
    {
        VulkanPso vulkanPso = (VulkanPso)pipeline;
        Debug.Assert(vulkanPso != null);
        this.Owner.Api.vkCmdBindPipeline(this.Handle, vulkanPso.BindPoint, vulkanPso.Pipeline);
    }

    /// <summary>Sets the top-left framebuffer viewport for upward-positive clip Y.</summary>
    /// <param name="viewport">The viewport rectangle and depth range.</param>
    public override void SetViewport(Viewport viewport)
    {
        Vk.VkViewport vkViewport = new()
        {
            x = viewport.X,
            y = viewport.Y + viewport.Height,
            width = viewport.Width,
            height = -viewport.Height,
            minDepth = viewport.MinDepth,
            maxDepth = viewport.MaxDepth,
        };
        this.Owner.Dispatch.CmdSetViewportWithCount(this.Handle, 1, &vkViewport);
    }

    /// <summary>Sets the scissor rectangle.</summary>
    /// <param name="scissor">The scissor rectangle.</param>
    public override void SetScissor(Scissor scissor)
    {
        Vk.VkRect2D vkScissor = new(scissor.X, scissor.Y, scissor.Width, scissor.Height);
        this.Owner.Dispatch.CmdSetScissorWithCount(this.Handle, 1, &vkScissor);
    }

    /// <summary>Sets depth and stencil tests for the current render pass.</summary>
    /// <param name="state">The depth and stencil state.</param>
    public override void SetDepthStencil(DepthStencilState state)
    {

        this.Owner.Dispatch.CmdSetDepthTestEnable(this.Handle, state.DepthTest);
        if (state.DepthTest)
        {
            this.Owner.Dispatch.CmdSetDepthWriteEnable(this.Handle, state.DepthWrite);
            this.Owner.Dispatch.CmdSetDepthCompareOp(this.Handle, (Vk.VkCompareOp)state.DepthCompare);
        }
        this.Owner.Dispatch.CmdSetStencilTestEnable(this.Handle, state.StencilTest);
        if (!state.StencilTest)
            return;

        this.Owner.Dispatch.CmdSetStencilOp(this.Handle, Vk.VkStencilFaceFlags.Front,
                                           (Vk.VkStencilOp)state.Front.Fail, (Vk.VkStencilOp)state.Front.Pass,
                                           (Vk.VkStencilOp)state.Front.DepthFail, (Vk.VkCompareOp)state.Front.Compare);
        this.Owner.Dispatch.CmdSetStencilOp(this.Handle, Vk.VkStencilFaceFlags.Back,
                                           (Vk.VkStencilOp)state.Back.Fail, (Vk.VkStencilOp)state.Back.Pass,
                                           (Vk.VkStencilOp)state.Back.DepthFail, (Vk.VkCompareOp)state.Back.Compare);
        this.Owner.Api.vkCmdSetStencilCompareMask(this.Handle, Vk.VkStencilFaceFlags.FrontAndBack, state.StencilReadMask);
        this.Owner.Api.vkCmdSetStencilWriteMask(this.Handle, Vk.VkStencilFaceFlags.FrontAndBack, state.StencilWriteMask);
        this.Owner.Api.vkCmdSetStencilReference(this.Handle, Vk.VkStencilFaceFlags.Front, state.Front.Reference);
        this.Owner.Api.vkCmdSetStencilReference(this.Handle, Vk.VkStencilFaceFlags.Back, state.Back.Reference);
    }

    /// <summary>Begins a render pass with the given attachments.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <param name="flags">The rendering behavior flags.</param>
    public override void BeginRenderPass(GPURenderingDesc desc, RenderingFlags flags = RenderingFlags.None)
    {
        Debug.Assert(desc.Colors.Length <= Backend.MaxColorAttachments);
        VulkanRenderView? depthView = (VulkanRenderView?)desc.Depth.RenderView;
        VulkanRenderView? stencilView = (VulkanRenderView?)desc.Stencil.RenderView;
        VulkanRenderView? areaView = desc.Colors.Length != 0
            ? (VulkanRenderView?)desc.Colors[0].RenderView
            : (depthView != null ? depthView : stencilView);
        Debug.Assert(areaView != null, "beginRenderPass requires an attachment to determine the render area");

        Vk.VkRenderingAttachmentInfo* colorAttachments = stackalloc Vk.VkRenderingAttachmentInfo[(int)Backend.MaxColorAttachments];
        for (int index = 0; index < desc.Colors.Length; ++index)
        {
            GPUColorAttachment attachment = desc.Colors[index];
            VulkanRenderView? view = (VulkanRenderView?)attachment.RenderView;
            Debug.Assert(view != null);
            VulkanRenderView? resolveView = (VulkanRenderView?)attachment.ResolveView;
            if (resolveView != null)
            {
                Debug.Assert(view.Source.SampleCount == 4 && resolveView.Source.SampleCount == 1,
                    "color resolve requires a four-sample source and single-sample target");
                Debug.Assert(resolveView.Source.Format == view.Source.Format && Backend.SupportsColorResolve(view.Source.Format),
                    "resolve views match the attachment format and require a supported resolve format");
                Debug.Assert(Backend.IsColorResolveTarget(resolveView.Source.Type, resolveView.Source.Usage),
                    "resolve targets are 2D color attachments without storage usage");
                Debug.Assert(view.Width == areaView.Width && view.Height == areaView.Height &&
                    resolveView.Width == areaView.Width && resolveView.Height == areaView.Height, "resolve views match the render area");
                Debug.Assert(flags == RenderingFlags.None, "color resolve does not support pass continuation");
            }
            Vk.VkClearColorValue clear = new();
            clear.float32[0] = attachment.Clear.X;
            clear.float32[1] = attachment.Clear.Y;
            clear.float32[2] = attachment.Clear.Z;
            clear.float32[3] = attachment.Clear.W;
            colorAttachments[index] = new Vk.VkRenderingAttachmentInfo
            {
                imageView = view.View,
                imageLayout = Vk.VkImageLayout.General,
                resolveMode = resolveView != null ? Vk.VkResolveModeFlags.Average : Vk.VkResolveModeFlags.None,
                resolveImageView = resolveView != null ? resolveView.View : default,
                resolveImageLayout = Vk.VkImageLayout.General,
                loadOp = (Vk.VkAttachmentLoadOp)attachment.Load,
                storeOp = (Vk.VkAttachmentStoreOp)attachment.Store,
                clearValue = new Vk.VkClearValue { color = clear },
            };
        }

        Vk.VkRenderingAttachmentInfo depthAttachment = new()
        {
            imageView = depthView != null ? depthView.View : default,
            imageLayout = Vk.VkImageLayout.General,
            loadOp = (Vk.VkAttachmentLoadOp)desc.Depth.Load,
            storeOp = (Vk.VkAttachmentStoreOp)desc.Depth.Store,
            clearValue = new Vk.VkClearValue { depthStencil = new Vk.VkClearDepthStencilValue(desc.Depth.Clear, 0) },
        };
        Vk.VkRenderingAttachmentInfo stencilAttachment = new()
        {
            imageView = stencilView != null ? stencilView.View : default,
            imageLayout = Vk.VkImageLayout.General,
            loadOp = (Vk.VkAttachmentLoadOp)desc.Stencil.Load,
            storeOp = (Vk.VkAttachmentStoreOp)desc.Stencil.Store,
            clearValue = new Vk.VkClearValue { depthStencil = new Vk.VkClearDepthStencilValue(0, desc.Stencil.Clear) },
        };
        Vk.VkRenderingInfo renderingInfo = new()
        {
            flags = (Vk.VkRenderingFlags)flags,
            renderArea = new Vk.VkRect2D(0, 0, areaView.Width, areaView.Height),
            layerCount = 1,
            colorAttachmentCount = (uint)desc.Colors.Length,
            pColorAttachments = colorAttachments,
            pDepthAttachment = depthView != null ? &depthAttachment : null,
            pStencilAttachment = stencilView != null ? &stencilAttachment : null,
        };
        this.Owner.Dispatch.CmdBeginRendering(this.Handle, &renderingInfo);

        Viewport viewport = new()
        {
            Width = areaView.Width,
            Height = areaView.Height,
        };
        SetViewport(viewport);
        Scissor scissor = default;
        scissor.Width = areaView.Width;
        scissor.Height = areaView.Height;
        SetScissor(scissor);
        SetDepthStencil(default);
    }

    /// <summary>Ends the current render pass.</summary>
    public override void EndRenderPass()
    {
        this.Owner.Dispatch.CmdEndRendering(this.Handle);
    }

    /// <summary>Records a non-indexed draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="vertexCount">Number of vertices per instance.</param>
    /// <param name="instanceCount">Number of instances.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public override void Draw(GPURange root, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        BindRoot(this, root);
        this.Owner.Api.vkCmdDraw(this.Handle, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Records an indexed draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="indices">GPU index-buffer range.</param>
    /// <param name="type">Index element type.</param>
    /// <param name="indexCount">Number of indices per instance.</param>
    /// <param name="instanceCount">Number of instances.</param>
    /// <param name="firstIndex">Index of the first index-buffer element.</param>
    /// <param name="vertexOffset">Offset added to each fetched index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public override void DrawIndexed(GPURange root, GPURange indices, IndexType type, uint indexCount, uint instanceCount = 1,
                            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        Debug.Assert(this.Owner != null);
        BindRoot(this, root);
        BindIndices(this, indices, type);
        this.Owner.Api.vkCmdDrawIndexed(this.Handle, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    /// <summary>Records draws with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing indirect draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public override void DrawIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(this.Owner != null);
        BindRoot(this, root);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBuffer buffer = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Buffer;
            this.Owner.Api.vkCmdDrawIndirect(this.Handle, buffer, arguments.Offset, drawCount,
                                                stride == 0 ? (uint)sizeof(Vk.VkDrawIndirectCommand) : stride);
            return;
        }
        Vk.VkDrawIndirect2InfoKHR info = new()
        {
            addressRange = new()
            {
                address = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Address + arguments.Offset,
                size = arguments.Size,
                stride = stride == 0 ? (ulong)sizeof(Vk.VkDrawIndirectCommand) : stride,
            },
            addressFlags = Backend.AddressFlags,
            drawCount = drawCount,
        };
        this.Owner.Api.vkCmdDrawIndirect2KHR(this.Handle, &info);
    }

    /// <summary>Records indexed draws with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="indices">GPU index-buffer range.</param>
    /// <param name="type">Index element type.</param>
    /// <param name="arguments">GPU range containing indirect indexed-draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public override void DrawIndexedIndirect(GPURange root, GPURange indices, IndexType type, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(this.Owner != null);
        BindRoot(this, root);
        BindIndices(this, indices, type);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBuffer buffer = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Buffer;
            this.Owner.Api.vkCmdDrawIndexedIndirect(this.Handle, buffer, arguments.Offset, drawCount,
                                                       stride == 0 ? (uint)sizeof(Vk.VkDrawIndexedIndirectCommand) : stride);
            return;
        }
        Vk.VkDrawIndirect2InfoKHR info = new()
        {
            addressRange = new()
            {
                address = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Address + arguments.Offset,
                size = arguments.Size,
                stride = stride == 0 ? (ulong)sizeof(Vk.VkDrawIndexedIndirectCommand) : stride,
            },
            addressFlags = Backend.AddressFlags,
            drawCount = drawCount,
        };
        this.Owner.Api.vkCmdDrawIndexedIndirect2KHR(this.Handle, &info);
    }

    /// <summary>Records a compute dispatch.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="groupCount">Number of workgroups in each dimension.</param>
    public override void Dispatch(GPURange root, Extent3D groupCount)
    {
        BindRoot(this, root);
        this.Owner.Api.vkCmdDispatch(this.Handle, groupCount.X, groupCount.Y, groupCount.Z);
    }

    /// <summary>Records a compute dispatch with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing three 32-bit workgroup counts.</param>
    public override void DispatchIndirect(GPURange root, GPURange arguments)
    {
        Debug.Assert(this.Owner != null);
        BindRoot(this, root);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBuffer buffer = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Buffer;
            this.Owner.Api.vkCmdDispatchIndirect(this.Handle, buffer, arguments.Offset);
            return;
        }
        Vk.VkDispatchIndirect2InfoKHR info = new()
        {
            addressRange = new()
            {
                address = ((VulkanGpuHeapOwner)arguments.Buffer!).Backing.Address + arguments.Offset,
                size = arguments.Size,
            },
            addressFlags = Backend.AddressFlags,
        };
        this.Owner.Api.vkCmdDispatchIndirect2KHR(this.Handle, &info);
    }

    /// <summary>Records a linear GPU-memory copy.</summary>
    /// <param name="source">The source resource or memory range.</param>
    /// <param name="destination">The destination resource or memory range.</param>
    public override void CopyMemory(GPURange source, GPURange destination)
    {
        Debug.Assert(this.Owner != null);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBufferCopy2 region = new()
            {
                srcOffset = source.Offset,
                dstOffset = destination.Offset,
                size = source.Size,
            };
            Vk.VkCopyBufferInfo2 info = new()
            {
                srcBuffer = ((VulkanGpuHeapOwner)source.Buffer!).Backing.Buffer,
                dstBuffer = ((VulkanGpuHeapOwner)destination.Buffer!).Backing.Buffer,
                regionCount = 1,
                pRegions = &region,
            };
            this.Owner.Dispatch.CmdCopyBuffer2(this.Handle, &info);
            return;
        }
        Vk.VkDeviceMemoryCopyKHR memoryRegion = new()
        {
            srcRange = new()
            {
                address = ((VulkanGpuHeapOwner)source.Buffer!).Backing.Address + source.Offset,
                size = source.Size,
            },
            srcFlags = Backend.AddressFlags,
            dstRange = new()
            {
                address = ((VulkanGpuHeapOwner)destination.Buffer!).Backing.Address + destination.Offset,
                size = destination.Size,
            },
            dstFlags = Backend.AddressFlags,
        };
        Vk.VkCopyDeviceMemoryInfoKHR memoryInfo = new()
        {
            regionCount = 1,
            pRegions = &memoryRegion,
        };
        this.Owner.Api.vkCmdCopyMemoryKHR(this.Handle, &memoryInfo);
    }

    /// <summary>Records an upload into a texture region.</summary>
    /// <param name="source">The source resource or memory range.</param>
    /// <param name="destination">The destination resource or memory range.</param>
    /// <param name="copy">The texture region and memory pitches.</param>
    public override void CopyMemoryToTexture(GPURange source, GPUTexture destination, TextureCopyDesc copy = default)
    {
        VulkanTexture vulkanDestination = (VulkanTexture)destination;
        Debug.Assert(this.Owner == vulkanDestination.Owner && (vulkanDestination.Swapchain == null || this.QueueIndex == 0));
        Vk.VkDeviceMemoryImageCopyKHR region = MakeTextureCopyRegion(vulkanDestination, copy, source);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBufferImageCopy2 bufferRegion = BufferTextureCopyRegion(in region, source.Offset);
            Vk.VkCopyBufferToImageInfo2 info = new()
            {
                srcBuffer = ((VulkanGpuHeapOwner)source.Buffer!).Backing.Buffer,
                dstImage = vulkanDestination.Image,
                dstImageLayout = Vk.VkImageLayout.General,
                regionCount = 1,
                pRegions = &bufferRegion,
            };
            this.Owner.Dispatch.CmdCopyBufferToImage2(this.Handle, &info);
            return;
        }
        Vk.VkCopyDeviceMemoryImageInfoKHR deviceMemoryInfo = new()
        {
            image = vulkanDestination.Image,
            regionCount = 1,
            pRegions = &region,
        };
        this.Owner.Api.vkCmdCopyMemoryToImageKHR(this.Handle, &deviceMemoryInfo);
    }

    /// <summary>Records a readback from a texture region.</summary>
    /// <param name="source">The source resource or memory range.</param>
    /// <param name="destination">The destination resource or memory range.</param>
    /// <param name="copy">The texture region and memory pitches.</param>
    public override void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default)
    {
        VulkanTexture vulkanSource = (VulkanTexture)source;
        Debug.Assert(this.Owner == vulkanSource.Owner && (vulkanSource.Swapchain == null || this.QueueIndex == 0));
        Vk.VkDeviceMemoryImageCopyKHR region = MakeTextureCopyRegion(vulkanSource, copy, destination);
        if (!this.Owner.AddressCommands)
        {
            Vk.VkBufferImageCopy2 bufferRegion = BufferTextureCopyRegion(in region, destination.Offset);
            Vk.VkCopyImageToBufferInfo2 info = new()
            {
                srcImage = vulkanSource.Image,
                srcImageLayout = Vk.VkImageLayout.General,
                dstBuffer = ((VulkanGpuHeapOwner)destination.Buffer!).Backing.Buffer,
                regionCount = 1,
                pRegions = &bufferRegion,
            };
            this.Owner.Dispatch.CmdCopyImageToBuffer2(this.Handle, &info);
            return;
        }
        Vk.VkCopyDeviceMemoryImageInfoKHR deviceMemoryInfo = new()
        {
            image = vulkanSource.Image,
            regionCount = 1,
            pRegions = &region,
        };
        this.Owner.Api.vkCmdCopyImageToMemoryKHR(this.Handle, &deviceMemoryInfo);
    }

    /// <summary>Records a non-overlapping texture copy outside render passes; formats, texture types and selected mip extents must match.</summary>
    /// <param name="source">The initialized transfer-source texture; combined depth/stencil formats are not supported.</param>
    /// <param name="destination">The initialized transfer-destination texture.</param>
    /// <param name="copy">The source and destination regions; synchronization remains explicit.</param>
    public override void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default)
    {
        VulkanTexture vulkanSource = (VulkanTexture)source;
        VulkanTexture vulkanDestination = (VulkanTexture)destination;
        Debug.Assert(vulkanSource.Owner == this.Owner && vulkanDestination.Owner == this.Owner);
        Debug.Assert((vulkanSource.Swapchain == null && vulkanDestination.Swapchain == null) || this.QueueIndex == 0);
        Debug.Assert(vulkanSource.Format == vulkanDestination.Format && vulkanSource.Type == vulkanDestination.Type);
        Extent3D mipExtent = new()
        {
            X = Math.Max(vulkanSource.Width >> (int)copy.SourceMipLevel, 1u),
            Y = Math.Max(vulkanSource.Height >> (int)copy.SourceMipLevel, 1u),
            Z = Math.Max(vulkanSource.Depth >> (int)copy.SourceMipLevel, 1u),
        };
        Debug.Assert(mipExtent.X == Math.Max(vulkanDestination.Width >> (int)copy.DestinationMipLevel, 1u) &&
            mipExtent.Y == Math.Max(vulkanDestination.Height >> (int)copy.DestinationMipLevel, 1u) &&
            mipExtent.Z == Math.Max(vulkanDestination.Depth >> (int)copy.DestinationMipLevel, 1u));
        Backend.AssertTextureCopyRegion(vulkanSource.Format, mipExtent, copy.SourceOffset, copy.Extent);
        Backend.AssertTextureCopyRegion(vulkanDestination.Format, mipExtent, copy.DestinationOffset, copy.Extent);
        uint slices = copy.SliceCount == 0 ? vulkanSource.LayerCount - copy.SourceBaseSlice : copy.SliceCount;
        Vk.VkImageCopy2 region = new()
        {
            srcSubresource = new()
            {
                aspectMask = Backend.ImageAspects(vulkanSource.Format),
                mipLevel = copy.SourceMipLevel,
                baseArrayLayer = copy.SourceBaseSlice,
                layerCount = slices,
            },
            srcOffset = new() { x = (int)copy.SourceOffset.X, y = (int)copy.SourceOffset.Y, z = (int)copy.SourceOffset.Z },
            dstSubresource = new()
            {
                aspectMask = Backend.ImageAspects(vulkanDestination.Format),
                mipLevel = copy.DestinationMipLevel,
                baseArrayLayer = copy.DestinationBaseSlice,
                layerCount = slices,
            },
            dstOffset = new() { x = (int)copy.DestinationOffset.X, y = (int)copy.DestinationOffset.Y, z = (int)copy.DestinationOffset.Z },
            extent = new()
            {
                width = copy.Extent.X == 0 ? mipExtent.X - copy.SourceOffset.X : copy.Extent.X,
                height = copy.Extent.Y == 0 ? mipExtent.Y - copy.SourceOffset.Y : copy.Extent.Y,
                depth = copy.Extent.Z == 0 ? mipExtent.Z - copy.SourceOffset.Z : copy.Extent.Z,
            },
        };
        Vk.VkCopyImageInfo2 info = new()
        {
            srcImage = vulkanSource.Image,
            srcImageLayout = Vk.VkImageLayout.General,
            dstImage = vulkanDestination.Image,
            dstImageLayout = Vk.VkImageLayout.General,
            regionCount = 1,
            pRegions = &region,
        };
        this.Owner.Dispatch.CmdCopyImage2(this.Handle, &info);
    }

    /// <summary>Inserts an execution and memory dependency between two command scopes.</summary>
    /// <param name="before">The source pipeline stages.</param>
    /// <param name="beforeAccess">The source memory accesses.</param>
    /// <param name="after">The destination pipeline stages.</param>
    /// <param name="afterAccess">The destination memory accesses.</param>
    public override void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
    {
        RecordBarrier(this.Owner, this.Handle, before, beforeAccess, after, afterAccess);
    }
}
