using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// A reusable render bundle. Vulkan forbids barriers inside a rendering scope, so
/// secondary command buffers cannot be used for bundles whose resources need
/// bind-time synchronization. Instead the bundle records its bind/draw commands as
/// a replay list; at <c>ExecuteBundle</c> time the list is re-executed on the
/// primary command buffer through the exact same code paths as direct calls, so
/// resources bound by a bundle participate in automatic barrier tracking.
/// </summary>
internal sealed unsafe class VulkanRenderBundle : GPURenderBundle
{
    private enum CommandKind : byte
    {
        Pipeline,
        Resources,
        VertexBuffer,
        IndexBuffer,
        Draw,
        DrawIndexed,
        DrawIndirect,
        DrawIndexedIndirect,
        PushConstants,
    }

    private struct BundleCommand
    {
        public CommandKind Kind;
        public VulkanPipeline? Pipeline;
        public VulkanResourceGroup? Group;
        public VulkanBuffer? Buffer;
        public uint Slot;
        public uint A, B, C, D;      // draw arguments
        public ulong Offset;
        public ulong Size;
        public IndexFormat IndexFormat;
        public uint PushOffset;
        public int PushDataId;
    }

    private readonly VulkanDevice _device;
    private List<BundleCommand> _recordingCommands = new();
    private List<BundleCommand> _recordedCommands = new();
    private List<byte[]> _recordingPushData = new();
    private List<byte[]> _recordedPushData = new();
    // recycled push-constant payloads: a bundle that re-records every frame
    // (the debug overlay, dynamic UI content) would otherwise allocate one
    // fresh byte[] per push constant on every recording
    private readonly List<byte[]> _pushDataFree = new();
    private GPUAttachmentLayout? _bundleLayout;

    // resources the bundle binds (built at EndCore); applied to the executing
    // primary's tracker on cached executes so bundle-bound resources get the
    // same hazard edges a direct call would record
    private List<(object Resource, VulkanResourceState State)> _recordedTouched = new();
    private List<(object Resource, VulkanResourceState State)> _recordingTouched = new();

    // _recordedTouched split by hazard kind (rebuilt at EndCore):
    // - read-state entries carry a per-entry content-version stamp: an entry
    //   whose version is unchanged since its mark last ran provably replays as
    //   a no-op (marks only act on state changes, and every state change bumps
    //   the resource's version), so cached executes skip it. This is what keeps
    //   scene bundles with thousands of material resources cheap to execute.
    // - write-state entries are always re-marked: their per-scope
    //   write-after-write edges are not version-tracked.
    private readonly List<(object Resource, VulkanResourceState State)> _recordedReads = new();
    private readonly List<(object Resource, VulkanResourceState State)> _recordedWrites = new();
    private readonly List<long> _readVersions = new();

    // dirty-log fast path for cached executes (built at EndCore alongside the
    // read split): instead of version-checking every read entry, intersect the
    // tracker's dirty log (resources whose state changed since the cursor) with
    // the read set — stable material textures then cost zero iterations.
    // _readIndexByResource maps a resource to the HEAD of a _readNext chain:
    // the same resource may hold several read entries (different states), and a
    // dirty hit must re-mark all of them.
    private readonly Dictionary<object, int> _readIndexByResource = new();
    private readonly List<int> _readNext = new();
    private readonly List<int> _dirtyHits = new();
    private long _dirtyCursorFrame = -1;
    private int _dirtyCursorIndex;

    // whether End() ever finished a recording. Matches WebGPU's HasBuffer: a
    // finished empty bundle is valid and executes as a no-op; only a bundle
    // that was never recorded rejects execution.
    private bool _hasRecorded;

