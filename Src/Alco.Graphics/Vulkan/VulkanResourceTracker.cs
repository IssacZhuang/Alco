using System.Runtime.CompilerServices;
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
/// "implicit synchronization" model: the caller never records barriers manually;
/// the tracker inserts them where the API surface allows.
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
    private struct TrackedImage
    {
        public VkImage Image;
        public uint MipLevels;
        public uint ArrayLayers;
    }

    private readonly Dictionary<VulkanTexture, VulkanResourceState> _imageStates = new();
    private readonly Dictionary<VulkanBuffer, VulkanResourceState> _bufferStates = new();

    // resources touched by the pass currently being recorded (cleared at pass end)
    private readonly HashSet<object> _passTouched = new();

    // uploads may run on threadpool threads concurrently with the render thread;
    // every public entry takes this gate (Monitor is re-entrant for internal calls)
    private readonly object _gate = new();

    public VulkanResourceState GetTextureState(VulkanTexture texture)
    {
        lock (_gate)
        {
            return _imageStates.TryGetValue(texture, out VulkanResourceState state) ? state : VulkanResourceState.Undefined;
        }
    }

    public VulkanResourceState GetBufferState(VulkanBuffer buffer)
    {
        lock (_gate)
        {
            return _bufferStates.TryGetValue(buffer, out VulkanResourceState state) ? state : VulkanResourceState.Undefined;
        }
    }

    /// <summary>Forgets the tracked state (e.g. a swapchain image that just came back
    /// from present with undefined contents).</summary>
    public void InvalidateTexture(VulkanTexture texture)
    {
        lock (_gate)
        {
            _imageStates[texture] = VulkanResourceState.Undefined;
        }
    }

    public void Remove(VulkanTexture texture) { lock (_gate) _imageStates.Remove(texture); }
    public void Remove(VulkanBuffer buffer) { lock (_gate) _bufferStates.Remove(buffer); }

    /// <summary>
    /// Records a barrier transitioning <paramref name="texture"/> into
    /// <paramref name="target"/> and updates the tracked state. Only call from outside
    /// a render/compute pass (attachment entry, copies, present).
    /// </summary>
    public void TransitionTexture(VkCommandBuffer commandBuffer, VulkanTexture texture, VulkanResourceState target)
    {
        lock (_gate)
        {
        VulkanResourceState source = GetTextureState(texture);
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
        lock (_gate)
        {
        VulkanResourceState source = GetBufferState(buffer);
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

        lock (_gate)
        {
            int changeCount = 0;
            foreach (BatchTransition t in targets)
            {
                if (GetTextureState(t.Texture) != t.Target)
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
                VulkanResourceState source = GetTextureState(t.Texture);
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
    /// give a resource a single usage per pass, and the previous pass flushed its
    /// writes when it ended.
    /// </summary>
    public void MarkTexture(VulkanTexture texture, VulkanResourceState target)
    {
        lock (_gate)
        {
            _imageStates[texture] = target;
        }
    }

    public void MarkBuffer(VulkanBuffer buffer, VulkanResourceState target)
    {
        lock (_gate)
        {
            _bufferStates[buffer] = target;
        }
    }

    /// <summary>Combined (stage, access) source scope of a bound resource state —
    /// pure mapping, no tracker state involved.</summary>
    internal static (VkPipelineStageFlags2 Stage, VkAccessFlags2 Access) ScopeOf(
        object resource, VulkanResourceState state)
        => resource is VulkanTexture ? ImageScope(state) : BufferScope(state);

    // source scopes contributed by render-bundle executes since the last
    // FlushPass; folded into the pass-flush union barrier so bundle-bound
    // resources are hazard-covered without per-resource tracker marks
    private VkPipelineStageFlags2 _bundleSrcStage;
    private VkAccessFlags2 _bundleSrcAccess;

    /// <summary>Accumulates the source scope of one render-bundle execute into the
    /// current pass's flush union (one call per execute, no per-resource work).</summary>
    public void UnionBundleScope(VkPipelineStageFlags2 stage, VkAccessFlags2 access)
    {
        lock (_gate)
        {
            _bundleSrcStage |= stage;
            _bundleSrcAccess |= access;
        }
    }

    /// <summary>Registers a resource touched by the currently open pass so its writes
    /// are flushed when the pass ends.</summary>
    public void TouchInPass(object resource)
    {
        lock (_gate)
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
        lock (_gate)
        {
            // render-bundle executes since the last flush contribute their read
            // scopes here; one union barrier covers them with the direct binds
            VkPipelineStageFlags2 bundleStage = _bundleSrcStage;
            VkAccessFlags2 bundleAccess = _bundleSrcAccess;
            _bundleSrcStage = VkPipelineStageFlags2.TopOfPipe;
            _bundleSrcAccess = VkAccessFlags2.None;

            if (_passTouched.Count == 0 && bundleStage != VkPipelineStageFlags2.TopOfPipe)
            {
                return;
            }

            int restoreCount = 0;
            foreach (object resource in _passTouched)
            {
                if (resource is VulkanTexture texture
                    && LayoutForState(texture, GetTextureState(texture)) != VkImageLayout.General)
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
                    VulkanResourceState state = GetTextureState(texture);
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
                    (VkPipelineStageFlags2 s, VkAccessFlags2 a) = BufferScope(GetBufferState(buffer));
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
        lock (_gate)
        {
            VulkanResourceState state = GetTextureState(texture);
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
        lock (_gate)
        {
            _passTouched.Clear();
        }
    }

    /// <summary>
    /// Emits a wide barrier making writes in <paramref name="writerState"/> scope
    /// visible to any later command in any submission. Used after out-of-pass writes
    /// (copies, query resolves) so readers never need cross-submit assumptions.
    /// </summary>
    public void MakeWritesVisible(VkCommandBuffer commandBuffer, VulkanResourceState writerState)
    {
        (VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess) = ImageScope(writerState);
        lock (_gate)
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
            return state == VulkanResourceState.Present || state == VulkanResourceState.Undefined
                ? (state == VulkanResourceState.Present ? VkImageLayout.PresentSrcKHR : VkImageLayout.Undefined)
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
        lock (_gate)
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
            VulkanResourceState.ShaderRead => (VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead | VkAccessFlags2.ShaderRead),
            VulkanResourceState.ShaderWrite => (VkPipelineStageFlags2.ComputeShader, VkAccessFlags2.ShaderWrite),
            VulkanResourceState.ShaderReadWrite => (VkPipelineStageFlags2.ComputeShader | VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderRead | VkAccessFlags2.ShaderWrite),
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
