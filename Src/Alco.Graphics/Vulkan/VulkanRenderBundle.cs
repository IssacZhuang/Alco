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
    private GPUAttachmentLayout? _bundleLayout;

    // resources the bundle binds (built at EndCore); their combined source
    // scope is unioned into the pass flush when the bundle executes so
    // bundle-bound resources are hazard-covered without per-resource marks
    private List<(object Resource, VulkanResourceState State)> _recordedTouched = new();
    private List<(object Resource, VulkanResourceState State)> _recordingTouched = new();
    private VkPipelineStageFlags2 _recordedScopeStage = VkPipelineStageFlags2.TopOfPipe;
    private VkAccessFlags2 _recordedScopeAccess = VkAccessFlags2.None;

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

    public override bool HasBuffer => _recordedCommands.Count > 0;

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
        _recordedScopeStage = VkPipelineStageFlags2.TopOfPipe;
        _recordedScopeAccess = VkAccessFlags2.None;
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
    }

    private void TryAddTouched(
        HashSet<(object Resource, VulkanResourceState State)> seen,
        object resource,
        VulkanResourceState state)
    {
        if (seen.Add((resource, state)))
        {
            _recordedTouched.Add((resource, state));
            (VkPipelineStageFlags2 stage, VkAccessFlags2 access) =
                VulkanResourceTracker.ScopeOf(resource, state);
            _recordedScopeStage |= stage;
            _recordedScopeAccess |= access;
        }
    }

    /// <summary>Unions the recorded bundle's resource scope into the executing
    /// pass's flush barrier. Called at execute time; bundle-bound resources take
    /// part in automatic hazard tracking without per-resource tracker marks.</summary>
    internal void ApplyTrackerMarks(VulkanResourceTracker tracker)
    {
        tracker.UnionBundleScope(_recordedScopeStage, _recordedScopeAccess);
    }

    /// <summary>Returns a cached secondary command buffer that replays this
    /// bundle, keyed by the executing primary (see <see cref="CachedSecondary"/>).
    /// Dynamic rendering secondaries start with undefined dynamic state, so the
    /// viewport/scissor/stencil state is baked into the recording.</summary>
    internal VkCommandBuffer GetOrRecordSecondary(
        VulkanDevice device,
        VulkanCommandBuffer primary,
        VulkanFrameBufferBase frameBuffer,
        VkViewport viewport,
        VkRect2D scissor,
        uint stencilReference)
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
        target.Primary = primary;
        target.FrameStamp = frame;
        target.Width = width;
        target.Height = height;
        target.StencilReference = stencilReference;
        target.Recorded = true;
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
                    tracker.TouchInPass(command.Buffer);
                    break;
                }

                case CommandKind.IndexBuffer:
                {
                    tracker.MarkBuffer(command.Buffer!, VulkanResourceState.IndexRead);
                    tracker.TouchInPass(command.Buffer);
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
                    tracker.TouchInPass(command.Buffer);
                    vkCmdDrawIndirect(commandBuffer, command.Buffer!.Native, command.Offset, 1, (uint)sizeof(VkDrawIndirectCommand));
                    break;
                }

                case CommandKind.DrawIndexedIndirect:
                {
                    tracker.MarkBuffer(command.Buffer!, VulkanResourceState.IndirectRead);
                    tracker.TouchInPass(command.Buffer);
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

        byte[] copy = new byte[size];
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