    // cached replay through secondary command buffers, keyed by executing
    // primary: a secondary without SIMULTANEOUS_USE may pend in only one
    // primary and must not be reset while pending. Keying by the primary
    // makes both trivially true — each primary waits on its own in-flight
    // fence at Begin(), so entries of that primary are always free while it
    // records (mirrors wgpu's native render bundle execution without the
    // driver's copy-on-execute penalty of simultaneous-use buffers).
    private sealed class CachedSecondary
    {
        public VkCommandBuffer CommandBuffer;
        public VulkanCommandBuffer? Primary;
        public long FrameStamp;
        public uint Width;
        public uint Height;
        public uint StencilReference;
        public bool Recorded;
    }
    private readonly List<CachedSecondary> _secondaries = new();

    private VulkanPipeline? _recordingPipeline;

    protected override VulkanDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public override bool HasBuffer => _hasRecorded;

    /// <summary>Whether the recorded content is empty (a finished empty bundle
    /// executes as a no-op; execution is skipped entirely).</summary>
    internal bool IsEmpty => _recordedCommands.Count == 0;

    public VulkanRenderBundle(VulkanDevice device, in RenderBundleDescriptor? descriptor)
        : base(descriptor)
    {
        _device = device;
    }

    protected override void BeginCore(GPUAttachmentLayout attachmentLayout)
    {
        // the attachment layout only validates compatibility (dynamic rendering
        // needs no native object); bundles must replay into a compatible pass
        _recordingCommands.Clear();
        _recordingPushData.Clear();
        _recordingTouched.Clear();
        _recordingPipeline = null;
        _bundleLayout = attachmentLayout;

        // the previous recorded content becomes stale: drop the cached
        // recordings (the native command buffers are only reset lazily at
        // their next use by the same primary, which fences them out via its
        // own in-flight fence)
        foreach (CachedSecondary secondary in _secondaries)
        {
            secondary.Recorded = false;
        }
    }

    protected override void EndCore()
    {
        // the recording is now valid content even when empty
        _hasRecorded = true;

        // swap: the recording list becomes the recorded list and the previous
        // recorded list is recycled as the next recording buffer
        List<BundleCommand> previousCommands = _recordedCommands;
        List<byte[]> previousPushData = _recordedPushData;
        List<(object, VulkanResourceState)> previousTouched = _recordedTouched;
        _recordedCommands = _recordingCommands;
        _recordedPushData = _recordingPushData;
        _recordedTouched = _recordingTouched;
        _recordingCommands = previousCommands;
        _recordingCommands.Clear();
        // the retired recording's payloads are no longer referenced by any
        // live list (secondaries baked their bytes at execute time), so they
        // go back to the pool instead of the GC heap
        _pushDataFree.AddRange(previousPushData);
        _recordingPushData = previousPushData;
        _recordingPushData.Clear();
        _recordingTouched = previousTouched;
        _recordingTouched.Clear();
        _recordingPipeline = null;

        // collect the resources the bundle binds so execute-time can apply the
        // bind-time tracker marks without replaying every command from C#.
        // Deduped: shared per-frame buffers are bound by every segment, and the
        // tracker only needs one scope contribution per (resource, state).
        _recordedTouched.Clear();
        HashSet<(object Resource, VulkanResourceState State)> seen = new();
        foreach (BundleCommand command in _recordedCommands)
        {
            switch (command.Kind)
            {
                case CommandKind.VertexBuffer:
                    TryAddTouched(seen, command.Buffer!, VulkanResourceState.VertexRead);
                    break;
                case CommandKind.IndexBuffer:
                    TryAddTouched(seen, command.Buffer!, VulkanResourceState.IndexRead);
                    break;
                case CommandKind.DrawIndirect:
                case CommandKind.DrawIndexedIndirect:
                    TryAddTouched(seen, command.Buffer!, VulkanResourceState.IndirectRead);
                    break;
                case CommandKind.Resources:
                {
                    VulkanResourceGroup group = command.Group!;
                    IReadOnlyList<VulkanBuffer> buffers = group.BoundBuffers;
                    IReadOnlyList<VulkanResourceState> bufferStates = group.BoundBufferStates;
                    for (int i = 0; i < buffers.Count; i++)
                    {
                        TryAddTouched(seen, buffers[i], bufferStates[i]);
                    }
                    IReadOnlyList<VulkanTextureView> views = group.BoundViews;
                    IReadOnlyList<VulkanResourceState> viewStates = group.BoundViewStates;
                    for (int i = 0; i < views.Count; i++)
                    {
                        TryAddTouched(seen, views[i].TextureRef, viewStates[i]);
                    }
                    break;
                }
            }
        }

        // split by hazard kind; fresh version stamps force one full mark pass
        _recordedReads.Clear();
        _recordedWrites.Clear();
        _readVersions.Clear();
        _readIndexByResource.Clear();
        _readNext.Clear();
        _dirtyCursorFrame = -1;
        _dirtyCursorIndex = 0;
        foreach ((object resource, VulkanResourceState state) in _recordedTouched)
        {
            if (VulkanResourceTracker.IsWriteState(state))
            {
                _recordedWrites.Add((resource, state));
            }
            else
            {
                int index = _recordedReads.Count;
                _readNext.Add(_readIndexByResource.TryGetValue(resource, out int head) ? head : -1);
                _readIndexByResource[resource] = index;
                _recordedReads.Add((resource, state));
                _readVersions.Add(-1); // never matches a real version
            }
        }
    }

