using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// How a resource is (or was last) used by the GPU. Mirrors wgpu's usage-scope
/// states: every resource carries exactly one state and transitions between
/// states are emitted as pipeline barriers automatically at usage points.
/// </summary>
internal enum VulkanResourceState : byte
{
    /// <summary>The resource has never been used (image is in the UNDEFINED layout).</summary>
    Undefined = 0,

    // ----- buffers -----
    VertexRead,
    IndexRead,
    UniformRead,
    /// <summary>Bound as a read/write storage buffer or storage image (the abstraction
    /// does not declare read-only-ness for storage buffers, so binding one always
    /// conservatively marks read-write).</summary>
    ShaderReadWrite,
    IndirectRead,
    CopySrc,
    CopyDst,

    // ----- images -----
    ColorAttachment,
    DepthWrite,
    DepthRead,
    /// <summary>Bound for sampling in a shader.</summary>
    ShaderRead,
    /// <summary>Bound as a write-only storage image.</summary>
    ShaderWrite,
    /// <summary>Queued for presentation (swapchain images only).</summary>
    Present,

    /// <summary>Image sits in GENERAL layout with no pending usage scope
    /// (set right after creation so first-use barriers see GENERAL).</summary>
    Idle,
}

/// <summary>
/// Tracks the last-known usage state of every GPU resource and records the Vulkan
/// pipeline barriers required to move resources between usages. This is the wgpu
/// "implicit synchronization" model: the caller never records barriers manually.
/// <para>
/// Threading model (mirrors wgpu): the DEVICE tracker is the authoritative
/// post-submission state, only mutated in submission order under the device's
/// queue lock. Every command buffer owns a PRIVATE recording tracker: while the
/// buffer records, its state map evolves in record order (which equals execution
/// order inside one command buffer), seeded lazily from the device tracker at
/// each resource's first use. At <see cref="SubmitCore"/> time — serialized in
/// submission order — the recording tracker is reconciled with the device
/// tracker: any resource whose device state drifted from the seed (another
/// command buffer was submitted in between) gets a prologue barrier emitted by a
/// tiny one-shot submission right before the recorded buffer, then the final
/// states are absorbed into the device tracker. This keeps concurrent recording
/// on any number of threads correct for arbitrary submit orders.
/// </para>
/// <para>
/// Layout policy (mirrors wgpu): attachment, transfer and present usages move the
/// image into their optimal layouts — this is what keeps depth compression and
/// present efficient. Sampling and storage usages keep the image in GENERAL:
/// binds recorded inside a rendering scope cannot emit layout transitions, so
/// every bindable texture must sit in GENERAL when a pass starts. <see cref="FlushPass"/>
/// restores attachments that left GENERAL back into it, and copies restore via
/// <see cref="RestoreImageToIdle"/>. Hazard handling:
/// - precise barriers at usage points outside passes (attachments on pass begin, copies);
/// - bind-time state updates inside passes without barriers (a resource has a single
///   usage per pass, matching wgpu usage scopes);
/// - one wide "pass flush" barrier per pass end, which makes all pass writes visible
///   to any later use and restores attachment layouts.
/// </para>
/// </summary>
internal sealed unsafe class VulkanResourceTracker
{
    /// <summary>First-use snapshot of one resource inside a recording context:
    /// the state this buffer's recorded barriers assume the resource starts in.</summary>
    private readonly struct FirstUse
    {
        public readonly object Resource;
        public readonly VulkanResourceState Seed;

        public FirstUse(object resource, VulkanResourceState seed)
        {
            Resource = resource;
            Seed = seed;
        }
    }

    // null on the device tracker; the parent a recording tracker seeds from
    private readonly VulkanResourceTracker? _parent;

    private readonly Dictionary<VulkanTexture, VulkanResourceState> _imageStates = new();
    private readonly Dictionary<VulkanBuffer, VulkanResourceState> _bufferStates = new();

    // first-use seeds of this recording context (buffers: empty on the device tracker)
    private readonly List<FirstUse> _firstUses = new();
    // prologue barrier scratch, reused per call (recording-safe: one tracker
    // per command buffer, recording is single-threaded per buffer)
    private readonly List<VkImageMemoryBarrier2> _prologueBarriers = new();

    // resources touched by the pass currently being recorded (cleared at pass end)
    private readonly HashSet<object> _passTouched = new();

    // device tracker: bumped on every queue submission / invalidation so recording
    // contexts can skip the submit-time scan when nothing was submitted in between;
    // recording trackers: the device serial observed at Reset() time
    private long _serial;

