using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// Vulkan command buffer with automatic barrier handling. The public render/compute
/// pass model is lowered to Vulkan 1.3 dynamic rendering; all synchronization is
/// derived from how resources are used (see <see cref="VulkanResourceTracker"/>).
/// <para>
/// Render bundles are replayed by re-executing their recorded bind/draw commands on
/// this buffer's native command buffer through the exact same code paths a direct
/// call would use, so bundle-recorded resources participate in barrier tracking.
/// </para>
/// </summary>
internal sealed unsafe class VulkanCommandBuffer : GPUCommandBuffer
{
    private readonly VulkanDevice _device;
    private VkCommandBuffer _commandBuffer;
    private VkFence _inFlightFence;

    private VulkanFrameBufferBase? _currentFrameBuffer;
    private VulkanPipeline? _currentGraphicsPipeline;
    private VulkanPipeline? _currentComputePipeline;
    private (VulkanTimestampQuerySet Set, uint Index)? _pendingEndTimestamp;
    private bool _hasCommands;

    protected override VulkanDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public override bool HasBuffer => _hasCommands;

    /// <summary>The native command buffer recorded since the last Begin().</summary>
    public VkCommandBuffer NativeCommandBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _commandBuffer;
    }

    public VulkanCommandBuffer(VulkanDevice device, in CommandBufferDescriptor? descriptor)
        : base(descriptor)
    {
        _device = device;
        _commandBuffer = device.AllocateCommandBuffer();
        _inFlightFence = device.CreateFenceNative(signaled: true); // so the first Begin() does not wait on a never-submitted fence
    }

    /// <summary>Fence signaled when the last submission of this buffer completes;
    /// waiting on it before re-recording keeps frames-in-flight safe.</summary>
    public VkFence InFlightFence
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _inFlightFence;
    }

    // ===== command buffer lifecycle =====

    protected override void BeginCore()
    {
        if (_commandBuffer.Handle == 0)
        {
            _commandBuffer = _device.AllocateCommandBuffer();
        }
        _device.PrepareCommandBuffer(this);

        VkCommandBufferBeginInfo beginInfo = new()
        {
            flags = VkCommandBufferUsageFlags.OneTimeSubmit,
        };
        vkBeginCommandBuffer(_commandBuffer, &beginInfo).ThrowOnFailure();
        _hasCommands = true;
    }

    protected override void EndCore()
    {
        vkEndCommandBuffer(_commandBuffer).ThrowOnFailure();
    }

    protected override void Dispose(bool disposing)
    {
        if (_inFlightFence.Handle != 0)
        {
            _device.QueueNativeDestroy(_inFlightFence);
            _inFlightFence = default;
        }
        // the native command buffer belongs to the device pool and is freed with it
        _commandBuffer = default;
    }

    // ===== render passes =====

    protected override void BeginRenderCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        BeginRenderNative(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps);
    }

    protected override void BeginRenderTimestampCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        BeginRenderNative(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps);

        if (beginningQueryIndex.HasValue)
        {
            vkCmdWriteTimestamp2(
                _commandBuffer,
                VkPipelineStageFlags2.TopOfPipe,
                ((VulkanTimestampQuerySet)querySet).Native,
                beginningQueryIndex.Value);
        }
        _pendingEndTimestamp = endQueryIndex.HasValue
            ? ((VulkanTimestampQuerySet)querySet, endQueryIndex.Value)
            : null;
    }

    protected override void EndRenderCore()
    {
        vkCmdEndRendering(_commandBuffer);

        if (_pendingEndTimestamp.HasValue)
        {
            (VulkanTimestampQuerySet set, uint index) = _pendingEndTimestamp.Value;
            vkCmdWriteTimestamp2(_commandBuffer, VkPipelineStageFlags2.BottomOfPipe, set.Native, index);
            _pendingEndTimestamp = null;
        }

        // make every write of the pass visible to any later use
        _device.Tracker.FlushPass(_commandBuffer);
        _currentFrameBuffer = null;
        _currentGraphicsPipeline = null;
    }

    /// <summary>Enters attachment states (precise barriers + touching) and starts
    /// dynamic rendering with the assembled load/store ops and clear values.</summary>
    private void BeginRenderNative(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        VulkanFrameBufferBase frameBufferImpl = (VulkanFrameBufferBase)frameBuffer;
        VulkanAttachmentLayout layout = (VulkanAttachmentLayout)frameBufferImpl.AttachmentLayout;
        _currentFrameBuffer = frameBufferImpl;

        ReadOnlySpan<GPUTexture> colors = frameBufferImpl.Colors;
        ReadOnlySpan<GPUTextureView> colorViews = frameBufferImpl.ColorViews;

        // ===== attachment barriers (outside the pass: one batched command) =====
        int attachmentCount = colors.Length + (frameBufferImpl.DepthStencil != null ? 1 : 0);
        VulkanResourceTracker.BatchTransition[] transitions
            = new VulkanResourceTracker.BatchTransition[attachmentCount];
        int transitionIndex = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            VulkanTexture texture = (VulkanTexture)colors[i];
            transitions[transitionIndex++] = new VulkanResourceTracker.BatchTransition(texture, VulkanResourceState.ColorAttachment);
            _device.Tracker.TouchInPass(texture);
        }

        bool hasDepth = frameBufferImpl.DepthStencil != null;
        bool depthReadOnly = false;
        VkImageLayout depthLayout = VkImageLayout.DepthStencilAttachmentOptimal;
        if (hasDepth)
        {
            VulkanTexture depthTexture = (VulkanTexture)frameBufferImpl.DepthStencil!;
            depthReadOnly = layout.DepthInfo?.ReadOnly ?? false;
            transitions[transitionIndex++] = new VulkanResourceTracker.BatchTransition(
                depthTexture,
                depthReadOnly ? VulkanResourceState.DepthRead : VulkanResourceState.DepthWrite);
            _device.Tracker.TouchInPass(depthTexture);
            depthLayout = _device.Tracker.LayoutForTexture(depthTexture, depthReadOnly ? VulkanResourceState.DepthRead : VulkanResourceState.DepthWrite);
        }
        _device.Tracker.TransitionBatch(_commandBuffer, transitions);

        // ===== attachment infos =====
        int colorCount = colors.Length;
        VkRenderingAttachmentInfo* colorAttachments = stackalloc VkRenderingAttachmentInfo[Math.Max(1, colorCount)];
        for (int i = 0; i < colorCount; i++)
        {
            AttachmentLoadOp loadOp = i < colorOps.Length ? colorOps[i].LoadOp : AttachmentLoadOp.Load;
            AttachmentStoreOp storeOp = i < colorOps.Length ? colorOps[i].StoreOp : AttachmentStoreOp.Store;

            Vector4 clearColor = layout.ColorAttachments[i].ClearColor;
            for (int c = 0; c < clearColors.Length; c++)
            {
                if (clearColors[c].Index == (uint)i)
                {
                    loadOp = AttachmentLoadOp.Clear;
                    clearColor = clearColors[c].Color;
                    break;
                }
            }

            VkClearValue clearValue = default;
            clearValue.color.float32[0] = clearColor.X;
            clearValue.color.float32[1] = clearColor.Y;
            clearValue.color.float32[2] = clearColor.Z;
            clearValue.color.float32[3] = clearColor.W;

            colorAttachments[i] = new VkRenderingAttachmentInfo
            {
                imageView = ((VulkanTextureView)colorViews[i]).Native,
                imageLayout = _device.Tracker.LayoutForTexture((VulkanTexture)colors[i], VulkanResourceState.ColorAttachment),
                loadOp = LoadOpToVulkan(loadOp),
                storeOp = StoreOpToVulkan(storeOp),
                clearValue = clearValue,
            };
        }

        VkRenderingAttachmentInfo depthAttachment = default;
        VkRenderingAttachmentInfo stencilAttachment = default;
        bool useDepth = false;
        bool useStencil = false;
        if (hasDepth)
        {
            DepthAttachment depthInfo = layout.DepthInfo ?? new DepthAttachment(PixelFormat.Depth32Float);

            float depthClear = depthInfo.ClearDepth;
            uint stencilClear = depthInfo.ClearStencil;
            AttachmentLoadOp depthLoadOp = clearDepth.HasValue ? AttachmentLoadOp.Clear : depthOps?.LoadOp ?? AttachmentLoadOp.Load;
            AttachmentLoadOp stencilLoadOp = clearStencil.HasValue ? AttachmentLoadOp.Clear : depthOps?.LoadOp ?? AttachmentLoadOp.Load;
            AttachmentStoreOp depthStoreOp = depthOps?.StoreOp ?? AttachmentStoreOp.Store;
            AttachmentStoreOp stencilStoreOp = depthOps?.StoreOp ?? AttachmentStoreOp.Store;
            if (clearDepth.HasValue)
            {
                depthClear = clearDepth.Value;
            }
            if (clearStencil.HasValue)
            {
                stencilClear = clearStencil.Value;
            }
            // read-only attachments cannot be cleared
            if (depthReadOnly)
            {
                depthLoadOp = AttachmentLoadOp.Load;
                stencilLoadOp = AttachmentLoadOp.Load;
            }

            depthAttachment = new VkRenderingAttachmentInfo
            {
                imageView = ((VulkanTextureView)frameBufferImpl.DepthStencilView!).Native,
                imageLayout = depthLayout,
                loadOp = LoadOpToVulkan(depthLoadOp),
                storeOp = StoreOpToVulkan(depthStoreOp),
                clearValue = new VkClearValue
                {
                    depthStencil = new VkClearDepthStencilValue(depthClear, stencilClear),
                },
            };
            useDepth = true;

            if (VulkanUtility.HasStencil(((VulkanTexture)frameBufferImpl.DepthStencil!).VkFormat))
            {
                stencilAttachment = new VkRenderingAttachmentInfo
                {
                    imageView = ((VulkanTextureView)frameBufferImpl.DepthStencilView!).Native,
                    imageLayout = depthLayout,
                    loadOp = LoadOpToVulkan(stencilLoadOp),
                    storeOp = StoreOpToVulkan(stencilStoreOp),
                    clearValue = new VkClearValue
                    {
                        depthStencil = new VkClearDepthStencilValue(depthClear, stencilClear),
                    },
                };
                useStencil = true;
            }
        }

        // ===== dynamic rendering =====
        // ContentsSecondaryCommandBuffers: render bundles execute through
        // secondary command buffers inside the pass (VUID-vkCmdExecuteCommands-flags-06024)
        VkRenderingInfo renderingInfo = new()
        {
            flags = VkRenderingFlags.ContentsSecondaryCommandBuffers,
            renderArea = new VkRect2D
            {
                offset = default,
                extent = new VkExtent2D { width = frameBufferImpl.Width, height = frameBufferImpl.Height },
            },
            layerCount = 1,
            colorAttachmentCount = (uint)colorCount,
            pColorAttachments = colorAttachments,
            pDepthAttachment = useDepth ? &depthAttachment : null,
            pStencilAttachment = useStencil ? &stencilAttachment : null,
        };
        vkCmdBeginRendering(_commandBuffer, &renderingInfo);

        // ===== automatic viewport/scissor over the whole frame buffer =====
        // the engine's NDC is wgpu-style (Y up); Vulkan's framebuffer space is
        // Y down, so flip with a negative-height viewport like wgpu does
        VkViewport viewport = new()
        {
            x = 0,
            y = frameBufferImpl.Height,
            width = frameBufferImpl.Width,
            height = -(float)frameBufferImpl.Height,
            minDepth = 0f,
            maxDepth = 1f,
        };
        VkRect2D scissor = new()
        {
            offset = default,
            extent = new VkExtent2D { width = frameBufferImpl.Width, height = frameBufferImpl.Height },
        };
        vkCmdSetViewport(_commandBuffer, 0, 1, &viewport);
        vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);
        _currentViewport = viewport;
        _currentScissor = scissor;
    }

    private static VkAttachmentLoadOp LoadOpToVulkan(AttachmentLoadOp loadOp)
    {
        return loadOp switch
        {
            AttachmentLoadOp.Load => VkAttachmentLoadOp.Load,
            AttachmentLoadOp.Clear => VkAttachmentLoadOp.Clear,
            _ => VkAttachmentLoadOp.Load,
        };
    }

    private static VkAttachmentStoreOp StoreOpToVulkan(AttachmentStoreOp storeOp)
    {
        return storeOp switch
        {
            AttachmentStoreOp.Store => VkAttachmentStoreOp.Store,
            AttachmentStoreOp.Discard => VkAttachmentStoreOp.DontCare,
            _ => VkAttachmentStoreOp.Store,
        };
    }

    // ===== compute passes =====

    protected override void BeginComputeCore()
    {
        _currentComputePipeline = null;
    }

    protected override void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex)
    {
        BeginComputeCore();
        if (beginningQueryIndex.HasValue)
        {
            vkCmdWriteTimestamp2(
                _commandBuffer,
                VkPipelineStageFlags2.TopOfPipe,
                ((VulkanTimestampQuerySet)querySet).Native,
                beginningQueryIndex.Value);
        }
        _pendingEndTimestamp = endQueryIndex.HasValue
            ? ((VulkanTimestampQuerySet)querySet, endQueryIndex.Value)
            : null;
    }

    protected override void WriteTimestampInsidePassCore(GPUTimestampQuerySet querySet, uint queryIndex)
    {
        vkCmdWriteTimestamp2(
            _commandBuffer,
            VkPipelineStageFlags2.BottomOfPipe,
            ((VulkanTimestampQuerySet)querySet).Native,
            queryIndex);
    }

    protected override void EndComputeCore()
    {
        if (_pendingEndTimestamp.HasValue)
        {
            (VulkanTimestampQuerySet set, uint index) = _pendingEndTimestamp.Value;
            vkCmdWriteTimestamp2(_commandBuffer, VkPipelineStageFlags2.BottomOfPipe, set.Native, index);
            _pendingEndTimestamp = null;
        }
        _device.Tracker.FlushPass(_commandBuffer);
        _currentComputePipeline = null;
    }

    // ===== graphics state =====

    // dynamic state currently in effect on this command buffer; bundles need it
    // to bake the same state into cached secondary command buffers
    private VkViewport _currentViewport;
    private VkRect2D _currentScissor;
    private uint _currentStencilReference;
    internal VkViewport CurrentViewport => _currentViewport;
    internal VkRect2D CurrentScissor => _currentScissor;
    internal uint CurrentStencilReference => _currentStencilReference;

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
        VkRect2D scissor = new()
        {
            offset = new VkOffset2D { x = (int)x, y = (int)y },
            extent = new VkExtent2D { width = width, height = height },
        };
        vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);
        _currentScissor = scissor;
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        VulkanPipeline pipelineImpl = (VulkanPipeline)pipeline;
        vkCmdBindPipeline(_commandBuffer, VkPipelineBindPoint.Graphics, pipelineImpl.Native);
        // stencil reference is dynamic; reset it with the pipeline
        vkCmdSetStencilReference(_commandBuffer, VkStencilFaceFlags.FrontAndBack, 0);
        _currentStencilReference = 0;
        _currentGraphicsPipeline = pipelineImpl;
    }

    protected override void SetStencilReferenceCore(uint value)
    {
        vkCmdSetStencilReference(_commandBuffer, VkStencilFaceFlags.FrontAndBack, value);
        _currentStencilReference = value;
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        VulkanPipeline pipeline = _currentGraphicsPipeline
            ?? throw new GraphicsException("SetResources requires a bound graphics pipeline.");
        BindGraphicsResourcesNative(_commandBuffer, pipeline, _device, slot, (VulkanResourceGroup)resourceGroup);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;
        VkBuffer nativeBuffer = bufferImpl.Native;
        ulong deviceOffset = offset;
        vkCmdBindVertexBuffers(_commandBuffer, slot, 1, &nativeBuffer, &deviceOffset);
        MarkBufferUse(bufferImpl, VulkanResourceState.VertexRead);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;
        vkCmdBindIndexBuffer(_commandBuffer, bufferImpl.Native, offset, VulkanUtility.IndexFormatToVulkan(format));
        MarkBufferUse(bufferImpl, VulkanResourceState.IndexRead);
    }

    internal static long TraceSubmits;

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        vkCmdDraw(_commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        vkCmdDrawIndexed(_commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        MarkBufferUse(buffer, VulkanResourceState.IndirectRead);
        vkCmdDrawIndirect(_commandBuffer, buffer.Native, offset, 1, (uint)sizeof(VkDrawIndirectCommand));
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        MarkBufferUse(buffer, VulkanResourceState.IndirectRead);
        vkCmdDrawIndexedIndirect(_commandBuffer, buffer.Native, offset, 1, (uint)sizeof(VkDrawIndexedIndirectCommand));
    }

    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        VulkanPipeline pipeline = _currentGraphicsPipeline
            ?? throw new GraphicsException("PushConstants requires a bound graphics pipeline.");
        vkCmdPushConstants(
            _commandBuffer,
            pipeline.NativeLayout,
            pipeline.PushConstantStages,
            bufferOffset,
            size,
            data);
    }

    // ===== compute state =====

    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        VulkanPipeline pipelineImpl = (VulkanPipeline)pipeline;
        vkCmdBindPipeline(_commandBuffer, VkPipelineBindPoint.Compute, pipelineImpl.Native);
        _currentComputePipeline = pipelineImpl;
    }

    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        VulkanPipeline pipeline = _currentComputePipeline
            ?? throw new GraphicsException("SetResources requires a bound compute pipeline.");
        BindComputeResourcesNative(_commandBuffer, pipeline, _device, slot, (VulkanResourceGroup)resourceGroup);
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        vkCmdDispatch(_commandBuffer, x, y, z);
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        MarkBufferUse(buffer, VulkanResourceState.IndirectRead);
        vkCmdDispatchIndirect(_commandBuffer, buffer.Native, offset);
    }

    protected override unsafe void PushComputeConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        VulkanPipeline pipeline = _currentComputePipeline
            ?? throw new GraphicsException("PushConstants requires a bound compute pipeline.");
        vkCmdPushConstants(
            _commandBuffer,
            pipeline.NativeLayout,
            pipeline.PushConstantStages,
            bufferOffset,
            size,
            data);
    }

    // ===== bundles =====

    protected override void ExecuteBundleCore(GPURenderBundle bundle)
    {
        VulkanRenderBundle bundleImpl = (VulkanRenderBundle)bundle;
        if (!bundleImpl.HasBuffer)
        {
            throw new GraphicsException($"Render bundle '{bundleImpl.Name}' was never recorded; call Begin/End before executing it.");
        }

        // replay through a cached secondary command buffer: one driver call per
        // bundle instead of re-recording every command from C# each execute
        // (mirrors wgpu's native render bundle execution)
        VkCommandBuffer secondary = bundleImpl.GetOrRecordSecondary(
            _device, this, _currentFrameBuffer!, _currentViewport, _currentScissor, _currentStencilReference);
        vkCmdExecuteCommands(_commandBuffer, 1, &secondary);
        bundleImpl.ApplyTrackerMarks(_device);
    }

    protected override void ExecuteBundleCore(ReadOnlySpan<GPURenderBundle> bundles)
    {
        foreach (GPURenderBundle bundle in bundles)
        {
            ExecuteBundleCore(bundle);
        }
    }

    /// <summary>Core of a graphics resource bind (also used by bundle replay).</summary>
    internal static void BindGraphicsResourcesNative(
        VkCommandBuffer commandBuffer,
        VulkanPipeline pipeline,
        VulkanDevice device,
        uint slot,
        VulkanResourceGroup group)
    {
        VkDescriptorSet set = group.NativeSet;
        vkCmdBindDescriptorSets(
            commandBuffer,
            VkPipelineBindPoint.Graphics,
            pipeline.NativeLayout,
            slot,
            1,
            &set,
            0,
            null);

        // bind-time state updates without barriers: a resource has a single usage
        // per pass (wgpu usage-scope semantics) and the previous pass flushed
        IReadOnlyList<VulkanBuffer> buffers = group.BoundBuffers;
        IReadOnlyList<VulkanResourceState> bufferStates = group.BoundBufferStates;
        for (int i = 0; i < buffers.Count; i++)
        {
            device.Tracker.MarkBuffer(buffers[i], bufferStates[i]);
            device.Tracker.TouchInPass(buffers[i]);
        }

        IReadOnlyList<VulkanTextureView> views = group.BoundViews;
        IReadOnlyList<VulkanResourceState> viewStates = group.BoundViewStates;
        for (int i = 0; i < views.Count; i++)
        {
            device.Tracker.MarkTexture(views[i].TextureRef, viewStates[i]);
            device.Tracker.TouchInPass(views[i].TextureRef);
        }
    }

    /// <summary>Core of a compute resource bind (also used by bundle replay).</summary>
    internal static void BindComputeResourcesNative(
        VkCommandBuffer commandBuffer,
        VulkanPipeline pipeline,
        VulkanDevice device,
        uint slot,
        VulkanResourceGroup group)
    {
        VkDescriptorSet set = group.NativeSet;
        vkCmdBindDescriptorSets(
            commandBuffer,
            VkPipelineBindPoint.Compute,
            pipeline.NativeLayout,
            slot,
            1,
            &set,
            0,
            null);

        IReadOnlyList<VulkanBuffer> buffers = group.BoundBuffers;
        IReadOnlyList<VulkanResourceState> bufferStates = group.BoundBufferStates;
        for (int i = 0; i < buffers.Count; i++)
        {
            device.Tracker.MarkBuffer(buffers[i], bufferStates[i]);
            device.Tracker.TouchInPass(buffers[i]);
        }

        IReadOnlyList<VulkanTextureView> views = group.BoundViews;
        IReadOnlyList<VulkanResourceState> viewStates = group.BoundViewStates;
        for (int i = 0; i < views.Count; i++)
        {
            device.Tracker.MarkTexture(views[i].TextureRef, viewStates[i]);
            device.Tracker.TouchInPass(views[i].TextureRef);
        }
    }

    private void MarkBufferUse(VulkanBuffer buffer, VulkanResourceState state)
    {
        _device.Tracker.MarkBuffer(buffer, state);
        _device.Tracker.TouchInPass(buffer);
    }

    // ===== out-of-pass copies =====

    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        VulkanBuffer srcImpl = (VulkanBuffer)src;
        VulkanBuffer dstImpl = (VulkanBuffer)dst;

        _device.Tracker.TransitionBuffer(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _device.Tracker.TransitionBuffer(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        VkBufferCopy copy = new()
        {
            srcOffset = srcOffset,
            dstOffset = dstOffset,
            size = size,
        };
        vkCmdCopyBuffer(_commandBuffer, srcImpl.Native, dstImpl.Native, 1, &copy);

        // writes are visible to any later command in any submission
        _device.Tracker.MakeWritesVisible(_commandBuffer, VulkanResourceState.CopyDst);
    }

    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect)
    {
        VulkanBuffer srcImpl = (VulkanBuffer)src;
        VulkanTexture dstImpl = (VulkanTexture)dst;

        _device.Tracker.TransitionBuffer(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _device.Tracker.TransitionTexture(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        VkImageAspectFlags imageAspect = VulkanUtility.AspectToVulkan(aspect, dstImpl.VkFormat);
        (uint width, uint height, uint depthOrLayers, ulong layerStride) = MipTransferLayout(dstImpl, mipLevel, imageAspect);

        // the buffer holds the mip tightly packed; all array layers in sequence
        VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(
            mipLevel, width, height, depthOrLayers, imageAspect, offset);
        vkCmdCopyBufferToImage(_commandBuffer, srcImpl.Native, dstImpl.Image,
            _device.Tracker.LayoutForTexture(dstImpl, VulkanResourceState.CopyDst), 1, &copy);

        _device.Tracker.RestoreImageToIdle(_commandBuffer, dstImpl);
    }

    protected override void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect)
    {
        VulkanTexture srcImpl = (VulkanTexture)src;
        VulkanTexture dstImpl = (VulkanTexture)dst;

        _device.Tracker.TransitionTexture(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _device.Tracker.TransitionTexture(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        VkImageAspectFlags imageAspect = VulkanUtility.AspectToVulkan(aspect, srcImpl.VkFormat);

        VkImageCopy copy = new()
        {
            srcSubresource = new VkImageSubresourceLayers
            {
                aspectMask = imageAspect,
                mipLevel = srcMipLevel,
                baseArrayLayer = 0,
                layerCount = srcImpl.ArrayLayers,
            },
            srcOffset = default,
            dstSubresource = new VkImageSubresourceLayers
            {
                aspectMask = imageAspect,
                mipLevel = dstMipLevel,
                baseArrayLayer = 0,
                layerCount = Math.Min(srcImpl.ArrayLayers, dstImpl.ArrayLayers),
            },
            dstOffset = default,
            extent = new VkExtent3D
            {
                width = uint.Min(srcImpl.MipWidth(srcMipLevel), dstImpl.MipWidth(dstMipLevel)),
                height = uint.Min(srcImpl.MipHeight(srcMipLevel), dstImpl.MipHeight(dstMipLevel)),
                depth = 1,
            },
        };
        vkCmdCopyImage(_commandBuffer,
            srcImpl.Image, _device.Tracker.LayoutForTexture(srcImpl, VulkanResourceState.CopySrc),
            dstImpl.Image, _device.Tracker.LayoutForTexture(dstImpl, VulkanResourceState.CopyDst), 1, &copy);

        _device.Tracker.RestoreImageToIdle(_commandBuffer, srcImpl);
        _device.Tracker.RestoreImageToIdle(_commandBuffer, dstImpl);
    }

    protected override void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset)
    {
        VulkanBuffer dstImpl = (VulkanBuffer)destination;
        _device.Tracker.TransitionBuffer(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        vkCmdCopyQueryPoolResults(
            _commandBuffer,
            ((VulkanTimestampQuerySet)querySet).Native,
            firstQuery,
            queryCount,
            dstImpl.Native,
            destinationOffset,
            sizeof(ulong),
            VkQueryResultFlags.Bit64);

        _device.Tracker.MakeWritesVisible(_commandBuffer, VulkanResourceState.CopyDst);

        // queries must be reset between uses; do it right after the copy so the
        // pool is ready for the next frame's writes
        vkCmdResetQueryPool(
            _commandBuffer,
            ((VulkanTimestampQuerySet)querySet).Native,
            firstQuery,
            queryCount);
    }

    /// <summary>Mip dimensions and tightly packed buffer layout for a transfer.</summary>
    private static (uint Width, uint Height, uint DepthOrLayers, ulong LayerStride) MipTransferLayout(
        VulkanTexture texture, uint mipLevel, VkImageAspectFlags aspect)
    {
        uint width = texture.MipWidth(mipLevel);
        uint height = texture.MipHeight(mipLevel);

        uint texelSize = VulkanUtility.PixelFormatSize(texture.PixelFormat);
        uint rowPitch = VulkanUtility.AlignUp(width * texelSize, VulkanUtility.TexelRowAlignment);
        ulong layerStride = (ulong)rowPitch * height;

        uint depthOrLayers = texture.Is3D ? texture.MipDepth(mipLevel) : texture.ArrayLayers;
        return (width, height, depthOrLayers, layerStride);
    }
}