    private void TryAddTouched(
        HashSet<(object Resource, VulkanResourceState State)> seen,
        object resource,
        VulkanResourceState state)
    {
        if (seen.Add((resource, state)))
        {
            _recordedTouched.Add((resource, state));
        }
    }

    /// <summary>Applies the recorded bundle's bind marks to the executing
    /// primary's tracker so bundle-bound resources get the same hazard edges a
    /// direct call would record. Skipped when the execute just re-recorded the
    /// cached secondary (the replay already marked everything).</summary>
    internal void ApplyTrackerMarks(VulkanResourceTracker tracker)
    {
        List<(object Resource, VulkanResourceState State)> reads = _recordedReads;

        // fast path: re-mark only the read entries whose resource the dirty log
        // saw change since the last pass (the log scan collects hits under its
        // lock; the marks run after, keeping lock order tracker -> dirty log)
        List<int> hits = _dirtyHits;
        hits.Clear();
        long cursorFrame = _dirtyCursorFrame;
        int cursorIndex = _dirtyCursorIndex;
        if (VulkanResourceTracker.ScanDirtyLog(_readIndexByResource, hits, ref cursorFrame, ref cursorIndex))
        {
            _dirtyCursorFrame = cursorFrame;
            _dirtyCursorIndex = cursorIndex;
            foreach (int head in hits)
            {
                for (int i = head; i >= 0; i = _readNext[i])
                {
                    (object resource, VulkanResourceState state) = reads[i];
                    MarkOne(tracker, resource, state);
                    _readVersions[i] = VulkanResourceTracker.VersionOf(resource);
                }
            }
        }
        else
        {
            // fallback (first pass, or the cursor fell out of the ring):
            // version-check every read entry — an entry whose version has not
            // moved since its mark last ran provably replays as a no-op
            for (int i = 0; i < reads.Count; i++)
            {
                (object resource, VulkanResourceState state) = reads[i];
                if (VulkanResourceTracker.VersionOf(resource) == _readVersions[i])
                {
                    continue;
                }
                MarkOne(tracker, resource, state);
                _readVersions[i] = VulkanResourceTracker.VersionOf(resource);
            }
            VulkanResourceTracker.DirtyLogCursor(out _dirtyCursorFrame, out _dirtyCursorIndex);
        }

        // write-state entries: per-scope WAW edges are not version-tracked
        List<(object Resource, VulkanResourceState State)> writes = _recordedWrites;
        for (int i = 0; i < writes.Count; i++)
        {
            MarkOne(tracker, writes[i].Resource, writes[i].State);
        }
    }

    private static void MarkOne(VulkanResourceTracker tracker, object resource, VulkanResourceState state)
    {
        if (resource is VulkanTexture texture)
        {
            tracker.MarkTexture(texture, state);
        }
        else if (resource is VulkanBuffer buffer)
        {
            tracker.MarkBuffer(buffer, state);
        }
    }

