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

    // Recording slot ring (wgpu-style per-frame command buffers): every Begin()
    // rotates to the next native buffer, so re-recording waits a submission from
    // RecordingSlotCount Begin cycles back instead of the previous frame's. With
    // the swapchain's two-flight throttle that submission is always complete
    // already, so CPU recording overlaps GPU execution instead of lock-stepping.
    private const int RecordingSlotCount = VulkanSwapchain.FlightSlotCount + 1;

    private struct RecordingSlot
    {
        // the native buffer, allocated lazily on the slot's first use
        public VkCommandBuffer Buffer;
        // the timeline value this slot's last submission signaled; re-recording
        // waits it before resetting (riders retire with the same wait)
        public long LastSubmitTimelineValue;
        // rider one-shots folded into this slot's last submission (deferred-work
        // lead / present trail): their completion is covered by the same timeline
        // value, so PrepareCommandBuffer recycles them after waiting it
        public VkCommandBuffer PendingLeadFlush;
        public VkCommandBuffer PendingTrailBarrier;
    }

    private readonly RecordingSlot[] _slots = new RecordingSlot[RecordingSlotCount];
    private int _activeSlot;
    private int _nextSlot;

    // the timeline value the active slot's last submission signaled
    internal long LastSubmitTimelineValue
    {
        get => _slots[_activeSlot].LastSubmitTimelineValue;
        set => _slots[_activeSlot].LastSubmitTimelineValue = value;
    }

    // rider one-shots folded into the active slot's last submission
    internal VkCommandBuffer PendingLeadFlush
    {
        get => _slots[_activeSlot].PendingLeadFlush;
        set => _slots[_activeSlot].PendingLeadFlush = value;
    }

    internal VkCommandBuffer PendingTrailBarrier
    {
        get => _slots[_activeSlot].PendingTrailBarrier;
        set => _slots[_activeSlot].PendingTrailBarrier = value;
    }

    // private recording tracker: states evolve in record order (== execution
    // order inside this buffer), seeded from the device tracker at each
    // resource's first use; reconciled with the device tracker at Submit time
    private readonly VulkanResourceTracker _tracker;
    // per-pass attachment transition scratch, grown once then reused so the
    // recording hot path never allocates
    private VulkanResourceTracker.BatchTransition[] _transitionScratch = Array.Empty<VulkanResourceTracker.BatchTransition>();

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

    /// <summary>The per-recording resource tracker; reconciled with the device
    /// tracker at submit time (see <see cref="VulkanResourceTracker"/>).</summary>
    internal VulkanResourceTracker Tracker => _tracker;

    public VulkanCommandBuffer(VulkanDevice device, in CommandBufferDescriptor? descriptor)
        : base(descriptor)
    {
        _device = device;
        _tracker = new VulkanResourceTracker(device.Tracker);
        // slots allocate lazily and stay at timeline value 0: the first Begin()
        // of each slot has nothing to wait
    }

    // ===== command buffer lifecycle =====

    protected override void BeginCore()
    {
        // rotate to the next slot: with one submission per frame per context the
        // slot being reused was last submitted RecordingSlotCount frames ago,
        // so the re-record wait below is satisfied by the swapchain's
        // frames-in-flight throttle and never actually blocks
        _activeSlot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % RecordingSlotCount;
        ref RecordingSlot slot = ref _slots[_activeSlot];
        if (slot.Buffer.Handle == 0)
        {
            slot.Buffer = _device.AllocateCommandBuffer();
        }
        _commandBuffer = slot.Buffer;
        _device.PrepareCommandBuffer(this);
        _tracker.Reset();

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
        for (int i = 0; i < _slots.Length; i++)
        {
            ref RecordingSlot slot = ref _slots[i];
            // riders may still be executing; their timeline value dies with this
            // wrapper (nothing will ever wait it), so retire them by frame
            // distance like the wrapper's own buffers
            if (slot.PendingLeadFlush.Handle != 0)
            {
                _device.RetireRiderOneShotByFrame(slot.PendingLeadFlush);
                slot.PendingLeadFlush = default;
            }
            if (slot.PendingTrailBarrier.Handle != 0)
            {
                _device.RetireRiderOneShotByFrame(slot.PendingTrailBarrier);
                slot.PendingTrailBarrier = default;
            }
            if (slot.Buffer.Handle != 0)
            {
                // the buffer may still be executing on the GPU; the deferred free
                // waits out frames in flight before returning it to the pool
                _device.QueueSecondaryCommandBufferFree(slot.Buffer);
                slot.Buffer = default;
            }
        }
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

        // backstop: flush bind-time transitions queued after the last draw
        // (usually empty — every draw flushes). Writes stay in their end state;
        // the next usage point emits its own precise edge.
        _tracker.FlushPendingBarriers(_commandBuffer);
        _tracker.EndPassScope();
        _currentFrameBuffer = null;
        _currentGraphicsPipeline = null;
    }

    /// <summary>Enters attachment states (precise barriers) and starts dynamic
    /// rendering with the assembled load/store ops and clear values.</summary>
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
        // a per-buffer scratch array (grown once, then reused) keeps the
        // per-pass recording off the GC heap
        if (_transitionScratch.Length < attachmentCount)
        {
            int capacity = Math.Max(attachmentCount, Math.Max(8, _transitionScratch.Length * 2));
            _transitionScratch = new VulkanResourceTracker.BatchTransition[capacity];
        }
        VulkanResourceTracker.BatchTransition[] transitions = _transitionScratch;
        int transitionIndex = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            VulkanTexture texture = (VulkanTexture)colors[i];
            transitions[transitionIndex++] = new VulkanResourceTracker.BatchTransition(texture, VulkanResourceState.ColorAttachment);
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
            depthLayout = _tracker.LayoutForTexture(depthTexture, depthReadOnly ? VulkanResourceState.DepthRead : VulkanResourceState.DepthWrite);
        }
        _tracker.TransitionBatch(_commandBuffer, transitions.AsSpan(0, attachmentCount));

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
                imageLayout = _tracker.LayoutForTexture((VulkanTexture)colors[i], VulkanResourceState.ColorAttachment),
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
        _tracker.BeginComputeScope();
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
        // close the dispatch scope: barriers queued by the last binds flushed
        // before the last dispatch; leftover dispatch scopes die with the pass
        // (the next usage point emits its own precise edge)
        _tracker.EndComputeScope();

        if (_pendingEndTimestamp.HasValue)
        {
            (VulkanTimestampQuerySet set, uint index) = _pendingEndTimestamp.Value;
            vkCmdWriteTimestamp2(_commandBuffer, VkPipelineStageFlags2.BottomOfPipe, set.Native, index);
            _pendingEndTimestamp = null;
        }
        // backstop for anything queued after the last dispatch
        _tracker.FlushPendingBarriers(_commandBuffer);
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
        // stencil reference is pass state in the wgpu model: it persists across
        // pipeline binds until SetStencilReference changes it (matching WebGPU)
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
        BindGraphicsResourcesNative(_commandBuffer, pipeline, _tracker, slot, (VulkanResourceGroup)resourceGroup);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;
        VkBuffer nativeBuffer = bufferImpl.Native;
        ulong deviceOffset = offset;
        vkCmdBindVertexBuffers(_commandBuffer, slot, 1, &nativeBuffer, &deviceOffset);
        _tracker.MarkBuffer(bufferImpl, VulkanResourceState.VertexRead);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;
        vkCmdBindIndexBuffer(_commandBuffer, bufferImpl.Native, offset, VulkanUtility.IndexFormatToVulkan(format));
        _tracker.MarkBuffer(bufferImpl, VulkanResourceState.IndexRead);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDraw(_commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDrawIndexed(_commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        _tracker.MarkBuffer(buffer, VulkanResourceState.IndirectRead);
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDrawIndirect(_commandBuffer, buffer.Native, offset, 1, (uint)sizeof(VkDrawIndirectCommand));
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        _tracker.MarkBuffer(buffer, VulkanResourceState.IndirectRead);
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDrawIndexedIndirect(_commandBuffer, buffer.Native, offset, 1, (uint)sizeof(VkDrawIndexedIndirectCommand));
    }

    protected override void MultiDrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        _tracker.MarkBuffer(buffer, VulkanResourceState.IndirectRead);
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDrawIndexedIndirect(_commandBuffer, buffer.Native, offset, drawCount, (uint)sizeof(VkDrawIndexedIndirectCommand));
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
        BindComputeResourcesNative(_commandBuffer, pipeline, _tracker, slot, (VulkanResourceGroup)resourceGroup);
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        // make the previous dispatch's writes visible to this one (bind-time
        // state flips queue their barriers here; no-op when nothing flipped)
        _tracker.FlushDispatchBarriers(_commandBuffer);
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdDispatch(_commandBuffer, x, y, z);
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        VulkanBuffer buffer = (VulkanBuffer)indirectBuffer;
        _tracker.MarkBuffer(buffer, VulkanResourceState.IndirectRead);
        _tracker.FlushDispatchBarriers(_commandBuffer);
        _tracker.FlushPendingBarriers(_commandBuffer);
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
        if (bundleImpl.IsEmpty)
        {
            // a finished empty bundle executes as a no-op (WebGPU semantics)
            return;
        }

        // replay through a cached secondary command buffer: one driver call per
        // bundle instead of re-recording every command from C# each execute
        // (mirrors wgpu's native render bundle execution)
        VkCommandBuffer secondary = bundleImpl.GetOrRecordSecondary(
            _device, this, _currentFrameBuffer!, _currentViewport, _currentScissor, _currentStencilReference,
            out bool reRecorded);
        if (!reRecorded)
        {
            // a cached replay recorded no marks this time; apply them now so
            // bundle-bound resources get the same hazard edges direct calls record
            bundleImpl.ApplyTrackerMarks(_tracker);
        }
        // barriers belong in the primary: the secondary runs inside the open
        // rendering scope, so every transition the bundle's binds queued flushes
        // here, before the execute
        _tracker.FlushPendingBarriers(_commandBuffer);
        vkCmdExecuteCommands(_commandBuffer, 1, &secondary);
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
        VulkanResourceTracker tracker,
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

        // bind-time hazard edges queue as precise barriers and flush before the
        // next draw (see VulkanResourceTracker.MarkBuffer/MarkTexture)
        IReadOnlyList<VulkanBuffer> buffers = group.BoundBuffers;
        IReadOnlyList<VulkanResourceState> bufferStates = group.BoundBufferStates;
        for (int i = 0; i < buffers.Count; i++)
        {
            tracker.MarkBuffer(buffers[i], bufferStates[i]);
        }

        IReadOnlyList<VulkanTextureView> views = group.BoundViews;
        IReadOnlyList<VulkanResourceState> viewStates = group.BoundViewStates;
        for (int i = 0; i < views.Count; i++)
        {
            tracker.MarkTexture(views[i].TextureRef, viewStates[i]);
        }
    }

    /// <summary>Core of a compute resource bind (also used by bundle replay).</summary>
    internal static void BindComputeResourcesNative(
        VkCommandBuffer commandBuffer,
        VulkanPipeline pipeline,
        VulkanResourceTracker tracker,
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
            tracker.MarkBuffer(buffers[i], bufferStates[i]);
        }

        IReadOnlyList<VulkanTextureView> views = group.BoundViews;
        IReadOnlyList<VulkanResourceState> viewStates = group.BoundViewStates;
        for (int i = 0; i < views.Count; i++)
        {
            tracker.MarkTexture(views[i].TextureRef, viewStates[i]);
        }
    }

    // ===== out-of-pass copies =====

    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        VulkanBuffer srcImpl = (VulkanBuffer)src;
        VulkanBuffer dstImpl = (VulkanBuffer)dst;

        _tracker.TransitionBuffer(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _tracker.TransitionBuffer(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        VkBufferCopy copy = new()
        {
            srcOffset = srcOffset,
            dstOffset = dstOffset,
            size = size,
        };
        vkCmdCopyBuffer(_commandBuffer, srcImpl.Native, dstImpl.Native, 1, &copy);

        // the destination stays in CopyDst; the next usage's transition makes
        // the write visible (no eager drain)
    }

    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect)
    {
        VulkanBuffer srcImpl = (VulkanBuffer)src;
        VulkanTexture dstImpl = (VulkanTexture)dst;

        _tracker.TransitionBuffer(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _tracker.TransitionTexture(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        VkImageAspectFlags imageAspect = VulkanUtility.AspectToVulkan(aspect, dstImpl.VkFormat);
        (uint width, uint height, uint depthOrLayers, ulong layerStride) = MipTransferLayout(dstImpl, mipLevel, imageAspect);

        // the buffer holds the mip tightly packed; all array layers in sequence
        VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(
            mipLevel, width, height, depthOrLayers, imageAspect, offset, dstImpl.Is3D);
        vkCmdCopyBufferToImage(_commandBuffer, srcImpl.Native, dstImpl.Image,
            _tracker.LayoutForTexture(dstImpl, VulkanResourceState.CopyDst), 1, &copy);

        // the image stays in CopyDst (TRANSFER_DST layout); the next usage
        // transitions it out with the precise edge
    }

    protected override void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect)
    {
        VulkanTexture srcImpl = (VulkanTexture)src;
        VulkanTexture dstImpl = (VulkanTexture)dst;

        _tracker.TransitionTexture(_commandBuffer, srcImpl, VulkanResourceState.CopySrc);
        _tracker.TransitionTexture(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

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
                // 3D images carry the slice count in extent.depth; everything else
                // covers slices through the subresource layer count
                depth = srcImpl.Is3D ? uint.Min(srcImpl.MipDepth(srcMipLevel), dstImpl.MipDepth(dstMipLevel)) : 1,
            },
        };
        vkCmdCopyImage(_commandBuffer,
            srcImpl.Image, _tracker.LayoutForTexture(srcImpl, VulkanResourceState.CopySrc),
            dstImpl.Image, _tracker.LayoutForTexture(dstImpl, VulkanResourceState.CopyDst), 1, &copy);

        // both images stay in their transfer states; the next usage transitions
    }

    protected override void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset)
    {
        VulkanBuffer dstImpl = (VulkanBuffer)destination;
        _tracker.TransitionBuffer(_commandBuffer, dstImpl, VulkanResourceState.CopyDst);

        vkCmdCopyQueryPoolResults(
            _commandBuffer,
            ((VulkanTimestampQuerySet)querySet).Native,
            firstQuery,
            queryCount,
            dstImpl.Native,
            destinationOffset,
            sizeof(ulong),
            VkQueryResultFlags.Bit64);

        // the destination stays in CopyDst; the readback's CopySrc transition
        // (or any later use) covers visibility of the resolve writes

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