    // source scopes contributed by render-bundle executes since the last
    // FlushPass; folded into the pass-flush union barrier so bundle-bound
    // resources are hazard-covered without per-resource tracker marks
    private VkPipelineStageFlags2 _bundleSrcStage;
    private VkAccessFlags2 _bundleSrcAccess;

    // compute passes have no native pass object in Vulkan, so barriers ARE
    // legal between their dispatches (rendering scopes are the only ones that
    // forbid them). WebGPU gives every dispatch its own usage scope, so the
    // resources bound since the last dispatch are collected here and flushed
    // as one barrier right before the next dispatch (flood-fill ping-pong,
    // blur reading what fill wrote). _dispatchBinds holds the currently bound
    // resources and their states; _dispatchScopes holds the states the last
    // flushed barrier left them in (undefined = not touched by any dispatch
    // in this pass yet).
    private readonly Dictionary<object, VulkanResourceState> _dispatchBinds = new();
    private readonly Dictionary<object, VulkanResourceState> _dispatchScopes = new();
    private bool _inComputePass;

    // the device tracker is genuinely shared (seed reads from recording threads
    // race submit-time mutations) and keeps a monitor; recording trackers are
    // single-owner under the wgpu handoff contract (a command buffer moves
    // between threads only with user synchronization), so they skip the gate
    // entirely — the recording path is the hottest lock in the engine
    private readonly object? _gate;

    /// <summary>Creates the device-level (authoritative) tracker.</summary>
    public VulkanResourceTracker()
    {
        _gate = new object();
    }

    /// <summary>Creates a per-command-buffer recording tracker seeded from
    /// <paramref name="parent"/> (the device tracker).</summary>
    public VulkanResourceTracker(VulkanResourceTracker parent)
    {
        _parent = parent;
    }

    /// <summary>Acquires the shared-state gate when this tracker has one;
    /// recording trackers run lock-free.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LockedScope Lock() => new(_gate);