    /// <summary>Returns a cached secondary command buffer that replays this
    /// bundle, keyed by the executing primary (see <see cref="CachedSecondary"/>).
    /// Dynamic rendering secondaries start with undefined dynamic state, so the
    /// viewport/scissor/stencil state is baked into the recording.
    /// <paramref name="reRecorded"/> reports whether the secondary was recorded
    /// fresh this call (its replay already applied the tracker marks).</summary>
    internal VkCommandBuffer GetOrRecordSecondary(
        VulkanDevice device,
        VulkanCommandBuffer primary,
        VulkanFrameBufferBase frameBuffer,
        VkViewport viewport,
        VkRect2D scissor,
        uint stencilReference,
        out bool reRecorded)
    {
        long frame = VulkanDevice.FrameCounter;
        uint width = (uint)scissor.extent.width;
        uint height = (uint)scissor.extent.height;

        foreach (CachedSecondary cached in _secondaries)
        {
            if (cached.Recorded
                && cached.Primary == primary
                && cached.FrameStamp != frame // not already executed this frame
                && cached.Width == width
                && cached.Height == height
                && cached.StencilReference == stencilReference)
            {
                cached.FrameStamp = frame;
                reRecorded = false;
                return cached.CommandBuffer;
            }
        }

        // re-record. Only entries belonging to this primary are reusable: while
        // this primary records, its fence has already signaled, so anything it
        // executed before is off the GPU. Entries of other primaries may still
        // be pending there and must not be reset.
        CachedSecondary? target = null;
        foreach (CachedSecondary cached in _secondaries)
        {
            if (cached.Primary == primary && cached.FrameStamp != frame)
            {
                target = cached;
                break;
            }
        }
        if (target == null)
        {
            foreach (CachedSecondary cached in _secondaries)
            {
                if (cached.Primary == null) // never used
                {
                    target = cached;
                    break;
                }
            }
        }
        if (target == null)
        {
            target = new CachedSecondary
            {
                CommandBuffer = device.AllocateSecondaryCommandBuffer(),
            };
            _secondaries.Add(target);
        }

        RecordSecondary(device, primary.Tracker, target, viewport, scissor, stencilReference);
        // the replay just applied every mark; stamp the dirty cursor first so
        // bumps racing with the version stamps below land AFTER the cursor and
        // are re-marked by the next execute's scan (never silently skipped)
        VulkanResourceTracker.DirtyLogCursor(out _dirtyCursorFrame, out _dirtyCursorIndex);
        // stamp the read versions so the next cached execute can skip the
        // no-op ones
        for (int i = 0; i < _recordedReads.Count; i++)
        {
            _readVersions[i] = VulkanResourceTracker.VersionOf(_recordedReads[i].Resource);
        }
        target.Primary = primary;
        target.FrameStamp = frame;
        target.Width = width;
        target.Height = height;
        target.StencilReference = stencilReference;
        target.Recorded = true;
        reRecorded = true;
        return target.CommandBuffer;
    }

