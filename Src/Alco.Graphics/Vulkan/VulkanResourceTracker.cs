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
/// image into their optimal layouts and the image STAYS there until the next
/// usage says otherwise — there is no pass-end restore, so depth/color
/// compression survives across passes. Sampling and storage usages keep GENERAL:
/// binds recorded inside a rendering scope cannot transition attachment layouts,
/// so every bindable texture must reach GENERAL at bind time, which the queued
/// bind-time transition guarantees. Hazard handling is fully edge-based (wgpu's
/// usage-scope expansion):
/// - every usage point derives the precise barrier from the resource's current
///   state scope to its next state scope (read-after-read records nothing);
/// - a resource carrying an unsynchronized write (see <see cref="_pendingWrites"/>)
///   forces a barrier even when the state repeats (write-after-write), and
///   first-use seeding imports that flag from the device tracker's state so
///   cross-submission writes are covered the same way;
/// - bind-time transitions batch into ONE barrier flushed before the next
///   draw/dispatch; pass-entry attachments and copies transition eagerly.
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

    // resources carrying a write no recorded barrier covers yet: a same-state
    // reuse while listed here still needs a barrier (write-after-write edge,
    // wgpu's usage-scope expansion). Recording trackers seed this from the
    // device tracker's state at first use so cross-submission writes are
    // covered the same way as in-buffer ones.
    private readonly HashSet<object> _pendingWrites = new();

    // write-state resources already synchronized into the currently open
    // usage scope (pass): WebGPU gives one usage scope per resource per render
    // pass, so rebinds of the same storage resource by later draws of the
    // pass must NOT emit another edge — the scope's first bind covered it.
    // Cleared when the pass closes.
    private readonly HashSet<object> _passBinds = new();

    // bind-time transitions queued since the last flush, emitted as ONE barrier
    // command before the next consuming command (see FlushPendingBarriers).
    // Only recording trackers ever queue; the device tracker's transitions are
    // emitted eagerly. _hasPendingMemory guards the stage unions because
    // TopOfPipe is a real stage bit and cannot double as "empty".
    private readonly List<VkImageMemoryBarrier2> _pendingImageBarriers = new();
    private VkPipelineStageFlags2 _pendingSrcStage = VkPipelineStageFlags2.TopOfPipe;
    private VkAccessFlags2 _pendingSrcAccess = VkAccessFlags2.None;
    private VkPipelineStageFlags2 _pendingDstStage = VkPipelineStageFlags2.TopOfPipe;
    private VkAccessFlags2 _pendingDstAccess = VkAccessFlags2.None;
    private bool _hasPendingMemory;

    // device tracker: bumped on every queue submission / invalidation so recording
    // contexts can skip the submit-time scan when nothing was submitted in between;
    // recording trackers: the device serial observed at Reset() time
    private long _serial;

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

    /// <summary>Whether this recording ever touched <paramref name="texture"/>.
    /// Unlike <see cref="GetTextureState"/> this never seeds state or records a
    /// first use, so it is safe as a submit-time gate.</summary>
    public bool RecordedTexture(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            return _imageStates.ContainsKey(texture);
        }
    }

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
            // a seeded WRITE state carries an unsynchronized cross-submission
            // hazard: the first same-state reuse in this buffer must still
            // emit the WAW edge against the previous submission's commands
            if (IsWriteState(seed))
            {
                _pendingWrites.Add(resource);
            }
        }
    }

    /// <summary>Forgets the tracked state (e.g. a swapchain image that just came back
    /// from present with undefined contents). Device tracker only.</summary>
    public void InvalidateTexture(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            _imageStates[texture] = VulkanResourceState.Undefined;
            _pendingWrites.Remove(texture);
            MarkStateChanged(texture);
            _serial++;
        }
    }

    public void Remove(VulkanTexture texture)
    {
        using var __ = Lock();
        {
            _imageStates.Remove(texture);
            _pendingWrites.Remove(texture);
            MarkStateChanged(texture);
            _serial++;
        }
    }

    public void Remove(VulkanBuffer buffer)
    {
        using var __ = Lock();
        {
            _bufferStates.Remove(buffer);
            _pendingWrites.Remove(buffer);
            MarkStateChanged(buffer);
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
    /// a render/compute pass (attachment entry, copies, present). Repeating the
    /// current state is a no-op unless the resource carries an unsynchronized
    /// write — a same-state write-after-write edge still needs the barrier.
    /// </summary>
    public void TransitionTexture(VkCommandBuffer commandBuffer, VulkanTexture texture, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetTextureStateUnlocked(texture);
            if (source == target && !(IsWriteState(target) && _pendingWrites.Contains(texture)))
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

            _pendingWrites.Remove(texture);
            _imageStates[texture] = target;
            if (source != target)
            {
                MarkStateChanged(texture);
            }
            if (IsWriteState(target))
            {
                _pendingWrites.Add(texture);
            }
        }
    }

    /// <summary>
    /// Records a barrier transitioning <paramref name="buffer"/> into
    /// <paramref name="target"/> and updates the tracked state. Repeating the
    /// current state is a no-op unless the buffer carries an unsynchronized write.
    /// </summary>
    public void TransitionBuffer(VkCommandBuffer commandBuffer, VulkanBuffer buffer, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetBufferStateUnlocked(buffer);
            if (source == target && !(IsWriteState(target) && _pendingWrites.Contains(buffer)))
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

            _pendingWrites.Remove(buffer);
            _bufferStates[buffer] = target;
            if (source != target)
            {
                MarkStateChanged(buffer);
            }
            if (IsWriteState(target))
            {
                _pendingWrites.Add(buffer);
            }
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
    /// command. Every texture that changes state gets a real per-image barrier;
    /// a texture that stays in a write state while carrying an unsynchronized
    /// write (the same attachment rendered by two passes in a row) gets a
    /// layout-preserving write-after-write edge. All fold into a single
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
                VulkanResourceState source = GetTextureStateUnlocked(t.Texture);
                if (source != t.Target || (IsWriteState(t.Target) && _pendingWrites.Contains(t.Texture)))
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
                if (source == t.Target && !(IsWriteState(t.Target) && _pendingWrites.Contains(t.Texture)))
                {
                    continue;
                }

                (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(source);
                (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = ImageScope(t.Target);
                imageBarriers[imageIndex++] = BuildImageBarrier(t.Texture, source, t.Target, srcStage, srcAccess, dstStage, dstAccess);

                _pendingWrites.Remove(t.Texture);
                _imageStates[t.Texture] = t.Target;
                if (source != t.Target)
                {
                    MarkStateChanged(t.Texture);
                }
                if (IsWriteState(t.Target))
                {
                    _pendingWrites.Add(t.Texture);
                }
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
    /// Records a new state for a resource used inside a pass. The hazard edge from
    /// the previous usage — a layout change, or an unsynchronized write the
    /// resource still carries — is queued and flushed as one barrier right before
    /// the next draw (<see cref="FlushPendingBarriers"/>). Bindable states never
    /// leave the GENERAL layout, so bind-time barriers inside a rendering scope
    /// are legal memory barriers that touch no attachment. Read-after-read with
    /// unchanged usage records nothing (wgpu usage scopes). Inside a compute pass
    /// the binding instead collects into the current dispatch scope: every
    /// dispatch is its own usage scope, so consecutive dispatches re-using a
    /// resource are synchronized by <see cref="FlushDispatchBarriers"/> instead.
    /// </summary>
    public void MarkTexture(VulkanTexture texture, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetTextureStateUnlocked(texture); // seed first use if needed
            if (_inComputePass)
            {
                if (source != target)
                {
                    _imageStates[texture] = target;
                    MarkStateChanged(texture);
                }
                _dispatchBinds[texture] = target;
                if (IsWriteState(target))
                {
                    _pendingWrites.Add(texture);
                }
                return;
            }

            if (source != target)
            {
                (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(source);
                (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = ImageScope(target);
                if (LayoutForState(texture, source) != LayoutForState(texture, target))
                {
                    _pendingImageBarriers.Add(BuildImageBarrier(texture, source, target, srcStage, srcAccess, dstStage, dstAccess));
                }
                else
                {
                    QueueMemoryEdge(srcStage, srcAccess, dstStage, dstAccess);
                }
            }
            else if (IsWriteState(target) && _pendingWrites.Contains(texture) && !_passBinds.Contains(texture))
            {
                // same-state write-after-write, FIRST bind of this resource in
                // the current usage scope: layouts stay GENERAL, so a pure
                // execution/memory edge suffices. Rebinds by later draws of the
                // same pass skip (WebGPU: one usage scope per resource per
                // render pass); the scope re-opens at the next pass.
                (VkPipelineStageFlags2 stage, VkAccessFlags2 access) = ImageScope(target);
                QueueMemoryEdge(stage, access, stage, access);
            }

            _pendingWrites.Remove(texture);
            if (source != target)
            {
                _imageStates[texture] = target;
                MarkStateChanged(texture);
            }
            if (IsWriteState(target))
            {
                _pendingWrites.Add(texture);
                _passBinds.Add(texture);
            }
        }
    }

    public void MarkBuffer(VulkanBuffer buffer, VulkanResourceState target)
    {
        using var __ = Lock();
        {
            VulkanResourceState source = GetBufferStateUnlocked(buffer); // seed first use if needed
            if (_inComputePass)
            {
                if (source != target)
                {
                    _bufferStates[buffer] = target;
                    MarkStateChanged(buffer);
                }
                _dispatchBinds[buffer] = target;
                if (IsWriteState(target))
                {
                    _pendingWrites.Add(buffer);
                }
                return;
            }

            if (source != target)
            {
                (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = BufferScope(source);
                (VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) = BufferScope(target);
                QueueMemoryEdge(srcStage, srcAccess, dstStage, dstAccess);
            }
            else if (IsWriteState(target) && _pendingWrites.Contains(buffer) && !_passBinds.Contains(buffer))
            {
                // same-state write-after-write edge, once per usage scope
                (VkPipelineStageFlags2 stage, VkAccessFlags2 access) = BufferScope(target);
                QueueMemoryEdge(stage, access, stage, access);
            }

            _pendingWrites.Remove(buffer);
            if (source != target)
            {
                _bufferStates[buffer] = target;
                MarkStateChanged(buffer);
            }
            if (IsWriteState(target))
            {
                _pendingWrites.Add(buffer);
                _passBinds.Add(buffer);
            }
        }
    }

    /// <summary>Closes the current usage scope (pass end): rebinds in the NEXT
    /// pass must re-evaluate their write-after-write edges. Compute passes use
    /// the dispatch-scope machinery instead and need no call.</summary>
    public void EndPassScope()
    {
        using var __ = Lock();
        {
            _passBinds.Clear();
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

    /// <summary>Closes the compute-pass scope. Leftover dispatch scopes die with
    /// the pass: the next usage point emits its own precise edge from whatever
    /// state the resource ended in.</summary>
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

    /// <summary>Whether a state writes the resource (a usage pair involving any
    /// write needs a barrier between the two usages).</summary>
    internal static bool IsWriteState(VulkanResourceState state)
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

    /// <summary>The resource's content version (see <see cref="VulkanTexture.TrackVersion"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long VersionOf(object resource)
    {
        return resource is VulkanTexture texture
            ? Volatile.Read(ref texture.TrackVersion)
            : Volatile.Read(ref ((VulkanBuffer)resource).TrackVersion);
    }

    /// <summary>Bumps the resource's content version; call on every tracked
    /// state VALUE change (any tracker). Interlocked: recording trackers on
    /// different threads may change the same resource's state concurrently.
    /// Also appends the resource to the dirty log so render bundles can re-mark
    /// only changed entries instead of scanning their whole read set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarkStateChanged(object resource)
    {
        if (resource is VulkanTexture texture)
        {
            _ = Interlocked.Increment(ref texture.TrackVersion);
        }
        else
        {
            _ = Interlocked.Increment(ref ((VulkanBuffer)resource).TrackVersion);
        }

        lock (s_dirtyLock)
        {
            long frame = VulkanDevice.FrameCounter;
            int slot = (int)(frame % DirtyRingSize);
            List<object> list = s_dirtyRing[slot];
            if (list == null || s_dirtyRingFrames[slot] != frame)
            {
                list ??= new List<object>(64);
                list.Clear();
                s_dirtyRing[slot] = list;
                s_dirtyRingFrames[slot] = frame;
            }
            list.Add(resource);
        }
    }

    // ===== cross-tracker dirty log (bundle re-mark fast path) =====
    // Every state-value change lands in the current frame's ring slot. A render
    // bundle keeps a (frame, index) cursor into this log and, on execute,
    // intersects the new entries with its read set — stable entries (the vast
    // majority: thousands of material textures that never leave ShaderRead)
    // cost zero iterations. Lock order: tracker lock -> s_dirtyLock, never the
    // reverse; ScanDirtyLog callers collect hits under s_dirtyLock and mark
    // after releasing it.
    private const int DirtyRingSize = 8;
    private static readonly List<object>[] s_dirtyRing = new List<object>[DirtyRingSize];
    private static readonly long[] s_dirtyRingFrames = new long[DirtyRingSize];
    private static readonly object s_dirtyLock = new();

    /// <summary>Collects the read-set indices whose resource appears in the dirty
    /// log after the cursor, and advances the cursor to the log tail. Returns
    /// false when the cursor fell out of the ring (or was never set): the caller
    /// must run a full mark pass and then re-stamp with <see cref="DirtyLogCursor"/>.</summary>
    internal static bool ScanDirtyLog(
        Dictionary<object, int> readIndex,
        List<int> hits,
        ref long cursorFrame,
        ref int cursorIndex)
    {
        lock (s_dirtyLock)
        {
            long frame = VulkanDevice.FrameCounter;
            if (cursorFrame < 0 || frame - cursorFrame >= DirtyRingSize)
            {
                return false;
            }

            for (long f = cursorFrame; f <= frame; f++)
            {
                int slot = (int)(f % DirtyRingSize);
                if (s_dirtyRingFrames[slot] != f)
                {
                    // slot overwritten or never written: history lost
                    return false;
                }
                List<object> list = s_dirtyRing[slot];
                if (list == null)
                {
                    continue; // stamped frame with nothing logged yet
                }
                int start = f == cursorFrame ? cursorIndex : 0;
                for (int i = start; i < list.Count; i++)
                {
                    if (readIndex.TryGetValue(list[i], out int readIdx))
                    {
                        hits.Add(readIdx);
                    }
                }
            }

            cursorFrame = frame;
            cursorIndex = s_dirtyRing[(int)(frame % DirtyRingSize)]?.Count ?? 0;
            return true;
        }
    }

    /// <summary>Stamps a dirty-log cursor at the current tail; call after a full
    /// mark pass (recording replay or a fallback scan) so later scans start here.</summary>
    internal static void DirtyLogCursor(out long cursorFrame, out int cursorIndex)
    {
        lock (s_dirtyLock)
        {
            long frame = VulkanDevice.FrameCounter;
            cursorFrame = frame;
            cursorIndex = s_dirtyRing[(int)(frame % DirtyRingSize)]?.Count ?? 0;
        }
    }

    // ===== queued bind-time barriers =====

    private void QueueMemoryEdge(
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess)
    {
        if (!_hasPendingMemory)
        {
            _pendingSrcStage = srcStage;
            _pendingSrcAccess = srcAccess;
            _pendingDstStage = dstStage;
            _pendingDstAccess = dstAccess;
            _hasPendingMemory = true;
            return;
        }
        _pendingSrcStage |= srcStage;
        _pendingSrcAccess |= srcAccess;
        _pendingDstStage |= dstStage;
        _pendingDstAccess |= dstAccess;
    }

    /// <summary>
    /// Emits every queued bind-time transition as ONE barrier command. Called
    /// right before the next consuming command (draw, dispatch, bundle execute)
    /// and as a backstop when a pass ends. <see cref="_pendingWrites"/> is NOT
    /// cleared: the barrier synchronizes the past, and the commands that follow
    /// still write. The fast path (nothing queued) is a pair of field reads —
    /// recording trackers carry no lock.
    /// </summary>
    public void FlushPendingBarriers(VkCommandBuffer commandBuffer)
    {
        if (!_hasPendingMemory && _pendingImageBarriers.Count == 0)
        {
            return;
        }

        using var __ = Lock();
        {
            VkMemoryBarrier2 memoryBarrier = new()
            {
                srcStageMask = _pendingSrcStage,
                srcAccessMask = _pendingSrcAccess,
                dstStageMask = _pendingDstStage,
                dstAccessMask = _pendingDstAccess,
            };

            Span<VkImageMemoryBarrier2> imageBarriers = CollectionsMarshal.AsSpan(_pendingImageBarriers);
            fixed (VkImageMemoryBarrier2* imagePtr = imageBarriers)
            {
                VkDependencyInfo dependency = new()
                {
                    imageMemoryBarrierCount = (uint)imageBarriers.Length,
                    pImageMemoryBarriers = imagePtr,
                };
                if (_hasPendingMemory)
                {
                    dependency.memoryBarrierCount = 1;
                    dependency.pMemoryBarriers = &memoryBarrier;
                }
                vkCmdPipelineBarrier2(commandBuffer, &dependency);
            }

            _pendingImageBarriers.Clear();
            _hasPendingMemory = false;
            _pendingSrcStage = VkPipelineStageFlags2.TopOfPipe;
            _pendingSrcAccess = VkAccessFlags2.None;
            _pendingDstStage = VkPipelineStageFlags2.TopOfPipe;
            _pendingDstAccess = VkAccessFlags2.None;
        }
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
            _pendingWrites.Clear();
            _passBinds.Clear();
            _pendingImageBarriers.Clear();
            _hasPendingMemory = false;
            _pendingSrcStage = VkPipelineStageFlags2.TopOfPipe;
            _pendingSrcAccess = VkAccessFlags2.None;
            _pendingDstStage = VkPipelineStageFlags2.TopOfPipe;
            _pendingDstAccess = VkAccessFlags2.None;
            _dispatchBinds.Clear();
            _dispatchScopes.Clear();
            _inComputePass = false;
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
    /// bumps its serial. Call under the device queue lock right after submitting.
    /// Resource content versions bump only where the device value actually moves
    /// (the recording's own marks already bumped them).</summary>
    public void AbsorbInto(VulkanResourceTracker device)
    {
        using var __ = Lock();
        {
            using var __device = device.Lock();
            {
                foreach (KeyValuePair<VulkanTexture, VulkanResourceState> pair in _imageStates)
                {
                    if (!device._imageStates.TryGetValue(pair.Key, out VulkanResourceState old) || old != pair.Value)
                    {
                        device._imageStates[pair.Key] = pair.Value;
                        MarkStateChanged(pair.Key);
                    }
                }
                foreach (KeyValuePair<VulkanBuffer, VulkanResourceState> pair in _bufferStates)
                {
                    if (!device._bufferStates.TryGetValue(pair.Key, out VulkanResourceState old) || old != pair.Value)
                    {
                        device._bufferStates[pair.Key] = pair.Value;
                        MarkStateChanged(pair.Key);
                    }
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
    /// usages stay in GENERAL because binds inside a rendering scope cannot
    /// transition attachment layouts, so every bindable texture must be in
    /// GENERAL at bind time — which the queued bind-time transition
    /// (MarkTexture + FlushPendingBarriers) establishes. Swapchain images
    /// (PreferGeneralLayout) stay in GENERAL for everything except present:
    /// their attachment barriers are recorded in a command buffer that
    /// interleaves with the main frame buffer, so optimal-layout transitions
    /// could land in the wrong submission.
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
            VulkanResourceState.DepthWrite => (VkPipelineStageFlags2.EarlyFragmentTests | VkPipelineStageFlags2.LateFragmentTests, VkAccessFlags2.DepthStencilAttachmentWrite | VkAccessFlags2.DepthStencilAttachmentRead),
            VulkanResourceState.DepthRead => (VkPipelineStageFlags2.EarlyFragmentTests | VkPipelineStageFlags2.LateFragmentTests, VkAccessFlags2.DepthStencilAttachmentRead),
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