    private readonly struct LockedScope : IDisposable
    {
        private readonly object? _gate;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LockedScope(object? gate)
        {
            if (gate is not null)
            {
                Monitor.Enter(gate);
            }
            _gate = gate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_gate is not null)
            {
                Monitor.Exit(_gate);
            }
        }
    }

    public bool IsRecordingTracker => _parent != null;

    // ===== state queries =====

    public VulkanResourceState GetTextureState(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            if (_imageStates.TryGetValue(texture, out VulkanResourceState state))
            {
                return state;
            }
            VulkanResourceState seed = _parent != null
                ? _parent.GetTextureState(texture)
                : VulkanResourceState.Undefined;
            _imageStates[texture] = seed;
            RecordFirstUse(texture, seed);
            return seed;
        }
    }

    public VulkanResourceState GetBufferState(VulkanBuffer buffer)
    {
        using var __ = Lock();
        {
            if (_bufferStates.TryGetValue(buffer, out VulkanResourceState state))
            {
                return state;
            }
            VulkanResourceState seed = _parent != null
                ? _parent.GetBufferState(buffer)
                : VulkanResourceState.Undefined;
            _bufferStates[buffer] = seed;
            RecordFirstUse(buffer, seed);
            return seed;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordFirstUse(object resource, VulkanResourceState seed)
    {
        if (_parent != null)
        {
            _firstUses.Add(new FirstUse(resource, seed));
        }
    }

    /// <summary>Forgets the tracked state (e.g. a swapchain image that just came back
    /// from present with undefined contents). Device tracker only.</summary>
    public void InvalidateTexture(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            _imageStates[texture] = VulkanResourceState.Undefined;
            _serial++;
        }
    }

    public void Remove(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            _imageStates.Remove(texture);
            _serial++;
        }
    }

    public void Remove(VulkanBuffer buffer)
    {
        using var __ = Lock();
        {
            _bufferStates.Remove(buffer);
            _serial++;
        }
    }

    /// <summary>Bumps the device tracker's submission serial (call once per
    /// vkQueueSubmit that may have absorbed states). Device tracker only.</summary>
    public void BumpSerial()
    {
        using var __ = Lock();
        {
            _serial++;
        }
    }

    // ===== recording-time transitions =====

    /// <summary>
    /// Records a barrier transitioning <paramref name="texture"/> into
    /// <paramref name="target"/> and updates the tracked state. Only call from outside
    /// a render/compute pass (attachment entry, copies, present).
    /// </summary>
    public void TransitionTexture(VkCommandBuffer commandBuffer, VulkanTexture texture, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetTextureStateUnlocked(texture);
            if (source == target)
            {
                return;
            }

            (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(source);
            (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = ImageScope(target);
            if (target == VulkanResourceState.Present)
            {
                // the present transition may be recorded in a submission whose
                // barrier context cannot know the image's real last use (the surface
                // pass interleaves with the main frame buffer), so wait for
                // everything rather than trusting the tracked scope
                srcStage = VkPipelineStageFlags2.AllCommands;
                srcAccess = VkAccessFlags2.MemoryWrite | VkAccessFlags2.MemoryRead;
            }

            VkImageMemoryBarrier2 barrier = BuildImageBarrier(texture, source, target, srcStage, srcAccess, dstStage, dstAccess);

            VkDependencyInfo dependency = new()
            {
                imageMemoryBarrierCount = 1,
                pImageMemoryBarriers = &barrier,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);

            _imageStates[texture] = target;
        }
    }

    /// <summary>
    /// Records a barrier transitioning <paramref name="buffer"/> into
    /// <paramref name="target"/> and updates the tracked state.
    /// </summary>
    public void TransitionBuffer(VkCommandBuffer commandBuffer, VulkanBuffer buffer, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetBufferStateUnlocked(buffer);
            if (source == target)
            {
                return;
            }

            (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = BufferScope(source);
            (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = BufferScope(target);

            VkMemoryBarrier2 barrier = new()
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = dstStage,
                dstAccessMask = dstAccess,
            };

            VkDependencyInfo dependency = new()
            {
                memoryBarrierCount = 1,
                pMemoryBarriers = &barrier,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);

            _bufferStates[buffer] = target;
        }
    }

    /// <summary>One texture transition request for <see cref="TransitionBatch"/>.</summary>
    public readonly struct BatchTransition
    {
        public readonly VulkanTexture Texture;
        public readonly VulkanResourceState Target;

        public BatchTransition(VulkanTexture texture, VulkanResourceState target)
        {
            Texture = texture;
            Target = target;
        }
    }

    /// <summary>
    /// Transitions a set of textures (the incoming pass attachments) in ONE barrier
    /// command. Attachments leave the GENERAL idle layout, so every texture that
    /// changes state needs a real per-image barrier; they all fold into a single
    /// vkCmdPipelineBarrier2.
    /// </summary>
    public void TransitionBatch(VkCommandBuffer commandBuffer, ReadOnlySpan<BatchTransition> targets)
    {
        if (targets.Length == 0)
        {
            return;
        }

        using var __ = Lock();
        {
            int changeCount = 0;
            foreach (BatchTransition t in targets)
            {
                if (GetTextureStateUnlocked(t.Texture) != t.Target)
                {
                    changeCount++;
                }
            }
            if (changeCount == 0)
            {
                return;
            }

            VkImageMemoryBarrier2* imageBarriers = stackalloc VkImageMemoryBarrier2[changeCount];
            int imageIndex = 0;

            foreach (BatchTransition t in targets)
            {
                VulkanResourceState source = GetTextureStateUnlocked(t.Texture);
                if (source == t.Target)
                {
                    continue;
                }

                (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(source);
                (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = ImageScope(t.Target);
                imageBarriers[imageIndex++] = BuildImageBarrier(t.Texture, source, t.Target, srcStage, srcAccess, dstStage, dstAccess);

                _imageStates[t.Texture] = t.Target;
            }

            VkDependencyInfo dependency = new()
            {
                imageMemoryBarrierCount = (uint)imageIndex,
                pImageMemoryBarriers = imageBarriers,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);
        }
    }

    /// <summary>
    /// Records a new state for a resource used inside a pass WITHOUT emitting a
    /// barrier (illegal inside a rendering scope). Correct because wgpu semantics
    /// give a resource a single usage per render pass, and the previous pass
    /// flushed its writes when it ended. Inside a compute pass the binding is
    /// also collected into the current dispatch scope: every dispatch is its
    /// own usage scope, so consecutive dispatches re-using a resource need a
    /// barrier between them even with an unchanged tracked state (see
    /// <see cref="FlushDispatchBarriers"/>).
    /// </summary>
    public void MarkTexture(VulkanTexture texture, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            _ = GetTextureStateUnlocked(texture); // seed first use if needed
            _imageStates[texture] = target;
            if (_inComputePass)
            {
                _dispatchBinds[texture] = target;
            }
        }
    }

    public void MarkBuffer(VulkanBuffer buffer, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            _ = GetBufferStateUnlocked(buffer); // seed first use if needed
            _bufferStates[buffer] = target;
            if (_inComputePass)
            {
                _dispatchBinds[buffer] = target;
            }
        }
    }

    /// <summary>Opens a compute-pass scope: bindings accumulate into the current
    /// dispatch scope and flush before each dispatch (Vulkan allows barriers
    /// between dispatches; rendering scopes are the only ones that forbid them).</summary>
    public void BeginComputeScope()
    {
        using var __ = Lock();
        {
            _inComputePass = true;
            _dispatchBinds.Clear();
            _dispatchScopes.Clear();
        }
    }

    /// <summary>Closes the compute-pass scope. Leftover scopes are dropped: the
    /// pass-end flush barrier already covers every touched resource's current
    /// state, making them redundant.</summary>
    public void EndComputeScope()
    {
        using var __ = Lock();
        {
            _inComputePass = false;
            _dispatchBinds.Clear();
            _dispatchScopes.Clear();
        }
    }

    /// <summary>
    /// Emits ONE barrier making the previous dispatch's accesses on the bound
    /// resources visible to the next one - the wgpu per-dispatch usage-scope
    /// model. A resource needs the barrier when its usage changed OR when
    /// either side of the two dispatches is a write (read-after-write,
    /// write-after-read and write-after-write all need it; a Vulkan read-write
    /// storage binding stays in the same state across dispatches yet still
    /// needs the sync). Pure read-after-read with unchanged usage is skipped.
    /// Resources seen for the first time in the pass have no prior dispatch to
    /// synchronize with and are skipped as well. Legal only outside a
    /// rendering scope.
    /// </summary>
    public void FlushDispatchBarriers(VkCommandBuffer commandBuffer)
    {
        using var __ = Lock();
        {
            if (!_inComputePass || _dispatchBinds.Count == 0)
            {
                return;
            }

            VkPipelineStageFlags2 srcStage = VkPipelineStageFlags2.TopOfPipe;
            VkAccessFlags2 srcAccess = VkAccessFlags2.None;
            VkPipelineStageFlags2 dstStage = VkPipelineStageFlags2.TopOfPipe;
            VkAccessFlags2 dstAccess = VkAccessFlags2.None;

            foreach (KeyValuePair<object, VulkanResourceState> bind in _dispatchBinds)
            {
                if (!_dispatchScopes.TryGetValue(bind.Key, out VulkanResourceState oldState))
                {
                    // no previous dispatch touched it inside this pass; its
                    // cross-pass/cross-submission hazards are handled by the
                    // regular entry barriers
                    continue;
                }

                VulkanResourceState newState = bind.Value;
                if (oldState == newState && !IsWriteState(oldState) && !IsWriteState(newState))
                {
                    // read-after-read with identical usage: no hazard
                    continue;
                }

                if (bind.Key is VulkanTexture)
                {
                    // storage/sampled states all live in GENERAL, so the memory
                    // barrier covers the image; layouts never change here
                    (VkPipelineStageFlags2 s, VkAccessFlags2 a) = ImageScope(oldState);
                    (VkPipelineStageFlags2 ds, VkAccessFlags2 da) = ImageScope(newState);
                    srcStage |= s;
                    srcAccess |= a;
                    dstStage |= ds;
                    dstAccess |= da;
                }
                else
                {
                    (VkPipelineStageFlags2 s, VkAccessFlags2 a) = BufferScope(oldState);
                    (VkPipelineStageFlags2 ds, VkAccessFlags2 da) = BufferScope(newState);
                    srcStage |= s;
                    srcAccess |= a;
                    dstStage |= ds;
                    dstAccess |= da;
                }
            }

            // the next dispatch runs with the scope this barrier established;
            // bindings stay live so dispatches that skip rebinding (unchanged
            // bind groups) keep being synchronized against this scope
            _dispatchScopes.Clear();
            foreach (KeyValuePair<object, VulkanResourceState> bind in _dispatchBinds)
            {
                _dispatchScopes[bind.Key] = bind.Value;
            }

            if (srcStage == VkPipelineStageFlags2.TopOfPipe)
            {
                return;
            }

            VkMemoryBarrier2 memoryBarrier = new()
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = dstStage,
                dstAccessMask = dstAccess,
            };

            VkDependencyInfo dependency = new()
            {
                memoryBarrierCount = 1,
                pMemoryBarriers = &memoryBarrier,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);
        }
    }

    /// <summary>Whether a state writes the resource (a dispatch pair involving
    /// any write needs a barrier between them).</summary>
    private static bool IsWriteState(VulkanResourceState state)
    {
        return state switch
        {
            VulkanResourceState.ShaderWrite
            or VulkanResourceState.ShaderReadWrite
            or VulkanResourceState.CopyDst
            or VulkanResourceState.ColorAttachment
            or VulkanResourceState.DepthWrite => true,
            _ => false,
        };
    }

    /// <summary>Combined (stage, access) source scope of a bound resource state —
    /// pure mapping, no tracker state involved.</summary>
    internal static (VkPipelineStageFlags2 Stage, VkAccessFlags2 Access) ScopeOf(
        object resource, VulkanResourceState state)
        => resource is VulkanTexture ? ImageScope(state) : BufferScope(state);

    // ===== bundle scopes / pass scopes =====

    /// <summary>Accumulates the source scope of one render-bundle execute into the
    /// current pass's flush union (one call per execute, no per-resource work).</summary>
    public void UnionBundleScope(VkPipelineStageFlags2 stage, VkAccessFlags2 access)
    {
        using var __ = Lock();
        {
            _bundleSrcStage |= stage;
            _bundleSrcAccess |= access;
        }
    }

    /// <summary>Registers a resource touched by the currently open pass so its writes
    /// are flushed when the pass ends.</summary>
    public void TouchInPass(object resource)
    {
        using var __ = Lock();
        {
            _passTouched.Add(resource);
        }
    }

    /// <summary>
    /// Ends a usage scope: emits one wide barrier covering everything the pass
    /// touched so all of its writes (shader writes, attachment writes) are visible
    /// to any later command, and restores every attachment that left the GENERAL
    /// idle layout back into it (in-pass binds assume GENERAL). Call right after
    /// the native pass ended, outside the pass.
    /// </summary>
    public void FlushPass(VkCommandBuffer commandBuffer)
    {
        using var __ = Lock();
        {
            // render-bundle executes since the last flush contribute their read
            // scopes here; one union barrier covers them with the direct binds
            VkPipelineStageFlags2 bundleStage = _bundleSrcStage;
            VkAccessFlags2 bundleAccess = _bundleSrcAccess;
            _bundleSrcStage = VkPipelineStageFlags2.TopOfPipe;
            _bundleSrcAccess = VkAccessFlags2.None;

            if (_passTouched.Count == 0 && bundleStage == VkPipelineStageFlags2.TopOfPipe)
            {
                return;
            }

            int restoreCount = 0;
            foreach (object resource in _passTouched)
            {
                if (resource is VulkanTexture texture
                    && LayoutForState(texture, GetTextureStateUnlocked(texture)) != VkImageLayout.General)
                {
                    restoreCount++;
                }
            }

            // zero-length stackalloc is legal; the pointer is unused when count is 0
            VkImageMemoryBarrier2* imageBarriers = stackalloc VkImageMemoryBarrier2[restoreCount];
            int imageIndex = 0;

            // fold every touched resource's write scope into ONE barrier
            VkPipelineStageFlags2 srcStage = bundleStage;
            VkAccessFlags2 srcAccess = bundleAccess;
            foreach (object resource in _passTouched)
            {
                if (resource is VulkanTexture texture)
                {
                    VulkanResourceState state = GetTextureStateUnlocked(texture);
                    (VkPipelineStageFlags2 s, VkAccessFlags2 a) = ImageScope(state);
                    srcStage |= s;
                    srcAccess |= a;

                    if (LayoutForState(texture, state) != VkImageLayout.General)
                    {
                        imageBarriers[imageIndex++] = new VkImageMemoryBarrier2
                        {
                            srcStageMask = s,
                            srcAccessMask = a,
                            dstStageMask = VkPipelineStageFlags2.AllCommands,
                            dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
                            oldLayout = LayoutForState(texture, state),
                            newLayout = VkImageLayout.General,
                            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                            image = texture.Image,
                            subresourceRange = new VkImageSubresourceRange
                            {
                                aspectMask = VulkanUtility.AspectToVulkan(TextureAspect.All, texture.VkFormat),
                                baseMipLevel = 0,
                                levelCount = texture.MipLevelCount,
                                baseArrayLayer = 0,
                                layerCount = texture.ArrayLayers,
                            },
                        };
                        _imageStates[texture] = VulkanResourceState.Idle;
                    }
                }
                else if (resource is VulkanBuffer buffer)
                {
                    (VkPipelineStageFlags2 s, VkAccessFlags2 a) = BufferScope(GetBufferStateUnlocked(buffer));
                    srcStage |= s;
                    srcAccess |= a;
                }
            }

            VkMemoryBarrier2 memoryBarrier = new()
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = VkPipelineStageFlags2.AllCommands,
                dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            };

            VkDependencyInfo dependency = new()
            {
                memoryBarrierCount = 1,
                pMemoryBarriers = &memoryBarrier,
                imageMemoryBarrierCount = (uint)imageIndex,
                pImageMemoryBarriers = imageBarriers,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);
            _passTouched.Clear();
        }
    }

    /// <summary>
    /// After an out-of-pass image copy, restores the image to the GENERAL idle
    /// layout and makes the transfer scope visible to any later command (in-pass
    /// binds and descriptor layouts assume GENERAL).
    /// </summary>
    public void RestoreImageToIdle(VkCommandBuffer commandBuffer, VulkanTexture texture)
    {
        using var __ = Lock();
        {
            VulkanResourceState state = GetTextureStateUnlocked(texture);
            (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(state);
            VkImageLayout oldLayout = LayoutForState(texture, state);

            VkImageMemoryBarrier2 barrier = new()
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = VkPipelineStageFlags2.AllCommands,
                dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
                oldLayout = oldLayout,
                newLayout = VkImageLayout.General,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = texture.Image,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VulkanUtility.AspectToVulkan(TextureAspect.All, texture.VkFormat),
                    baseMipLevel = 0,
                    levelCount = texture.MipLevelCount,
                    baseArrayLayer = 0,
                    layerCount = texture.ArrayLayers,
                },
            };

            VkDependencyInfo dependency = new()
            {
                imageMemoryBarrierCount = 1,
                pImageMemoryBarriers = &barrier,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);

            _imageStates[texture] = VulkanResourceState.Idle;
        }
    }

    /// <summary>Barriers everything the tracker touched without a pass context
    /// (used after out-of-pass writes such as queue uploads).</summary>
    public void ClearPassScope()
    {
        using var __ = Lock();
        {
            _passTouched.Clear();
        }
    }

    /// <summary>
    /// Emits a wide barrier making writes in <paramref name="writerState"/> scope
    /// visible to any later command in any submission. Used after out-of-pass
    /// BUFFER writes (copies, query resolves) so readers never need cross-submit
    /// assumptions.
    /// </summary>
    public void MakeWritesVisible(VkCommandBuffer commandBuffer, VulkanResourceState writerState)
    {
        (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = BufferScope(writerState);
        using var __ = Lock();
        {
            RecordGlobalBarrier(commandBuffer, srcStage, srcAccess);
        }
    }

    private static void RecordGlobalBarrier(VkCommandBuffer commandBuffer, VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess)
    {
        VkMemoryBarrier2 barrier = new()
        {
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = VkPipelineStageFlags2.AllCommands,
            dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
        };

        VkDependencyInfo dependency = new()
        {
            memoryBarrierCount = 1,
            pMemoryBarriers = &barrier,
        };
        vkCmdPipelineBarrier2(commandBuffer, &dependency);
    }

    // ===== submit-time reconciliation (recording tracker -> device tracker) =====

    /// <summary>Clears every per-recording structure. Call at command buffer
    /// Begin() so a re-recorded buffer re-seeds from the device tracker.</summary>
    public void Reset()
    {
        using var __ = Lock();
        {
            _imageStates.Clear();
            _bufferStates.Clear();
            _firstUses.Clear();
            _passTouched.Clear();
            _dispatchBinds.Clear();
            _dispatchScopes.Clear();
            _inComputePass = false;
            _bundleSrcStage = VkPipelineStageFlags2.TopOfPipe;
            _bundleSrcAccess = VkAccessFlags2.None;
            _serial = _parent?.CurrentSerial ?? 0;
        }
    }

    /// <summary>The device tracker's current submission serial (cheap snapshot).</summary>
    private long CurrentSerial
    {
        get
        {
            using var __ = Lock();
            {
                return _serial;
            }
        }
    }

    /// <summary>Whether the recording's seeds may have drifted from the device
    /// tracker (another submission landed between Reset() and now). Cheap serial
    /// comparison; the common single-recorder case skips the whole scan.</summary>
    public bool NeedsPrologue(VulkanResourceTracker device)
    {
        using var __ = Lock();
        {
            return _firstUses.Count > 0 && _serial != device.CurrentSerial;
        }
    }

    /// <summary>Records the prologue barriers moving every drifted resource from
    /// its device (true execution) state into the state this buffer's recorded
    /// barriers assume. Call under the device queue lock, immediately before
    /// submitting the recorded command buffer.</summary>
    public bool RecordPrologue(VkCommandBuffer commandBuffer, VulkanResourceTracker device)
    {
        // reused barrier list: the prologue runs on every submission whose
        // recording saw another submit in between, so it must not allocate
        List<VkImageMemoryBarrier2> imageBarriers = _prologueBarriers;
        imageBarriers.Clear();
        VkPipelineStageFlags2 srcStage = VkPipelineStageFlags2.TopOfPipe;
        VkAccessFlags2 srcAccess = VkAccessFlags2.None;
        VkPipelineStageFlags2 dstStage = VkPipelineStageFlags2.TopOfPipe;
        VkAccessFlags2 dstAccess = VkAccessFlags2.None;

        using var __ = Lock();
        {
            using var __device = device.Lock();
            {
                foreach (FirstUse use in _firstUses)
                {
                    VulkanResourceState deviceState = device.GetStateUnlocked(use.Resource);
                    if (deviceState == use.Seed)
                    {
                        continue;
                    }

                    if (use.Resource is VulkanTexture texture)
                    {
                        (VkPipelineStageFlags2 s, VkAccessFlags2 a) = ImageScope(deviceState);
                        (VkPipelineStageFlags2 ds, VkAccessFlags2 da) = ImageScope(use.Seed);
                        srcStage |= s;
                        srcAccess |= a;
                        dstStage |= ds;
                        dstAccess |= da;
                        imageBarriers.Add(BuildImageBarrier(texture, deviceState, use.Seed, s, a, ds, da));
                    }
                    else if (use.Resource is VulkanBuffer buffer)
                    {
                        (VkPipelineStageFlags2 s, VkAccessFlags2 a) = BufferScope(deviceState);
                        (VkPipelineStageFlags2 ds, VkAccessFlags2 da) = BufferScope(use.Seed);
                        srcStage |= s;
                        srcAccess |= a;
                        dstStage |= ds;
                        dstAccess |= da;
                        _ = buffer;
                    }
                }
            }
        }

        if (imageBarriers.Count == 0 && srcStage == VkPipelineStageFlags2.TopOfPipe)
        {
            // nothing actually drifted past this recording's assumptions
            return false;
        }

        VkMemoryBarrier2 memoryBarrier = new()
        {
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = dstStage,
            dstAccessMask = dstAccess,
        };

        Span<VkImageMemoryBarrier2> barriers = CollectionsMarshal.AsSpan(imageBarriers);
        fixed (VkImageMemoryBarrier2* imagePtr = barriers)
        {
            VkDependencyInfo dependency = new()
            {
                memoryBarrierCount = 1,
                pMemoryBarriers = &memoryBarrier,
                imageMemoryBarrierCount = (uint)barriers.Length,
                pImageMemoryBarriers = imagePtr,
            };
            vkCmdPipelineBarrier2(commandBuffer, &dependency);
        }
        return true;
    }

    /// <summary>Copies this recording's final states into the device tracker and
    /// bumps its serial. Call under the device queue lock right after submitting.</summary>
    public void AbsorbInto(VulkanResourceTracker device)
    {
        using var __ = Lock();
        {
            using var __device = device.Lock();
            {
                foreach (KeyValuePair<VulkanTexture, VulkanResourceState> pair in _imageStates)
                {
                    device._imageStates[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<VulkanBuffer, VulkanResourceState> pair in _bufferStates)
                {
                    device._bufferStates[pair.Key] = pair.Value;
                }
                device._serial++;
            }
        }
    }

    // ===== internal helpers =====

    private VulkanResourceState GetStateUnlocked(object resource)
    {
        VulkanResourceState state = VulkanResourceState.Undefined;
        if (resource is VulkanTexture texture)
        {
            _imageStates.TryGetValue(texture, out state);
        }
        else if (resource is VulkanBuffer buffer)
        {
            _bufferStates.TryGetValue(buffer, out state);
        }
        return state;
    }

    private VulkanResourceState GetTextureStateUnlocked(VulkanTexture texture)
    {
        if (_imageStates.TryGetValue(texture, out VulkanResourceState state))
        {
            return state;
        }
        VulkanResourceState seed = _parent != null ? _parent.GetTextureState(texture) : VulkanResourceState.Undefined;
        _imageStates[texture] = seed;
        RecordFirstUse(texture, seed);
        return seed;
    }

    private VulkanResourceState GetBufferStateUnlocked(VulkanBuffer buffer)
    {
        if (_bufferStates.TryGetValue(buffer, out VulkanResourceState state))
        {
            return state;
        }
        VulkanResourceState seed = _parent != null ? _parent.GetBufferState(buffer) : VulkanResourceState.Undefined;
        _bufferStates[buffer] = seed;
        RecordFirstUse(buffer, seed);
        return seed;
    }

    // ===== layout / scope mapping =====

    /// <summary>
    /// The actual VkImageLayout a state implies. Mirrors wgpu: attachments and
    /// transfer/present operations use their optimal layouts (this is what makes
    /// depth compression and present work efficiently); sampling and storage
    /// usages stay in GENERAL because in-pass binds cannot record layout
    /// transitions, so every bindable texture must be reachable in GENERAL at
    /// bind time (FlushPass restores attachments to GENERAL when a pass ends).
    /// Swapchain images (PreferGeneralLayout) stay in GENERAL for everything
    /// except present: their attachment barriers are recorded in a command
    /// buffer that interleaves with the main frame buffer, so optimal-layout
    /// transitions could land in the wrong submission.
    /// </summary>
    private static VkImageLayout LayoutForState(VulkanTexture texture, VulkanResourceState state)
    {
        if (texture.PreferGeneralLayout)
        {
            return state == VulkanResourceState.Present
                ? VkImageLayout.PresentSrcKHR
                : state == VulkanResourceState.Undefined
                    ? VkImageLayout.Undefined
                    : VkImageLayout.General;
        }
        return state switch
        {
            VulkanResourceState.Undefined => VkImageLayout.Undefined,
            VulkanResourceState.ColorAttachment => VkImageLayout.ColorAttachmentOptimal,
            VulkanResourceState.DepthWrite => VkImageLayout.DepthStencilAttachmentOptimal,
            VulkanResourceState.DepthRead => VkImageLayout.DepthStencilReadOnlyOptimal,
            VulkanResourceState.CopySrc => VkImageLayout.TransferSrcOptimal,
            VulkanResourceState.CopyDst => VkImageLayout.TransferDstOptimal,
            VulkanResourceState.Present => VkImageLayout.PresentSrcKHR,
            _ => VkImageLayout.General,
        };
    }

    /// <summary>The layout <paramref name="texture"/> is in while in
    /// <paramref name="state"/>; for copy commands that must name the layout.</summary>
    public VkImageLayout LayoutForTexture(VulkanTexture texture, VulkanResourceState state)
    {
        using var __ = Lock();
        {
            return LayoutForState(texture, state);
        }
    }

    private static VkImageMemoryBarrier2 BuildImageBarrier(
        VulkanTexture texture,
        VulkanResourceState source,
        VulkanResourceState target,
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess)
    {
        return new VkImageMemoryBarrier2
        {
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = dstStage,
            dstAccessMask = dstAccess,
            oldLayout = LayoutForState(texture, source),
            newLayout = LayoutForState(texture, target),
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = texture.Image,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VulkanUtility.AspectToVulkan(TextureAspect.All, texture.VkFormat),
                baseMipLevel = 0,
                levelCount = texture.MipLevelCount,
                baseArrayLayer = 0,
                layerCount = texture.ArrayLayers,
            },
        };
    }

    private static (VkPipelineStageFlags2, VkAccessFlags2) ImageScope(VulkanResourceState state)
    {
        return state switch
        {
            VulkanResourceState.Undefined => (VkPipelineStageFlags2.TopOfPipe, VkAccessFlags2.None),
            VulkanResourceState.Idle => (VkPipelineStageFlags2.TopOfPipe, VkAccessFlags2.None),
            VulkanResourceState.ColorAttachment => (VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ColorAttachmentRead),
            VulkanResourceState.DepthWrite => (VkPipelineStageFlags2.LateFragmentTests, VkAccessFlags2.DepthStencilAttachmentWrite | VkAccessFlags2.DepthStencilAttachmentRead),
            VulkanResourceState.DepthRead => (VkPipelineStageFlags2.LateFragmentTests, VkAccessFlags2.DepthStencilAttachmentRead),
            VulkanResourceState.ShaderRead => (VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.ShaderSampledRead | VkAccessFlags2.ShaderRead),
            VulkanResourceState.ShaderWrite => (VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.ShaderWrite),
            VulkanResourceState.ShaderReadWrite => (VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.ShaderRead | VkAccessFlags2.ShaderWrite),
            VulkanResourceState.CopySrc => (VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferRead),
            VulkanResourceState.CopyDst => (VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite),
            VulkanResourceState.Present => (VkPipelineStageFlags2.BottomOfPipe, VkAccessFlags2.None),
            _ => (VkPipelineStageFlags2.AllCommands, VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite),
        };
    }

    private static (VkPipelineStageFlags2, VkAccessFlags2) BufferScope(VulkanResourceState state)
    {
        return state switch
        {
            VulkanResourceState.Undefined => (VkPipelineStageFlags2.TopOfPipe, VkAccessFlags2.None),
            VulkanResourceState.VertexRead => (VkPipelineStageFlags2.VertexInput, VkAccessFlags2.VertexAttributeRead),
            VulkanResourceState.IndexRead => (VkPipelineStageFlags2.VertexInput, VkAccessFlags2.IndexRead),
            VulkanResourceState.UniformRead => (VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.UniformRead),
            VulkanResourceState.ShaderReadWrite => (VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.ShaderRead | VkAccessFlags2.ShaderWrite),
            VulkanResourceState.IndirectRead => (VkPipelineStageFlags2.DrawIndirect | VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.IndirectCommandRead),
            VulkanResourceState.CopySrc => (VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferRead),
            VulkanResourceState.CopyDst => (VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite),
            _ => (VkPipelineStageFlags2.AllCommands, VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite),
        };
    }
}