    private unsafe void RecordSecondary(
        VulkanDevice device,
        VulkanResourceTracker tracker,
        CachedSecondary cached,
        VkViewport viewport,
        VkRect2D scissor,
        uint stencilReference)
    {
        VulkanAttachmentLayout? layout = _bundleLayout as VulkanAttachmentLayout;
        if (layout == null)
        {
            throw new GraphicsException($"Render bundle '{Name}' has no attachment layout; call Begin first.");
        }

        ReadOnlySpan<ColorAttachment> colors = layout.ColorAttachments;
        VkFormat* colorFormats = stackalloc VkFormat[Math.Max(1, colors.Length)];
        for (int i = 0; i < colors.Length; i++)
        {
            colorFormats[i] = VulkanUtility.PixelFormatToVulkan(colors[i].Format);
        }
        VkFormat depthFormat = VkFormat.Undefined;
        VkFormat stencilFormat = VkFormat.Undefined;
        if (layout.DepthInfo is DepthAttachment depth)
        {
            VkFormat format = VulkanUtility.PixelFormatToVulkan(depth.Format);
            depthFormat = format;
            if (VulkanUtility.HasStencil(format))
            {
                stencilFormat = format;
            }
        }

        VkCommandBufferInheritanceRenderingInfo inheritedRendering = new()
        {
            colorAttachmentCount = (uint)colors.Length,
            pColorAttachmentFormats = colorFormats,
            depthAttachmentFormat = depthFormat,
            stencilAttachmentFormat = stencilFormat,
            rasterizationSamples = VkSampleCountFlags.Count1,
        };
        VkCommandBufferInheritanceInfo inheritanceInfo = new()
        {
            pNext = &inheritedRendering,
        };
        VkCommandBufferBeginInfo beginInfo = new()
        {
            // no SimultaneousUse: the per-primary keying guarantees the secondary
            // is only ever pending inside its own primary (see CachedSecondary);
            // avoiding the flag lets the driver link the buffer by reference
            // instead of copying its commands on every execute
            flags = VkCommandBufferUsageFlags.RenderPassContinue,
            pInheritanceInfo = &inheritanceInfo,
        };

        _ = vkResetCommandBuffer(cached.CommandBuffer, VkCommandBufferResetFlags.None);
        vkBeginCommandBuffer(cached.CommandBuffer, &beginInfo).ThrowOnFailure();
        vkCmdSetViewport(cached.CommandBuffer, 0, 1, &viewport);
        vkCmdSetScissor(cached.CommandBuffer, 0, 1, &scissor);
        vkCmdSetStencilReference(cached.CommandBuffer, VkStencilFaceFlags.FrontAndBack, stencilReference);
        Replay(cached.CommandBuffer, tracker);
        vkEndCommandBuffer(cached.CommandBuffer).ThrowOnFailure();
    }

    /// <summary>Replays the recorded commands into an open render pass; tracker
    /// marks land in the executing primary's recording tracker.</summary>
    internal void Replay(VkCommandBuffer commandBuffer, VulkanResourceTracker tracker)
    {
        VulkanPipeline? currentPipeline = null;
        List<byte[]> pushData = _recordedPushData;

        foreach (BundleCommand command in _recordedCommands)
        {
            switch (command.Kind)
            {
                case CommandKind.Pipeline:
                    currentPipeline = command.Pipeline;
                    vkCmdBindPipeline(commandBuffer, VkPipelineBindPoint.Graphics, command.Pipeline!.Native);
                    break;

                case CommandKind.Resources:
                    VulkanCommandBuffer.BindGraphicsResourcesNative(
                        commandBuffer, currentPipeline!, tracker, command.Slot, command.Group!);
                    break;

                case CommandKind.VertexBuffer:
                {
                    VkBuffer buffer = command.Buffer!.Native;
                    ulong offset = command.Offset;
                    vkCmdBindVertexBuffers(commandBuffer, command.Slot, 1, &buffer, &offset);
                    tracker.MarkBuffer(command.Buffer, VulkanResourceState.VertexRead);
                    break;
                }

                case CommandKind.IndexBuffer:
                {
                    tracker.MarkBuffer(command.Buffer!, VulkanResourceState.IndexRead);
                    vkCmdBindIndexBuffer(
                        commandBuffer,
                        command.Buffer!.Native,
                        command.Offset,
                        VulkanUtility.IndexFormatToVulkan(command.IndexFormat));
                    break;
                }

                case CommandKind.Draw:
                    vkCmdDraw(commandBuffer, command.A, command.B, command.C, command.D);
                    break;

                case CommandKind.DrawIndexed:
                    vkCmdDrawIndexed(commandBuffer, command.A, command.B, command.C, (int)command.Offset, command.D);
                    break;

                case CommandKind.DrawIndirect:
                {
                    tracker.MarkBuffer(command.Buffer!, VulkanResourceState.IndirectRead);
                    vkCmdDrawIndirect(commandBuffer, command.Buffer!.Native, command.Offset, 1, (uint)sizeof(VkDrawIndirectCommand));
                    break;
                }

                case CommandKind.DrawIndexedIndirect:
                {
                    tracker.MarkBuffer(command.Buffer!, VulkanResourceState.IndirectRead);
                    vkCmdDrawIndexedIndirect(commandBuffer, command.Buffer!.Native, command.Offset, 1, (uint)sizeof(VkDrawIndexedIndirectCommand));
                    break;
                }

                case CommandKind.PushConstants:
                {
                    byte[] data = pushData[command.PushDataId];
                    fixed (byte* ptr = data)
                    {
                        vkCmdPushConstants(
                            commandBuffer,
                            currentPipeline!.NativeLayout,
                            currentPipeline.PushConstantStages,
                            command.PushOffset,
                            (uint)data.Length,
                            ptr);
                    }
                    break;
                }
            }
        }
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        VulkanPipeline pipelineImpl = (VulkanPipeline)pipeline;
        _recordingPipeline = pipelineImpl;
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.Pipeline,
            Pipeline = pipelineImpl,
        });
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        if (_recordingPipeline == null)
        {
            throw new GraphicsException("SetResources requires a bound graphics pipeline (render bundle recording).");
        }
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.Resources,
            Pipeline = _recordingPipeline,
            Slot = slot,
            Group = (VulkanResourceGroup)resourceGroup,
        });
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.VertexBuffer,
            Slot = slot,
            Buffer = (VulkanBuffer)buffer,
            Offset = offset,
            Size = size,
        });
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.IndexBuffer,
            Buffer = (VulkanBuffer)buffer,
            Offset = offset,
            Size = size,
            IndexFormat = format,
        });
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.Draw,
            A = vertexCount,
            B = instanceCount,
            C = firstVertex,
            D = firstInstance,
        });
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.DrawIndexed,
            A = indexCount,
            B = instanceCount,
            C = firstIndex,
            Offset = (ulong)vertexOffset,
            D = firstInstance,
        });
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.DrawIndirect,
            Buffer = (VulkanBuffer)indirectBuffer,
            Offset = offset,
        });
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.DrawIndexedIndirect,
            Buffer = (VulkanBuffer)indirectBuffer,
            Offset = offset,
        });
    }

    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        if (_recordingPipeline == null)
        {
            throw new GraphicsException("PushGraphicsConstants requires a bound graphics pipeline.");
        }

        // exact-size reuse only: Replay pushes data.Length bytes, so an
        // oversized buffer would append garbage. Push sizes are per-pipeline
        // constants, so exact hits are the norm after the first frame
        byte[] copy = null!;
        for (int i = 0; i < _pushDataFree.Count; i++)
        {
            if (_pushDataFree[i].Length == size)
            {
                copy = _pushDataFree[i];
                _pushDataFree.RemoveAt(i);
                break;
            }
        }
        if (copy == null)
        {
            copy = new byte[size];
        }
        fixed (byte* dst = copy)
        {
            Buffer.MemoryCopy(data, dst, size, size);
        }

        _recordingPushData.Add(copy);
        _recordingCommands.Add(new BundleCommand
        {
            Kind = CommandKind.PushConstants,
            PushOffset = bufferOffset,
            PushDataId = _recordingPushData.Count - 1,
        });
    }

    protected override void Dispose(bool disposing)
    {
        // bundles own no native objects; the replay list only references other GPU
        // resources whose lifetimes are managed externally
        _recordedCommands.Clear();
        _recordingCommands.Clear();
        _recordedPushData.Clear();
        _recordingPushData.Clear();
        _recordedTouched.Clear();
        _recordingTouched.Clear();
        _recordedReads.Clear();
        _recordedWrites.Clear();
        _readVersions.Clear();
        _readIndexByResource.Clear();
        _readNext.Clear();
        _dirtyHits.Clear();

        if (disposing)
        {
            // secondaries may still be pending in an in-flight primary; the
            // device's delayed disposal covers frames in flight
            foreach (CachedSecondary secondary in _secondaries)
            {
                _device.QueueSecondaryCommandBufferFree(secondary.CommandBuffer);
            }
            _secondaries.Clear();
        }
    }
}
