using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vma;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>Boolean adapter features the backend pipeline creation code consults.</summary>
internal sealed class VulkanDeviceFeatures
{
    public bool SamplerAnisotropy { get; init; }
    public bool DepthBounds { get; init; }
    public bool TextureCompressionBC { get; init; }
    /// <summary>The device's <c>limits.maxSamplerAnisotropy</c> (only meaningful when
    /// <see cref="SamplerAnisotropy"/> is supported).</summary>
    public float MaxSamplerAnisotropy { get; init; }
}

/// <summary>
/// Native Vulkan device: instance/physical device/logical device wiring, VMA
/// allocator, the graphics queue, command buffer pools, blocking uploads/readbacks,
/// asynchronous texture readbacks, deferred native destruction and debug naming.
/// Synchronization policy lives in <see cref="VulkanResourceTracker"/> and the
/// swapchain's frame slots.
/// </summary>
internal sealed unsafe class VulkanDevice : GPUDevice
{
    private const string InstanceLayerValidation = "VK_LAYER_KHRONOS_validation";
    private const string ExtensionDebugUtils = "VK_EXT_debug_utils";
    private const string ExtensionSurface = "VK_KHR_surface";
    private const string ExtensionSwapchain = "VK_KHR_swapchain";

    private readonly bool _debug;
    private readonly PixelFormat _preferredSurfaceFormat;

    public VkInstance Instance { get; private set; }
    public VkPhysicalDevice PhysicalDevice { get; private set; }
    public VkDevice NativeDevice { get; private set; }
    public VkQueue Queue { get; private set; }

    // device-wide completion timeline (wgpu model): every queue submission
    // signals the next rising value; waiting a value from any thread is a pure
    // read of a monotonic counter, so command-buffer re-record waits and
    // frame-slot throttle waits can coexist without fence reset races
    private VkSemaphore _timelineSemaphore;
    private long _timelineValue;
    public int QueueFamilyIndex { get; private set; }
    public VmaAllocator Allocator { get; private set; }

    public VulkanResourceTracker Tracker { get; } = new();

    public VulkanDeviceFeatures Features { get; private set; } = new();

    public override GraphicsBackend Backend => GraphicsBackend.NativeVulkan;

    public override PixelFormat PreferredSurfaceFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSurfaceFormat;
    }

    private GPUFeatures _supportedFeatures;
    public override GPUFeatures SupportedFeatures => _supportedFeatures;

    private float _timestampPeriodNanoseconds;
    public override float TimestampPeriodNanoseconds => _timestampPeriodNanoseconds;
    private int _maxBindGroups = 8;
    public override int MaxBindGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _maxBindGroups;
    }

    // ===== default bind groups =====
    public override GPUBindGroup BindGroupUniformBuffer { get; }
    public override GPUBindGroup BindGroupStorageBuffer { get; }
    public override GPUBindGroup BindGroupStorageBufferWithCounter { get; }
    public override GPUBindGroup BindGroupTexture2DRead { get; }
    public override GPUBindGroup BindGroupTexture2DStorage { get; }
    public override GPUBindGroup BindGroupTexture3DRead { get; }

    // ===== command pools / one-shot submissions =====
    private VkCommandPool _commandPool;
    // uploads record on worker threads while the main thread records frames,
    // so one-shot buffers live in their own pool guarded by _queueLock
    private VkCommandPool _uploadPool;
    // zero-alloc handle pools: a ConcurrentStack allocates a fresh node on
    // every push, which is measurable garbage in the per-frame retire cycle;
    // pre-sized Stacks with an uncontended gate recycle without touching the
    // GC heap
    private readonly object _oneShotPoolLock = new();
    private readonly Stack<VkCommandBuffer> _oneShotFree = new(16);

    // vkQueueSubmit requires external synchronization, and uploads can run on
    // threadpool threads concurrently with the render thread
    private readonly object _queueLock = new();
    // the frame command pool is externally synchronized too (render bundles
    // allocate secondaries on worker threads) but is independent of the queue,
    // so it gets its own gate — pool allocation never waits on submits
    private readonly object _commandPoolLock = new();
    private readonly object _blockingFenceLock = new();
    private readonly Stack<VkFence> _blockingFencePool = new(4);
    // fences for fire-and-forget one-shots; pooled and recycled by ProcessUploads
    private readonly object _asyncFenceLock = new();
    private readonly Stack<VkFence> _asyncFencePool = new(4);

    // ===== asynchronous queue uploads (wgpu queue-write semantics: submit
    // without waiting, retire the staging buffer once the fence signals) =====
    private struct PendingUpload
    {
        public VkFence Fence;
        public VkCommandBuffer CommandBuffer;
        public StagingBuffer Staging;
    }
    // arrivals guarded by _queueLock (submitters already hold it); the frame
    // pump swaps the live queue out under the same lock, so a per-frame
    // one-shot never churns ConcurrentQueue segments. The active list is
    // main-thread only, so retirement never contends with arrivals
    private Queue<PendingUpload> _uploadsLive = new(16);
    private Queue<PendingUpload>? _uploadsDrain;
    private readonly List<PendingUpload> _activeUploads = new();

    // one-shot buffers submitted against an externally owned fence (present
    // barriers signal the swapchain slot fence); retired by frame distance
    // once the slot handshake guarantees the GPU finished them. Riders folded
    // into a destroyed command buffer's submission retire at the disposal
    // delay instead (their fence dies with the wrapper).
    private struct PendingOneShot
    {
        public long FrameStamp;
        public int Delay;
        public VkCommandBuffer CommandBuffer;
    }
    private readonly List<PendingOneShot> _pendingOneShots = new();

    // ===== deferred texture layout initialization =====
    // fresh images need one Undefined -> GENERAL transition before first use;
    // batching them (instead of one submission per texture) keeps asset-load
    // bursts from flooding the queue with tiny submissions and fences
    private readonly ConcurrentQueue<VulkanTexture> _pendingTextureInits = new();

    // ===== deferred buffer copy arena =====
    // The engine updates dynamic buffers thousands of times per frame (per-object
    // uniforms into a handful of persistent buffers). Submitting one command
    // buffer per write is prohibitively slow, so writes are memcpy'd into a ring
    // of host-visible arena chunks and flushed as ONE submission before the next
    // queue submission consumes them (mirrors wgpu's internal staging ring).
    private struct PendingBufferCopy
    {
        public VulkanBuffer Destination;
        public ulong DestinationOffset;
        public VkBuffer Source;
        public ulong SourceOffset;
        public ulong Size;
    }

    private struct ArenaChunk
    {
        public VkBuffer Buffer;
        public VmaAllocation Allocation;
        public void* Mapped;
        public ulong Size;
        public ulong Used;
    }

    private const int ArenaSlotCount = 3; // ≥ frames in flight + 1
    private readonly List<ArenaChunk>[] _arenaSlots = new List<ArenaChunk>[ArenaSlotCount];
    // retired chunks wait one full ring cycle here before re-entering the free
    // pool: the same frame distance the old destroy path relied on (retire + 3
    // disposal delay frames), so a chunk is never rewritten while the GPU may
    // still be reading its previous contents
    private readonly List<ArenaChunk>[] _retiredArenaChunks = new List<ArenaChunk>[ArenaSlotCount];
    private readonly List<ArenaChunk> _freeArenaChunks = new();
    // idle pool memory budget; chunks above it are released so an asset-load
    // burst cannot pin staging memory forever
    private const ulong FreeArenaChunkBudget = 64u * 1024 * 1024;
    // copy entries are published only after their memcpy finished, so a
    // concurrent flush can never hand the GPU a half-written region.
    // Pre-sized ping-pong queues instead of a ConcurrentQueue: a queue that
    // drains to empty every frame would allocate a fresh 32-slot segment on
    // each refill, so the hot path stays off the GC heap entirely
    private readonly object _copiesLock = new();
    private Queue<PendingBufferCopy> _copiesLive = new(256);
    private Queue<PendingBufferCopy>? _copiesDrain;
    // flush scratch, reused under _queueLock (clear-per-use keeps capacity)
    private readonly List<PendingBufferCopy> _drainedCopies = new(256);
    private readonly HashSet<VulkanBuffer> _transitionedBuffers = new();
    // guards only arena chunk reservation and slot rotation; the WriteBuffer
    // memcpy (hottest path in the engine) runs entirely outside this lock
    private readonly object _arenaLock = new();
    private long _uploadFrame;

    private List<ArenaChunk> ArenaSlot(int slot)
    {
        List<ArenaChunk> list = _arenaSlots[slot] ??= new List<ArenaChunk>();
        return list;
    }

    /// <summary>Copies <paramref name="data"/> into the frame's arena chunk and
    /// enqueues a deferred copy into <paramref name="buffer"/>. The copy executes
    /// as part of the next queue submission, ahead of everything recorded after
    /// this call.</summary>
    private unsafe void EnqueueBufferUpload(VulkanBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        ArenaChunk chunk;
        ulong sourceOffset;
        lock (_arenaLock)
        {
            List<ArenaChunk> chunks = ArenaSlot((int)(_uploadFrame % ArenaSlotCount));
            int index = -1;
            chunk = default;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i].Size - chunks[i].Used >= size)
                {
                    chunk = chunks[i];
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                // reuse a recycled chunk before growing the pool; steady-state
                // frames hit this every time and never allocate at all
                for (int i = 0; i < _freeArenaChunks.Count; i++)
                {
                    if (_freeArenaChunks[i].Size >= size)
                    {
                        chunk = _freeArenaChunks[i];
                        _freeArenaChunks.RemoveAt(i);
                        break;
                    }
                }
                if (chunk.Buffer.Handle == 0)
                {
                    ulong chunkSize = Math.Max(8u * 1024 * 1024, VulkanUtility.AlignUp((ulong)size, 65536));
                    chunk = CreateArenaChunk(chunkSize);
                }
                chunks.Add(chunk);
                index = chunks.Count - 1;
            }

            sourceOffset = chunk.Used;
            chunk.Used += size;
            chunks[index] = chunk;
        }

        // the memcpy runs OUTSIDE the lock: only the space reservation must be
        // atomic. Dynamic buffer writes hit this path thousands of times per
        // frame, so the lock is the shortest possible bounded scan.
        Buffer.MemoryCopy(data, (byte*)chunk.Mapped + sourceOffset, size, size);

        // publish only after the bytes landed, so a concurrent flush can never
        // submit a half-written region to the GPU
        PendingBufferCopy copy = new()
        {
            Destination = buffer,
            DestinationOffset = bufferOffset,
            Source = chunk.Buffer,
            SourceOffset = sourceOffset,
            Size = size,
        };
        lock (_copiesLock)
        {
            _copiesLive.Enqueue(copy);
        }
    }

    private unsafe ArenaChunk CreateArenaChunk(ulong size)
    {
        VkBufferCreateInfo bufferInfo = new()
        {
            size = size,
            usage = VkBufferUsageFlags.TransferSrc,
            sharingMode = VkSharingMode.Exclusive,
        };
        VmaAllocationCreateInfo allocInfo = new()
        {
            usage = VmaMemoryUsage.Auto,
            requiredFlags = VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
        };
        ArenaChunk chunk = default;
        chunk.Size = size;
        VkResult result = vmaCreateBuffer(Allocator, &bufferInfo, &allocInfo, &chunk.Buffer, &chunk.Allocation, null);
        VulkanException.ThrowIfFailed(result, "Failed to create upload arena chunk");
        result = vmaMapMemory(Allocator, chunk.Allocation, &chunk.Mapped);
        VulkanException.ThrowIfFailed(result, "Failed to map upload arena chunk");
        return chunk;
    }

    /// <summary>Records pending texture layout initializations and buffer copies
    /// into <paramref name="commandBuffer"/>. Returns whether anything was
    /// recorded (an empty lead buffer must not ride a submission).</summary>
    private bool RecordDeferredWorkLocked(VkCommandBuffer commandBuffer)
    {
        bool recorded = false;
        while (_pendingTextureInits.TryDequeue(out VulkanTexture texture))
        {
            // a worker-thread upload may already have moved the state past
            // Undefined; only untouched images still need the transition
            if (Tracker.GetTextureState(texture) == VulkanResourceState.Undefined)
            {
                Tracker.TransitionTexture(commandBuffer, texture, VulkanResourceState.Idle);
                recorded = true;
            }
        }

        Queue<PendingBufferCopy> copies;
        lock (_copiesLock)
        {
            if (_copiesLive.Count == 0)
            {
                return recorded;
            }
            copies = _copiesLive;
            _copiesLive = _copiesDrain ?? new Queue<PendingBufferCopy>(256);
            _copiesDrain = null;
        }

        // reused scratch: flushes are serialized under _queueLock, so the
        // per-frame flush never allocates; Clear keeps the capacity
        List<PendingBufferCopy> drained = _drainedCopies;
        HashSet<VulkanBuffer> transitioned = _transitionedBuffers;
        drained.Clear();
        transitioned.Clear();
        while (copies.TryDequeue(out PendingBufferCopy copy))
        {
            drained.Add(copy);
            if (transitioned.Add(copy.Destination))
            {
                Tracker.TransitionBuffer(commandBuffer, copy.Destination, VulkanResourceState.CopyDst);
            }
        }

        foreach (PendingBufferCopy copy in drained)
        {
            VkBuffer nativeSource = copy.Source;
            VkBuffer nativeDestination = copy.Destination.Native;
            VkBufferCopy region = new()
            {
                srcOffset = copy.SourceOffset,
                dstOffset = copy.DestinationOffset,
                size = copy.Size,
            };
            vkCmdCopyBuffer(commandBuffer, nativeSource, nativeDestination, 1, &region);
        }

        // the drained queue becomes the next swap target
        _copiesDrain = copies;

        Tracker.MakeWritesVisible(commandBuffer, VulkanResourceState.CopyDst);
        return true;
    }

    /// <summary>Whether deferred texture initializations or buffer copies are
    /// waiting for the next submission. Caller must hold
    /// <see cref="_queueLock"/>.</summary>
    private bool HasDeferredWorkLocked()
    {
        lock (_copiesLock)
        {
            if (_copiesLive.Count > 0)
            {
                return true;
            }
        }
        return !_pendingTextureInits.IsEmpty;
    }

    /// <summary>Rotates the upload arena: chunks that sat out the safety
    /// distance in the retired ring re-enter the free pool for reuse, and the
    /// chunks of the slot the next frame writes into are retired in their
    /// place. Retiring plus a full ring cycle matches the frame distance the
    /// old destroy path relied on, so the GPU is guaranteed done reading a
    /// chunk's previous contents before it is rewritten. Free-pool memory
    /// above the budget is released so a burst cannot pin staging memory
    /// forever.</summary>
    private unsafe void RotateUploadArena()
    {
        lock (_arenaLock)
        {
            int slot = (int)((_uploadFrame + 1) % ArenaSlotCount);

            // recycle chunks retired one full ring cycle ago: their last write
            // is now the same distance away the old dispose-after-3-frames
            // path guaranteed, so they are safe to hand out again
            if (_retiredArenaChunks[slot] is { } recycled)
            {
                foreach (ArenaChunk chunk in recycled)
                {
                    ArenaChunk reset = chunk;
                    reset.Used = 0;
                    _freeArenaChunks.Add(reset);
                }
                recycled.Clear();
            }

            // retire the slot the next frame owns; its chunks re-enter the
            // free pool when this slot index comes up for rotation again
            List<ArenaChunk> chunks = ArenaSlot(slot);
            List<ArenaChunk> retired = _retiredArenaChunks[slot] ??= new List<ArenaChunk>();
            retired.AddRange(chunks);
            chunks.Clear();

            // shrink from the tail: burst-sized chunks were recycled last, and
            // steady-state 8MB chunks at the head get reused first
            ulong freeBytes = 0;
            foreach (ArenaChunk free in _freeArenaChunks)
            {
                freeBytes += free.Size;
            }
            for (int i = _freeArenaChunks.Count - 1; i >= 0 && freeBytes > FreeArenaChunkBudget; i--)
            {
                ArenaChunk chunk = _freeArenaChunks[i];
                freeBytes -= chunk.Size;
                _freeArenaChunks.RemoveAt(i);
                QueueDisposal(DisposalKind.BufferWithAllocation, chunk.Buffer.Handle, chunk.Allocation, chunk.Mapped);
            }

            _uploadFrame++;
        }
    }

    // ===== swapchains =====
    private VulkanSwapchain? _activeSwapchain;

    // ===== asynchronous texture readbacks =====
    private struct PendingReadback
    {
        public VkFence Fence;
        public VkCommandBuffer CommandBuffer;
        public StagingBuffer Staging;
        public byte* Destination;
        public uint DataSize;
        public uint Height;
        public uint Layers;
        public ulong TightRow;
        public ulong AlignedRow;
        public GPUTextureReadbackRequest Request;
    }
    private readonly ConcurrentQueue<PendingReadback> _readbackArrivals = new();
    private readonly List<PendingReadback> _activeReadbacks = new();

    // ===== deferred native destruction =====
    private enum DisposalKind : byte
    {
        BufferWithAllocation,
        ImageWithAllocation,
        ImageView,
        Sampler,
        QueryPool,
        Pipeline,
        PipelineLayout,
        Fence,
        SecondaryCommandBuffer,
        DescriptorPool,
        DescriptorSetLayout,
    }

    private struct PendingDisposal
    {
        public uint FramesLeft;
        public DisposalKind Kind;
        public ulong Handle;
        public VmaAllocation Allocation;
        public void* MappedPointer;
    }
    private const uint DisposalDelayFrames = 3;
    // arrivals from any thread (finalizers, DestroyImmediate on workers, main
    // thread) are lock-free; the aging list is main-thread only so destroys
    // never hold up an arriving finalizer
    private readonly ConcurrentQueue<PendingDisposal> _disposalArrivals = new();
    private readonly List<PendingDisposal> _agingDisposals = new();

    // ===== descriptor set retirement =====
    // sets released by destroyed resource groups may still be referenced by
    // in-flight command buffers; they recycle into the owning layout's free
    // list only after the same frame delay the disposal path relies on
    private struct RetiredDescriptorSet
    {
        public uint FramesLeft;
        public VulkanBindGroup Owner;
        public VkDescriptorSet Set;
    }
    // same pattern as disposals: lock-free arrivals, main-thread aging
    private readonly ConcurrentQueue<RetiredDescriptorSet> _retiredSetArrivals = new();
    private readonly List<RetiredDescriptorSet> _agingRetiredSets = new();

    // ===== live-object teardown registry =====
    // The engine may still hold reachable-but-undisposed wrappers when the device
    // shuts down (static singletons, material caches); their finalizers would run
    // after the native device died. Every object registers at creation, and
    // teardown force-destroys them while the native handles are still valid.
    private readonly List<WeakReference> _liveObjects = new();
    private readonly object _liveObjectsLock = new();

    /// <summary>Registers a newly created GPU object for teardown tracking and
    /// returns it — creation cores wrap their constructor call in this.</summary>
    internal T Track<T>(T gpuObject) where T : BaseGPUObject
    {
        lock (_liveObjectsLock)
        {
            _liveObjects.Add(new WeakReference(gpuObject));
        }
        return gpuObject;
    }

    /// <summary>Force-destroys every tracked object. Must run before the native
    /// device dies; in-flight GPU work is fenced by the caller (wait idle).</summary>
    private void DestroyTrackedObjects()
    {
        List<BaseGPUObject> targets = new();
        lock (_liveObjectsLock)
        {
            foreach (WeakReference reference in _liveObjects)
            {
                if (reference.Target is BaseGPUObject gpuObject)
                {
                    targets.Add(gpuObject);
                }
            }
            _liveObjects.Clear();
        }
        foreach (BaseGPUObject gpuObject in targets)
        {
            try
            {
                gpuObject.Destroy();
            }
            catch
            {
                // teardown is best effort
            }
        }
    }

    private VkDebugUtilsMessengerEXT _messenger;

    public VulkanDevice(in DeviceDescriptor descriptor) : base(descriptor)
    {
        _debug = descriptor.Debug;
        _preferredSurfaceFormat = descriptor.PreferredSurfaceFormat;

        CreateInstance();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateAllocator();
        CreateCommandPool();
        CreateUploadPool();
        CreateTimelineSemaphore();

        VkPhysicalDeviceProperties properties = default;
        vkGetPhysicalDeviceProperties(PhysicalDevice, &properties);
        LogInfo($"Graphics backend: Vulkan ({GetDeviceName(ref properties)}, API {properties.apiVersion.Major}.{properties.apiVersion.Minor}.{properties.apiVersion.Patch})");

        BindGroupUniformBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.UniformBuffer),
            },
        });
        BindGroupStorageBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
            },
        });
        BindGroupStorageBufferWithCounter = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer_with_counter",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
                new BindGroupEntry(1, ShaderStage.Standard, BindingType.StorageBuffer),
            },
        });
        BindGroupTexture2DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
            },
        });
        BindGroupTexture2DStorage = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_texture",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageTexture, null,
                    new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D, PixelFormat.RGBA8Unorm)),
            },
        });
        BindGroupTexture3DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_3d_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture3D)),
            },
        });
    }

    // ===== logging =====

    private void LogInfo(string message) => _host.LogInfo(message);
    internal void LogWarning(string message) => _host.LogWarning(message);

    // ===== instance / device creation =====

    private void CreateInstance()
    {
        // resolve the vulkan loader function pointers before any other call
        VkResult initResult = vkInitialize(null);
        VulkanException.ThrowIfFailed(initResult, "Failed to load the Vulkan loader library (vulkan-1.dll)");

        List<string> extensions = new() { ExtensionSurface };
        string platformExtension = OperatingSystem.IsWindows() ? "VK_KHR_win32_surface"
            : OperatingSystem.IsAndroid() ? "VK_KHR_android_surface"
            : OperatingSystem.IsMacOS() ? "VK_MVK_macos_surface"
            : "VK_KHR_xcb_surface";
        extensions.Add(platformExtension);

        List<string> layers = new();
        bool debugUtilsRequested = false;
        if (_debug)
        {
            extensions.Add(ExtensionDebugUtils);
            layers.Add(InstanceLayerValidation);
        }

        // only enable what the loader actually supports
        uint availableExtensionCount = 0;
        _ = vkEnumerateInstanceExtensionProperties(null, &availableExtensionCount, null);
        VkExtensionProperties* availableExtensions = stackalloc VkExtensionProperties[(int)Math.Max(1, availableExtensionCount)];
        _ = vkEnumerateInstanceExtensionProperties(null, &availableExtensionCount, availableExtensions);
        extensions = FilterExtensions(availableExtensions, availableExtensionCount, extensions);
        debugUtilsRequested = extensions.Contains(ExtensionDebugUtils);

        uint availableLayerCount = 0;
        _ = vkEnumerateInstanceLayerProperties(&availableLayerCount, null);
        VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)Math.Max(1, availableLayerCount)];
        _ = vkEnumerateInstanceLayerProperties(&availableLayerCount, availableLayers);
        layers = FilterLayers(availableLayers, availableLayerCount, layers);

        sbyte* applicationName = Str("Alco");
        sbyte* engineName = Str("Alco Engine");
        VkApplicationInfo applicationInfo = new()
        {
            pApplicationName = applicationName,
            applicationVersion = VkVersion.Version_1_0,
            pEngineName = engineName,
            engineVersion = VkVersion.Version_1_0,
            apiVersion = VkVersion.Version_1_3,
        };

        sbyte** extensionNames = AllocUtf8Array(extensions);
        sbyte** layerNames = AllocUtf8Array(layers);

        try
        {
            VkInstanceCreateInfo createInfo = new()
            {
                pApplicationInfo = &applicationInfo,
                enabledExtensionCount = (uint)extensions.Count,
                ppEnabledExtensionNames = extensionNames,
                enabledLayerCount = (uint)layers.Count,
                ppEnabledLayerNames = layerNames,
            };

            VkInstance instance = default;
            VkResult result = vkCreateInstance(&createInfo, null, &instance);
            VulkanException.ThrowIfFailed(result, "Failed to create Vulkan instance");
            Instance = instance;
            // resolve the instance-level (and instance-dispatchable) entry points
            vkLoadInstance(instance);

            if (debugUtilsRequested)
            {
                CreateDebugMessenger();
            }
        }
        finally
        {
            FreeUtf8Array(extensionNames, extensions.Count);
            FreeUtf8Array(layerNames, layers.Count);
        }
    }

    [UnmanagedCallersOnly]
    private static uint DebugCallback(
        VkDebugUtilsMessageSeverityFlagsEXT severity,
        VkDebugUtilsMessageTypeFlagsEXT types,
        VkDebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        string message = DecodeUtf8(data->pMessage);
        if (severity >= VkDebugUtilsMessageSeverityFlagsEXT.Error)
        {
            Console.Error.WriteLine($"[Vulkan validation] {message}");
        }
        else
        {
            Console.WriteLine($"[Vulkan validation] {message}");
        }
        return 0;
    }

    private void CreateDebugMessenger()
    {
        VkDebugUtilsMessengerCreateInfoEXT createInfo = new()
        {
            messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Error | VkDebugUtilsMessageSeverityFlagsEXT.Warning,
            messageType = VkDebugUtilsMessageTypeFlagsEXT.Validation | VkDebugUtilsMessageTypeFlagsEXT.Performance,
            pfnUserCallback = &DebugCallback,
        };

        VkDebugUtilsMessengerEXT messenger = default;
        VkResult result = vkCreateDebugUtilsMessengerEXT(Instance, &createInfo, null, &messenger);
        if (result == VkResult.Success)
        {
            _messenger = messenger;
        }
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _ = vkEnumeratePhysicalDevices(Instance, &deviceCount, null);
        if (deviceCount == 0)
        {
            throw new GraphicsException("No Vulkan-capable physical device found.");
        }

        VkPhysicalDevice* devices = stackalloc VkPhysicalDevice[(int)deviceCount];
        _ = vkEnumeratePhysicalDevices(Instance, &deviceCount, devices);

        // prefer a discrete Vulkan 1.3 GPU, then any Vulkan 1.3 GPU, then the first device
        PhysicalDevice = devices[0];
        VkPhysicalDeviceProperties bestProperties = default;
        vkGetPhysicalDeviceProperties(PhysicalDevice, &bestProperties);
        for (uint i = 1; i < deviceCount; i++)
        {
            VkPhysicalDeviceProperties properties = default;
            vkGetPhysicalDeviceProperties(devices[i], &properties);
            if (IsBetterDevice(ref properties, ref bestProperties))
            {
                PhysicalDevice = devices[i];
                bestProperties = properties;
            }
        }
    }

    /// <summary>Orders physical devices: Vulkan 1.3 support first (the backend's
    /// minimum), discrete GPUs second.</summary>
    private static bool IsBetterDevice(ref VkPhysicalDeviceProperties candidate, ref VkPhysicalDeviceProperties best)
    {
        bool candidateModern = candidate.apiVersion >= VkVersion.Version_1_3;
        bool bestModern = best.apiVersion >= VkVersion.Version_1_3;
        if (candidateModern != bestModern)
        {
            return candidateModern;
        }
        bool candidateDiscrete = candidate.deviceType == VkPhysicalDeviceType.DiscreteGpu;
        bool bestDiscrete = best.deviceType == VkPhysicalDeviceType.DiscreteGpu;
        return candidateDiscrete && !bestDiscrete;
    }

    private void CreateLogicalDevice()
    {
        // the backend requires Vulkan 1.3 (synchronization2 + dynamic rendering)
        VkPhysicalDeviceProperties requiredCheck = default;
        vkGetPhysicalDeviceProperties(PhysicalDevice, &requiredCheck);
        if (requiredCheck.apiVersion < VkVersion.Version_1_3)
        {
            throw new GraphicsException(
                $"The Vulkan backend requires a Vulkan 1.3 capable device, but '{GetDeviceName(ref requiredCheck)}' reports {requiredCheck.apiVersion.Major}.{requiredCheck.apiVersion.Minor}.{requiredCheck.apiVersion.Patch}.");
        }

        // query capabilities with the 1.3 chain, then request only what is needed;
        // must use new() — 'default' leaves the internal sType at 0
        VkPhysicalDeviceVulkan12Features vulkan12Features = new()
        {
            // monotonic device-wide completion tracker: submissions signal a
            // rising value, any thread can wait a value without resets (the
            // wgpu model — fence pools cannot be waited concurrently with a
            // reset without racing)
            timelineSemaphore = VkBool32.True,
        };
        VkPhysicalDeviceVulkan13Features vulkan13Features = new()
        {
            synchronization2 = VkBool32.True,
            dynamicRendering = VkBool32.True,
        };
        vulkan12Features.pNext = &vulkan13Features;
        VkPhysicalDeviceFeatures2 queryFeatures2 = new()
        {
            pNext = &vulkan12Features,
        };
        vkGetPhysicalDeviceFeatures2(PhysicalDevice, &queryFeatures2);

        VkPhysicalDeviceFeatures queried = queryFeatures2.features;
        Features = new VulkanDeviceFeatures
        {
            SamplerAnisotropy = queried.samplerAnisotropy == VkBool32.True,
            DepthBounds = queried.depthBounds == VkBool32.True,
            TextureCompressionBC = queried.textureCompressionBC == VkBool32.True,
            MaxSamplerAnisotropy = requiredCheck.limits.maxSamplerAnisotropy,
        };

        _supportedFeatures = GPUFeatures.None;
        if (Features.TextureCompressionBC)
        {
            _supportedFeatures |= GPUFeatures.TextureCompressionBC;
        }

        // enable exactly the core features the backend uses (enabling everything
        // the device reports trades driver latitude for nothing)
        VkPhysicalDeviceFeatures enabledFeatures = new()
        {
            samplerAnisotropy = Features.SamplerAnisotropy ? VkBool32.True : VkBool32.False,
            depthBounds = Features.DepthBounds ? VkBool32.True : VkBool32.False,
            textureCompressionBC = Features.TextureCompressionBC ? VkBool32.True : VkBool32.False,
        };
        VkPhysicalDeviceFeatures2 features2 = new()
        {
            pNext = &vulkan12Features,
            features = enabledFeatures,
        };

        // queue family: graphics + compute
        uint familyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &familyCount, null);
        VkQueueFamilyProperties* families = stackalloc VkQueueFamilyProperties[(int)familyCount];
        vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &familyCount, families);

        QueueFamilyIndex = -1;
        for (uint i = 0; i < familyCount; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) != 0 && (families[i].queueFlags & VkQueueFlags.Compute) != 0)
            {
                QueueFamilyIndex = (int)i;
                if (families[i].timestampValidBits > 0)
                {
                    _supportedFeatures |= GPUFeatures.TimestampQuery | GPUFeatures.TimestampQueryInsidePasses;
                }
                break;
            }
        }
        if (QueueFamilyIndex < 0)
        {
            throw new GraphicsException("No Vulkan queue family with graphics and compute support.");
        }

        float queuePriority = 1.0f;
        VkDeviceQueueCreateInfo queueCreateInfo = new()
        {
            queueFamilyIndex = (uint)QueueFamilyIndex,
            queueCount = 1,
            pQueuePriorities = &queuePriority,
        };

        List<string> deviceExtensions = new() { ExtensionSwapchain };
        sbyte** extensionNames = AllocUtf8Array(deviceExtensions);

        try
        {
            VkDeviceCreateInfo createInfo = new()
            {
                pNext = &features2,
                queueCreateInfoCount = 1,
                pQueueCreateInfos = &queueCreateInfo,
                enabledExtensionCount = (uint)deviceExtensions.Count,
                ppEnabledExtensionNames = extensionNames,
                pEnabledFeatures = null, // features come through the features2 chain
            };

            VkDevice device = default;
            VkResult result = vkCreateDevice(PhysicalDevice, &createInfo, null, &device);
            VulkanException.ThrowIfFailed(result, "Failed to create Vulkan logical device");
            NativeDevice = device;
            // resolve the device-level entry points
            vkLoadDevice(device);

            VkQueue queue = default;
            vkGetDeviceQueue(NativeDevice, (uint)QueueFamilyIndex, 0, &queue);
            Queue = queue;

            VkPhysicalDeviceProperties properties = default;
            vkGetPhysicalDeviceProperties(PhysicalDevice, &properties);
            _timestampPeriodNanoseconds = properties.limits.timestampPeriod;
            _maxBindGroups = (int)Math.Clamp(properties.limits.maxBoundDescriptorSets, 1u, 8u);
        }
        finally
        {
            FreeUtf8Array(extensionNames, deviceExtensions.Count);
        }
    }

    private void CreateAllocator()
    {
        VmaAllocatorCreateInfo allocatorInfo = new()
        {
            physicalDevice = PhysicalDevice,
            device = NativeDevice,
            instance = Instance,
            vulkanApiVersion = VkVersion.Version_1_3,
        };

        VmaAllocator allocator = default;
        VkResult result = vmaCreateAllocator(&allocatorInfo, &allocator);
        VulkanException.ThrowIfFailed(result, "Failed to create VMA allocator");
        Allocator = allocator;
    }

    private void CreateCommandPool()
    {
        VkCommandPoolCreateInfo poolInfo = new()
        {
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
            queueFamilyIndex = (uint)QueueFamilyIndex,
        };

        VkCommandPool pool = default;
        VkResult result = vkCreateCommandPool(NativeDevice, &poolInfo, null, &pool);
        VulkanException.ThrowIfFailed(result, "Failed to create command pool");
        _commandPool = pool;
    }

    private void CreateUploadPool()
    {
        VkCommandPoolCreateInfo poolInfo = new()
        {
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
            queueFamilyIndex = (uint)QueueFamilyIndex,
        };

        VkCommandPool pool = default;
        VkResult result = vkCreateCommandPool(NativeDevice, &poolInfo, null, &pool);
        VulkanException.ThrowIfFailed(result, "Failed to create upload command pool");
        _uploadPool = pool;
    }

    private void CreateTimelineSemaphore()
    {
        VkSemaphoreTypeCreateInfo typeInfo = new()
        {
            semaphoreType = VkSemaphoreType.Timeline,
            initialValue = 0,
        };
        VkSemaphoreCreateInfo createInfo = new()
        {
            pNext = &typeInfo,
        };
        VkSemaphore semaphore = default;
        VkResult result = vkCreateSemaphore(NativeDevice, &createInfo, null, &semaphore);
        VulkanException.ThrowIfFailed(result, "Failed to create timeline semaphore");
        _timelineSemaphore = semaphore;
        _timelineValue = 0;
    }

    /// <summary>The timeline value of the most recent submission; waiting any
    /// value at or below it is guaranteed to complete. Thread-safe read.</summary>
    internal long CurrentTimelineValue => Volatile.Read(ref _timelineValue);

    /// <summary>Takes the next timeline signal value. Call under
    /// <see cref="_queueLock"/> so values are handed out in submission order.</summary>
    private long NextTimelineValueLocked() => ++_timelineValue;

    /// <summary>Blocks until the device timeline reaches <paramref name="value"/>.
    /// Safe from any thread and concurrent with other waits (no reset involved).</summary>
    internal void WaitTimeline(long value)
    {
        if (value <= 0)
        {
            return; // nothing was ever submitted under this ticket
        }
        ulong v = (ulong)value;
        VkSemaphore semaphore = _timelineSemaphore;
        VkSemaphoreWaitInfo waitInfo = new()
        {
            semaphoreCount = 1,
            pSemaphores = &semaphore,
            pValues = &v,
        };
        VkResult result = vkWaitSemaphores(NativeDevice, &waitInfo, ulong.MaxValue);
        VulkanException.ThrowIfFailed(result, "Failed to wait for timeline semaphore");
    }

    // ===== string helpers =====

    private static sbyte* Str(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)(utf8.Length + 1));
        Marshal.Copy(utf8, 0, (nint)buffer, utf8.Length);
        buffer[utf8.Length] = 0;
        return (sbyte*)buffer;
    }

    private static string DecodeUtf8(sbyte* value)
    {
        if (value == null)
        {
            return string.Empty;
        }
        int length = 0;
        while (value[length] != 0)
        {
            length++;
        }
        return Encoding.UTF8.GetString((byte*)value, length);
    }

    private static string GetDeviceName(ref VkPhysicalDeviceProperties properties)
    {
        Span<byte> name = stackalloc byte[256];
        int length = 0;
        for (int i = 0; i < 256; i++)
        {
            byte c = (byte)properties.deviceName[i];
            name[i] = c;
            if (c == 0)
            {
                length = i;
                break;
            }
        }
        return Encoding.UTF8.GetString(name[..length]);
    }

    private static sbyte** AllocUtf8Array(List<string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }
        sbyte** array = (sbyte**)NativeMemory.Alloc((nuint)(sizeof(byte*) * values.Count));
        for (int i = 0; i < values.Count; i++)
        {
            array[i] = Str(values[i]);
        }
        return array;
    }

    private static void FreeUtf8Array(sbyte** array, int count)
    {
        if (array == null)
        {
            return;
        }
        for (int i = 0; i < count; i++)
        {
            NativeMemory.Free(array[i]);
        }
        NativeMemory.Free(array);
    }

    private static unsafe List<string> FilterExtensions(VkExtensionProperties* extensions, uint count, List<string> requested)
    {
        List<string> result = new();
        foreach (string name in requested)
        {
            for (uint i = 0; i < count; i++)
            {
                if (ExtensionNameMatches(extensions + i, name))
                {
                    result.Add(name);
                    break;
                }
            }
        }
        return result;
    }

    private static unsafe List<string> FilterLayers(VkLayerProperties* layers, uint count, List<string> requested)
    {
        List<string> result = new();
        foreach (string name in requested)
        {
            for (uint i = 0; i < count; i++)
            {
                if (LayerNameMatches(layers + i, name))
                {
                    result.Add(name);
                    break;
                }
            }
        }
        return result;
    }

    private static unsafe bool ExtensionNameMatches(VkExtensionProperties* extension, string name)
        => FixedNameMatches(extension->extensionName, name);

    private static unsafe bool LayerNameMatches(VkLayerProperties* layer, string name)
        => FixedNameMatches(layer->layerName, name);

    private static unsafe bool FixedNameMatches(sbyte* buffer, string name)
    {
        ReadOnlySpan<byte> expected = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i < 256; i++)
        {
            byte c = (byte)buffer[i];
            byte want = (byte)(i < expected.Length ? expected[i] : 0);
            if (c != want)
            {
                return false;
            }
            if (c == 0)
            {
                return true;
            }
        }
        return false;
    }

    // ===== command buffer plumbing =====

    /// <summary>Allocates a primary command buffer from the frame pool. The pool
    /// is externally synchronized and shared with secondary-command-buffer
    /// allocation/freeing (render bundles record on worker threads); its gate is
    /// independent of the queue so allocation never waits on submits.</summary>
    public VkCommandBuffer AllocateCommandBuffer()
    {
        lock (_commandPoolLock)
        {
            VkCommandBufferAllocateInfo allocateInfo = new()
            {
                commandPool = _commandPool,
                level = VkCommandBufferLevel.Primary,
                commandBufferCount = 1,
            };

            VkCommandBuffer commandBuffer = default;
            VkResult result = vkAllocateCommandBuffers(NativeDevice, &allocateInfo, &commandBuffer);
            VulkanException.ThrowIfFailed(result, "Failed to allocate command buffer");
            return commandBuffer;
        }
    }

    /// <summary>Allocates a secondary command buffer from the frame pool (used by
    /// cached render bundle replays; bundles may be recorded on worker threads,
    /// so the externally synchronized pool needs its own gate).</summary>
    public VkCommandBuffer AllocateSecondaryCommandBuffer()
    {
        lock (_commandPoolLock)
        {
            VkCommandBufferAllocateInfo allocateInfo = new()
            {
                commandPool = _commandPool,
                level = VkCommandBufferLevel.Secondary,
                commandBufferCount = 1,
            };

            VkCommandBuffer commandBuffer = default;
            VkResult result = vkAllocateCommandBuffers(NativeDevice, &allocateInfo, &commandBuffer);
            VulkanException.ThrowIfFailed(result, "Failed to allocate secondary command buffer");
            return commandBuffer;
        }
    }

    /// <summary>Allocates a primary command buffer from the upload pool
    /// (must be called while holding <see cref="_queueLock"/>).</summary>
    public VkCommandBuffer AllocateUploadCommandBuffer()
    {
        VkCommandBufferAllocateInfo allocateInfo = new()
        {
            commandPool = _uploadPool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        VkCommandBuffer commandBuffer = default;
        VkResult result = vkAllocateCommandBuffers(NativeDevice, &allocateInfo, &commandBuffer);
        VulkanException.ThrowIfFailed(result, "Failed to allocate command buffer");
        return commandBuffer;
    }

    /// <summary>Waits for the command buffer's previous submission (timeline
    /// value, which covers the whole submission array — riders included) and
    /// resets the command buffer so recording can start again. The rider
    /// one-shots (deferred-work lead, present trail) finished too and return
    /// to the free pool here.</summary>
    public void PrepareCommandBuffer(VulkanCommandBuffer commandBuffer)
    {
        WaitTimeline(commandBuffer.LastSubmitTimelineValue);
        _ = vkResetCommandBuffer(commandBuffer.NativeCommandBuffer, VkCommandBufferResetFlags.None);

        if (commandBuffer.PendingLeadFlush.Handle != 0)
        {
            RetireRiderOneShotPool(commandBuffer.PendingLeadFlush);
            commandBuffer.PendingLeadFlush = default;
        }
        if (commandBuffer.PendingTrailBarrier.Handle != 0)
        {
            RetireRiderOneShotPool(commandBuffer.PendingTrailBarrier);
            commandBuffer.PendingTrailBarrier = default;
        }
    }

    /// <summary>Returns a rider one-shot (whose completion was just fence-waited)
    /// to the free pool.</summary>
    private void RetireRiderOneShotPool(VkCommandBuffer commandBuffer)
    {
        lock (_oneShotPoolLock)
        {
            _oneShotFree.Push(commandBuffer);
        }
    }

    /// <summary>Retires a rider one-shot of a DESTROYED command buffer: its only
    /// completion fence died with the wrapper and may never be waited, so retire
    /// by frame distance instead. The rider executed in the same submission as
    /// the wrapper's own command buffer, whose deferred free relies on the exact
    /// same delay — this is exactly as safe. May be called from any thread.</summary>
    public void RetireRiderOneShotByFrame(VkCommandBuffer commandBuffer)
    {
        lock (_queueLock)
        {
            _pendingOneShots.Add(new PendingOneShot
            {
                FrameStamp = FrameCounter,
                Delay = (int)DisposalDelayFrames,
                CommandBuffer = commandBuffer,
            });
        }
    }

    public VkFence CreateFenceNative(bool signaled)
    {
        VkFenceCreateInfo info = new()
        {
            flags = signaled ? VkFenceCreateFlags.Signaled : VkFenceCreateFlags.None,
        };
        VkFence fence = default;
        vkCreateFence(NativeDevice, &info, null, &fence).ThrowOnFailure();
        return fence;
    }

    /// <summary>Begins a one-shot upload command buffer. The caller must hold
    /// <see cref="_queueLock"/> until the submission is enqueued.</summary>
    private VkCommandBuffer BeginOneShotLocked()
    {
        VkCommandBuffer commandBuffer;
        lock (_oneShotPoolLock)
        {
            if (_oneShotFree.Count == 0)
            {
                commandBuffer = AllocateUploadCommandBuffer();
            }
            else
            {
                commandBuffer = _oneShotFree.Pop();
            }
        }

        _ = vkResetCommandBuffer(commandBuffer, VkCommandBufferResetFlags.None);
        VkCommandBufferBeginInfo beginInfo = new()
        {
            flags = VkCommandBufferUsageFlags.OneTimeSubmit,
        };
        vkBeginCommandBuffer(commandBuffer, &beginInfo).ThrowOnFailure();
        return commandBuffer;
    }

    /// <summary>Ends and submits a one-shot buffer with the given fence. The caller
    /// must hold <see cref="_queueLock"/>.</summary>
    private void SubmitOneShotLocked(VkCommandBuffer commandBuffer, VkFence fence)
    {
        vkEndCommandBuffer(commandBuffer).ThrowOnFailure();

        VkCommandBuffer native = commandBuffer;
        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
        };

        _ = vkResetFences(NativeDevice, 1, &fence);
        VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
        VulkanException.ThrowIfFailed(result, "Failed to submit one-shot command buffer");
        Tracker.BumpSerial();
    }

    /// <summary>Ends and submits a one-shot buffer with a pooled fence. The caller
    /// must hold <see cref="_queueLock"/>; returns the fence to wait on.</summary>
    private VkFence SubmitOneShotBlockingLocked(VkCommandBuffer commandBuffer)
    {
        vkEndCommandBuffer(commandBuffer).ThrowOnFailure();

        VkFence fence;
        lock (_blockingFenceLock)
        {
            if (_blockingFencePool.Count == 0)
            {
                fence = CreateFenceNative(signaled: false);
            }
            else
            {
                fence = _blockingFencePool.Pop();
            }
        }

        VkCommandBuffer native = commandBuffer;
        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
        };
        VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
        VulkanException.ThrowIfFailed(result, "Failed to submit one-shot command buffer");
        Tracker.BumpSerial();
        return fence;
    }

    /// <summary>Ends and submits a one-shot buffer WITHOUT waiting for it. The
    /// fence and staging buffer are retired later by <see cref="ProcessUploads"/>.
    /// The caller must hold <see cref="_queueLock"/>.</summary>
    private void SubmitOneShotAsyncLocked(VkCommandBuffer commandBuffer, StagingBuffer staging)
    {
        vkEndCommandBuffer(commandBuffer).ThrowOnFailure();

        VkFence fence;
        lock (_asyncFenceLock)
        {
            if (_asyncFencePool.Count == 0)
            {
                fence = CreateFenceNative(signaled: false);
            }
            else
            {
                fence = _asyncFencePool.Pop();
            }
        }

        VkCommandBuffer native = commandBuffer;
        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
        };
        _ = vkResetFences(NativeDevice, 1, &fence);
        VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
        VulkanException.ThrowIfFailed(result, "Failed to submit one-shot command buffer");
        Tracker.BumpSerial();

        _uploadsLive.Enqueue(new PendingUpload
        {
            Fence = fence,
            CommandBuffer = commandBuffer,
            Staging = staging,
        });
    }

    /// <summary>Batches the initial Undefined → GENERAL transition for a freshly
    /// created image; flushed at the head of the next submission (queue order
    /// guarantees it lands before any later submission that binds the texture).</summary>
    public void InitializeTextureLayout(VulkanTexture texture)
    {
        _pendingTextureInits.Enqueue(texture);
    }

    /// <summary>Resets a freshly created query pool so the first timestamp
    /// writes are legal (queries start unavailable-but-not-reset).</summary>
    public void ResetQueryPoolInitial(VkQueryPool queryPool, uint queryCount)
    {
        VkCommandBuffer commandBuffer;
        VkFence fence;
        lock (_queueLock)
        {
            commandBuffer = BeginOneShotLocked();
            vkCmdResetQueryPool(commandBuffer, queryPool, 0, queryCount);
            fence = SubmitOneShotBlockingLocked(commandBuffer);
        }

        VkResult resetResult = vkWaitForFences(NativeDevice, 1, &fence, true, ulong.MaxValue);
        VulkanException.ThrowIfFailed(resetResult, "Failed to wait for query pool reset");
        RecycleOneShot(commandBuffer, fence);
    }

    /// <summary>Returns a finished one-shot buffer and its fence to the lock-free
    /// pools. A null fence (owned externally, e.g. async readback) is skipped.</summary>
    private void RecycleOneShot(VkCommandBuffer commandBuffer, VkFence fence)
    {
        if (fence.Handle != 0)
        {
            _ = vkResetFences(NativeDevice, 1, &fence);
            lock (_blockingFenceLock)
            {
                _blockingFencePool.Push(fence);
            }
        }
        lock (_oneShotPoolLock)
        {
            _oneShotFree.Push(commandBuffer);
        }
    }



    /// <summary>Submits the present-layout transition for a swapchain image. The
    /// submission is ordered before present through the swapchain's semaphores;
    /// its one-shot command buffer is retired by frame distance through
    /// <see cref="ProcessOneShots"/> (the timeline value it signals is waited by
    /// the next-next BeginFrame, so its status cannot be polled for retirement).</summary>
    public void SubmitPresentBarrier(VulkanTexture texture, VulkanSwapchain swapchain)
    {
        lock (_queueLock)
        {
            VkCommandBuffer commandBuffer = BeginOneShotLocked();
            Tracker.TransitionTexture(commandBuffer, texture, VulkanResourceState.Present);
            vkEndCommandBuffer(commandBuffer);

            VkSemaphore waitSemaphore = swapchain.PendingAcquireSemaphore;
            VkSemaphore signalSemaphore = swapchain.TakeSubmitSemaphore();
            swapchain.ConsumeAcquireSemaphore();

            long timelineValue = NextTimelineValueLocked();
            VkCommandBufferSubmitInfo bufferInfo = new() { commandBuffer = commandBuffer };
            VkSemaphoreSubmitInfo* signalInfos = stackalloc VkSemaphoreSubmitInfo[2];
            uint signalCount = 0;
            if (signalSemaphore.Handle != 0)
            {
                signalInfos[signalCount++] = new VkSemaphoreSubmitInfo
                {
                    semaphore = signalSemaphore,
                    value = 0, // binary semaphore: value ignored
                    stageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
                };
            }
            signalInfos[signalCount++] = new VkSemaphoreSubmitInfo
            {
                semaphore = _timelineSemaphore,
                value = (ulong)timelineValue,
                stageMask = VkPipelineStageFlags2.AllCommands,
            };
            VkSemaphoreSubmitInfo waitInfo = new()
            {
                semaphore = waitSemaphore,
                value = 0,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
            };

            VkSubmitInfo2 submitInfo = new()
            {
                commandBufferInfoCount = 1,
                pCommandBufferInfos = &bufferInfo,
                waitSemaphoreInfoCount = waitSemaphore.Handle != 0 ? 1u : 0u,
                pWaitSemaphoreInfos = &waitInfo,
                signalSemaphoreInfoCount = signalCount,
                pSignalSemaphoreInfos = signalInfos,
            };

            VkResult result = vkQueueSubmit2(Queue, 1, &submitInfo, VkFence.Null);
            VulkanException.ThrowIfFailed(result, "Failed to submit present barrier");

            // the swapchain captures this value as the frame's end: BeginFrame
            // of the next slot cycle waits it before recycling slot resources,
            // after which the buffer is safely recyclable
            _pendingOneShots.Add(new PendingOneShot
            {
                FrameStamp = FrameCounter,
                Delay = VulkanSwapchain.FlightSlotCount,
                CommandBuffer = commandBuffer,
            });
            Tracker.BumpSerial();
        }
    }

    /// <summary>Retires externally fenced one-shots (present barriers) by frame
    /// distance, returning the command buffers to the free pool. Fence polling
    /// cannot work here: the swapchain slot fence a present barrier signals is
    /// reset by the next BeginFrame before this runs, so its status reads
    /// unsignaled forever. The slot handshake still guarantees completion —
    /// BeginFrame of the following cycle waits that same fence — so after the
    /// flight-slot frame distance the buffer is no longer executing. Rider
    /// one-shots of destroyed command buffers retire at the disposal delay.</summary>
    private void ProcessOneShots()
    {
        lock (_queueLock)
        {
            for (int i = _pendingOneShots.Count - 1; i >= 0; i--)
            {
                if (FrameCounter - _pendingOneShots[i].FrameStamp >= _pendingOneShots[i].Delay)
                {
                    lock (_oneShotPoolLock)
                    {
                        _oneShotFree.Push(_pendingOneShots[i].CommandBuffer);
                    }
                    _pendingOneShots.RemoveAt(i);
                }
            }
        }
    }

    // ===== debug naming =====

    public void SetDebugName(VkObjectType objectType, ulong handle, string name)
    {
        if (!_debug || handle == 0 || string.IsNullOrEmpty(name))
        {
            return;
        }

        sbyte* utf8 = Str(name);
        try
        {
            VkDebugUtilsObjectNameInfoEXT nameInfo = new()
            {
                objectType = objectType,
                objectHandle = handle,
                pObjectName = utf8,
            };
            _ = vkSetDebugUtilsObjectNameEXT(NativeDevice, &nameInfo);
        }
        finally
        {
            NativeMemory.Free(utf8);
        }
    }

    // ===== deferred native destruction =====

    private void QueueDisposal(DisposalKind kind, ulong handle, VmaAllocation allocation = default, void* mapped = null)
    {
        _disposalArrivals.Enqueue(new PendingDisposal
        {
            FramesLeft = DisposalDelayFrames,
            Kind = kind,
            Handle = handle,
            Allocation = allocation,
            MappedPointer = mapped,
        });
    }

    public void QueueNativeDestroy(VkBuffer buffer, VmaAllocation allocation, void* mappedPointer)
        => QueueDisposal(DisposalKind.BufferWithAllocation, buffer.Handle, allocation, mappedPointer);

    public void QueueNativeDestroy(VkImage image, VmaAllocation allocation)
        => QueueDisposal(DisposalKind.ImageWithAllocation, image.Handle, allocation);

    public void QueueNativeDestroy(VkImageView view) => QueueDisposal(DisposalKind.ImageView, view.Handle);
    public void QueueNativeDestroy(VkSampler sampler) => QueueDisposal(DisposalKind.Sampler, sampler.Handle);
    public void QueueNativeDestroy(VkQueryPool queryPool) => QueueDisposal(DisposalKind.QueryPool, queryPool.Handle);
    public void QueueNativeDestroy(VkPipeline pipeline) => QueueDisposal(DisposalKind.Pipeline, pipeline.Handle);
    public void QueueNativeDestroy(VkPipelineLayout layout) => QueueDisposal(DisposalKind.PipelineLayout, layout.Handle);
    public void QueueNativeDestroy(VkFence fence) => QueueDisposal(DisposalKind.Fence, fence.Handle);
    public void QueueNativeDestroy(VkDescriptorPool pool) => QueueDisposal(DisposalKind.DescriptorPool, pool.Handle);
    public void QueueNativeDestroy(VkDescriptorSetLayout layout) => QueueDisposal(DisposalKind.DescriptorSetLayout, layout.Handle);

    /// <summary>Deferred free of a secondary command buffer (must not free while
    /// the GPU may still execute it; the disposal delay covers frames in flight).</summary>
    public void QueueSecondaryCommandBufferFree(VkCommandBuffer commandBuffer)
        => QueueDisposal(DisposalKind.SecondaryCommandBuffer, (ulong)commandBuffer.Handle);

    /// <summary>Queues a descriptor set released by a destroyed resource group
    /// for frame-delayed recycling; may be called from any thread.</summary>
    internal void QueueDescriptorSetRetirement(VulkanBindGroup owner, VkDescriptorSet set)
    {
        _retiredSetArrivals.Enqueue(new RetiredDescriptorSet
        {
            FramesLeft = DisposalDelayFrames,
            Owner = owner,
            Set = set,
        });
    }

    private void ProcessRetiredDescriptorSets()
    {
        while (_retiredSetArrivals.TryDequeue(out RetiredDescriptorSet arrival))
        {
            _agingRetiredSets.Add(arrival);
        }

        for (int i = _agingRetiredSets.Count - 1; i >= 0; i--)
        {
            RetiredDescriptorSet retired = _agingRetiredSets[i];
            if (retired.FramesLeft > 0)
            {
                retired.FramesLeft--;
                _agingRetiredSets[i] = retired;
                continue;
            }

            // a dead owner drops the set: its pools are already queued for
            // destruction, which frees the set implicitly
            retired.Owner.RecycleSet(retired.Set);
            _agingRetiredSets.RemoveAt(i);
        }
    }

    /// <summary>Monotonic frame counter (main thread, once per OnEndFrame) used by
    /// render bundles to key cached secondary command buffers per in-flight frame.</summary>
    internal static long FrameCounter;

    private void ProcessDisposals()
    {
        // arrivals from any thread are lock-free; the aging list belongs to the
        // frame pump, so the destroys below never block an arriving finalizer
        while (_disposalArrivals.TryDequeue(out PendingDisposal arrival))
        {
            _agingDisposals.Add(arrival);
        }

        for (int i = _agingDisposals.Count - 1; i >= 0; i--)
        {
            PendingDisposal disposal = _agingDisposals[i];
            if (disposal.FramesLeft > 0)
            {
                disposal.FramesLeft--;
                _agingDisposals[i] = disposal;
                continue;
            }

            DestroyDisposal(disposal);
            _agingDisposals.RemoveAt(i);
        }
    }

    private unsafe void DestroyDisposal(in PendingDisposal disposal)
    {
        switch (disposal.Kind)
        {
            case DisposalKind.BufferWithAllocation:
            {
                VkBuffer buffer = new(disposal.Handle);
                if (disposal.MappedPointer != null)
                {
                    vmaUnmapMemory(Allocator, disposal.Allocation);
                }
                vmaDestroyBuffer(Allocator, buffer, disposal.Allocation);
                break;
            }
            case DisposalKind.ImageWithAllocation:
                vmaDestroyImage(Allocator, new VkImage(disposal.Handle), disposal.Allocation);
                break;
            case DisposalKind.ImageView:
                vkDestroyImageView(NativeDevice, new VkImageView(disposal.Handle), null);
                break;
            case DisposalKind.Sampler:
                vkDestroySampler(NativeDevice, new VkSampler(disposal.Handle), null);
                break;
            case DisposalKind.QueryPool:
                vkDestroyQueryPool(NativeDevice, new VkQueryPool(disposal.Handle), null);
                break;
            case DisposalKind.Pipeline:
                vkDestroyPipeline(NativeDevice, new VkPipeline(disposal.Handle), null);
                break;
            case DisposalKind.PipelineLayout:
                vkDestroyPipelineLayout(NativeDevice, new VkPipelineLayout(disposal.Handle), null);
                break;
            case DisposalKind.Fence:
                vkDestroyFence(NativeDevice, new VkFence(disposal.Handle), null);
                break;
            case DisposalKind.DescriptorPool:
                vkDestroyDescriptorPool(NativeDevice, new VkDescriptorPool(disposal.Handle), null);
                break;
            case DisposalKind.DescriptorSetLayout:
                vkDestroyDescriptorSetLayout(NativeDevice, new VkDescriptorSetLayout(disposal.Handle), null);
                break;
            case DisposalKind.SecondaryCommandBuffer:
            {
                // the pool is shared with worker-thread allocation, so free
                // under the pool gate
                VkCommandBuffer commandBuffer = new((nint)disposal.Handle);
                lock (_commandPoolLock)
                {
                    vkFreeCommandBuffers(NativeDevice, _commandPool, 1, &commandBuffer);
                }
                break;
            }
        }
    }

    // ===== creation cores =====

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor) => Track(new VulkanBuffer(this, descriptor));

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor) => Track(new VulkanTexture(this, descriptor));

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
        => Track(new VulkanCommandBuffer(this, descriptor));

    protected override GPUTimestampQuerySet CreateTimestampQuerySetCore(uint count, string name)
        => Track(new VulkanTimestampQuerySet(this, count, name));

    protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor)
        => Track(new VulkanRenderBundle(this, descriptor));

    protected override GPUAttachmentLayout CreateAttachmentLayoutCore(in AttachmentLayoutDescriptor descriptor)
        => Track(new VulkanAttachmentLayout(this, descriptor));

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
        => Track(new VulkanFrameBuffer(this, descriptor));

    protected override GPUFrameBuffer CreateExternalFrameBufferCore(in ExternalFrameBufferDescriptor descriptor)
        => Track(new VulkanExternalFrameBuffer(this, descriptor));

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
        => Track(VulkanPipeline.CreateGraphics(this, descriptor));

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
        => Track(VulkanPipeline.CreateCompute(this, descriptor));

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
        => Track(new VulkanBindGroup(this, descriptor));

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
        => Track(new VulkanResourceGroup(this, descriptor));

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
        => Track(new VulkanTextureView(this, descriptor));

    protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
        => Track(new VulkanSampler(this, descriptor));

    public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor)
    {
        VulkanSwapchain swapchain = Track(new VulkanSwapchain(this, descriptor));
        _activeSwapchain = swapchain;
        return swapchain;
    }

    // ===== submission =====

    /// <summary>Presents under the queue lock: presentation must be serialized
    /// against submissions targeting the same queue.</summary>
    internal VkResult PresentLocked(VkPresentInfoKHR* presentInfo)
    {
        lock (_queueLock)
        {
            return vkQueuePresentKHR(Queue, presentInfo);
        }
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        VulkanCommandBuffer commandBufferImpl = (VulkanCommandBuffer)commandBuffer;
        VulkanSwapchain? swapchain = _activeSwapchain;

        lock (_queueLock)
        {
            // lead: deferred texture initializations / buffer copies plus the
            // drift reconciliation prologue ride the SAME submission as the
            // recorded buffer (mirrors wgpu: one vkQueueSubmit per submit call).
            // Order matters: deferred work first (its tracker updates must be
            // visible to the prologue's state comparison), prologue second.
            VkCommandBuffer lead = default;
            bool hasDeferred = HasDeferredWorkLocked();
            bool needsPrologue = commandBufferImpl.Tracker.NeedsPrologue(Tracker);
            if (hasDeferred || needsPrologue)
            {
                VkCommandBuffer candidate = BeginOneShotLocked();
                bool recorded = false;
                if (hasDeferred)
                {
                    recorded |= RecordDeferredWorkLocked(candidate);
                }
                if (needsPrologue)
                {
                    recorded |= commandBufferImpl.Tracker.RecordPrologue(candidate, Tracker);
                }
                if (recorded)
                {
                    vkEndCommandBuffer(candidate).ThrowOnFailure();
                    lead = candidate;
                }
                else
                {
                    // nothing actually needed recording: recycle the begun
                    // buffer instead of submitting an empty one
                    RecycleOneShot(candidate, default);
                }
            }

            VkSemaphore waitSemaphore = default;
            VkSemaphore signalSemaphore = default;
            if (swapchain != null)
            {
                waitSemaphore = swapchain.PendingAcquireSemaphore;
                signalSemaphore = swapchain.TakeSubmitSemaphore();
            }

            // the recording's final states become the device-resolved states
            // BEFORE the trail below reads them (pure tracker bookkeeping; the
            // submit has not happened yet but nothing between here and the
            // vkQueueSubmit call observes the device tracker)
            commandBufferImpl.Tracker.AbsorbInto(Tracker);

            // trail: the acquired swapchain image's present-layout transition
            // appended to the same submission, so Present() needs no extra
            // vkQueueSubmit at all (wgpu bakes this into the submit too)
            VkCommandBuffer trail = default;
            if (swapchain != null
                && swapchain.AcquiredImageTexture is { } presentImage
                && commandBufferImpl.Tracker.RecordedTexture(presentImage)
                && Tracker.GetTextureState(presentImage) != VulkanResourceState.Present)
            {
                trail = BeginOneShotLocked();
                Tracker.TransitionTexture(trail, presentImage, VulkanResourceState.Present);
                vkEndCommandBuffer(trail);
                swapchain.NotePresentTrailRecorded();
            }

            // submission: command buffers [lead?, main, trail?] plus signal
            // infos — the present-wait semaphore (if presenting) and the
            // device timeline with a fresh value (the wgpu shape); a single
            // vkQueueSubmit2 carries the whole frame slice
            long timelineValue = NextTimelineValueLocked();
            VkCommandBufferSubmitInfo* bufferInfos = stackalloc VkCommandBufferSubmitInfo[3];
            uint bufferCount = 0;
            if (lead.Handle != 0)
            {
                bufferInfos[bufferCount++] = new VkCommandBufferSubmitInfo { commandBuffer = lead };
            }
            bufferInfos[bufferCount++] = new VkCommandBufferSubmitInfo
            {
                commandBuffer = commandBufferImpl.NativeCommandBuffer,
            };
            if (trail.Handle != 0)
            {
                bufferInfos[bufferCount++] = new VkCommandBufferSubmitInfo { commandBuffer = trail };
            }

            VkSemaphoreSubmitInfo* signalInfos = stackalloc VkSemaphoreSubmitInfo[2];
            uint signalCount = 0;
            if (signalSemaphore.Handle != 0)
            {
                signalInfos[signalCount++] = new VkSemaphoreSubmitInfo
                {
                    semaphore = signalSemaphore,
                    value = 0, // binary semaphore: value ignored
                    stageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
                };
            }
            signalInfos[signalCount++] = new VkSemaphoreSubmitInfo
            {
                semaphore = _timelineSemaphore,
                value = (ulong)timelineValue,
                stageMask = VkPipelineStageFlags2.AllCommands,
            };

            VkSemaphoreSubmitInfo waitInfo = new()
            {
                semaphore = waitSemaphore,
                value = 0,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
            };

            VkSubmitInfo2 submitInfo = new()
            {
                commandBufferInfoCount = bufferCount,
                pCommandBufferInfos = bufferInfos,
                waitSemaphoreInfoCount = waitSemaphore.Handle != 0 ? 1u : 0u,
                pWaitSemaphoreInfos = &waitInfo,
                signalSemaphoreInfoCount = signalCount,
                pSignalSemaphoreInfos = signalInfos,
            };

            VkResult result = vkQueueSubmit2(Queue, 1, &submitInfo, VkFence.Null);
            if (result != VkResult.Success)
            {
                // interpolated only on failure: building it eagerly would
                // allocate on every frame's submit
                VulkanException.ThrowIfFailed(result, $"Failed to submit command buffer '{commandBuffer.Name}'");
            }

            if (swapchain != null && waitSemaphore.Handle != 0)
            {
                swapchain.ConsumeAcquireSemaphore();
            }

            // re-record waits this timeline value (covers the whole array,
            // riders included); the frame-slot throttle waits the value
            // Present() captures (which is at least this one)
            commandBufferImpl.LastSubmitTimelineValue = timelineValue;

            // the riders' completion is covered by the same timeline value;
            // PrepareCommandBuffer recycles them after waiting it
            commandBufferImpl.PendingLeadFlush = lead;
            commandBufferImpl.PendingTrailBarrier = trail;
        }
    }

    // ===== buffer write/read (blocking) =====

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;

        if (bufferImpl.IsHostVisible)
        {
            Buffer.MemoryCopy(data, (byte*)bufferImpl.MappedPointer + bufferOffset, size, size);
            return;
        }

        // device-local buffer: stage into the frame arena; the copy rides the
        // next queue submission ahead of anything recorded after this call
        // (one submission per frame instead of one per write)
        EnqueueBufferUpload(bufferImpl, bufferOffset, data, size);
    }

    protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size)
    {
        VulkanBuffer bufferImpl = (VulkanBuffer)buffer;

        if (bufferImpl.IsHostVisible)
        {
            Buffer.MemoryCopy((byte*)bufferImpl.MappedPointer + bufferOffset, dest, size, size);
            return;
        }

        StagingBuffer staging = StagingBuffer.Create(this, size, writable: false);

        VkCommandBuffer commandBuffer;
        VkFence fence;
        lock (_queueLock)
        {
            commandBuffer = BeginOneShotLocked();
            // any deferred writes to this buffer must land before the readback
            RecordDeferredWorkLocked(commandBuffer);
            Tracker.TransitionBuffer(commandBuffer, bufferImpl, VulkanResourceState.CopySrc);
            VkBufferCopy copy = new()
            {
                srcOffset = bufferOffset,
                dstOffset = 0,
                size = size,
            };
            vkCmdCopyBuffer(commandBuffer, bufferImpl.Native, staging.Buffer, 1, &copy);
            Tracker.MakeWritesVisible(commandBuffer, VulkanResourceState.CopySrc);
            fence = SubmitOneShotBlockingLocked(commandBuffer);
        }

        VkResult readResult = vkWaitForFences(NativeDevice, 1, &fence, true, ulong.MaxValue);
        VulkanException.ThrowIfFailed(readResult, "Failed to wait for readback completion");
        RecycleOneShot(commandBuffer, fence);

        Buffer.MemoryCopy(staging.Mapped, dest, size, size);
        staging.Destroy(this);
    }

    // ===== texture write/read (blocking, repacks 256-byte row alignment) =====

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel)
    {
        VulkanTexture textureImpl = (VulkanTexture)texture;
        uint texelSize = VulkanUtility.PixelFormatSize(textureImpl.PixelFormat);
        uint width = textureImpl.MipWidth(mipLevel);
        uint height = textureImpl.MipHeight(mipLevel);
        uint depthOrLayers = textureImpl.Is3D ? textureImpl.MipDepth(mipLevel) : textureImpl.ArrayLayers;
        VkImageAspectFlags aspect = VulkanUtility.AspectToVulkan(TextureAspect.All, textureImpl.VkFormat);

        StagingBuffer staging;
        // 0 keeps the copy tight (compressed path expects block-aligned data)
        ulong alignedRow = 0;
        if (texelSize > 0)
        {
            ulong tightRow = (ulong)width * texelSize;
            alignedRow = VulkanUtility.AlignUp(tightRow, VulkanUtility.TexelRowAlignment);
            ulong alignedLayerStride = alignedRow * height;
            ulong stagingSize = alignedLayerStride * depthOrLayers;

            staging = StagingBuffer.Create(this, stagingSize, writable: true);

            if (alignedRow == tightRow)
            {
                // rows are already 256-aligned: single copy
                ulong expected = tightRow * height * depthOrLayers;
                Buffer.MemoryCopy(data, staging.Mapped, stagingSize, Math.Min((ulong)dataSize, expected));
            }
            else
            {
                // repack tight rows into the aligned staging layout
                ulong tightLayerStride = tightRow * height;
                byte* src = data;
                byte* dst = (byte*)staging.Mapped;
                for (uint layer = 0; layer < depthOrLayers; layer++)
                {
                    for (uint row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(
                            src + (ulong)layer * tightLayerStride + (ulong)row * tightRow,
                            dst + (ulong)layer * alignedLayerStride + (ulong)row * alignedRow,
                            tightRow,
                            tightRow);
                    }
                }
            }
        }
        else
        {
            // compressed formats: expect block-aligned source data (queue convention)
            staging = StagingBuffer.Create(this, dataSize, writable: true);
            Buffer.MemoryCopy(data, staging.Mapped, dataSize, dataSize);
        }

        // asynchronous like buffer uploads: texture streaming runs on worker
        // threads and must never block the render thread or the queue
        lock (_queueLock)
        {
            VkCommandBuffer commandBuffer = BeginOneShotLocked();
            Tracker.TransitionTexture(commandBuffer, textureImpl, VulkanResourceState.CopyDst);
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(
                mipLevel, width, height, depthOrLayers, aspect, 0,
                textureImpl.Is3D, alignedRow, texelSize);
            vkCmdCopyBufferToImage(commandBuffer, staging.Buffer, textureImpl.Image, Tracker.LayoutForTexture(textureImpl, VulkanResourceState.CopyDst), 1, &copy);
            Tracker.RestoreImageToIdle(commandBuffer, textureImpl);
            SubmitOneShotAsyncLocked(commandBuffer, staging);
        }
    }

    protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel = 0)
    {
        VulkanTexture textureImpl = (VulkanTexture)texture;
        uint texelSize = VulkanUtility.PixelFormatSize(textureImpl.PixelFormat);
        uint width = textureImpl.MipWidth(mipLevel);
        uint height = textureImpl.MipHeight(mipLevel);
        uint depthOrLayers = textureImpl.Is3D ? textureImpl.MipDepth(mipLevel) : textureImpl.ArrayLayers;
        VkImageAspectFlags aspect = VulkanUtility.AspectToVulkan(TextureAspect.All, textureImpl.VkFormat);

        StagingBuffer staging;
        ulong alignedRow = 0;
        if (texelSize > 0)
        {
            ulong tightRow = (ulong)width * texelSize;
            alignedRow = VulkanUtility.AlignUp(tightRow, VulkanUtility.TexelRowAlignment);
            staging = StagingBuffer.Create(this, alignedRow * height * depthOrLayers, writable: false);
        }
        else
        {
            staging = StagingBuffer.Create(this, dataSize, writable: false);
        }

        VkCommandBuffer commandBuffer;
        VkFence fence;
        lock (_queueLock)
        {
            commandBuffer = BeginOneShotLocked();
            Tracker.TransitionTexture(commandBuffer, textureImpl, VulkanResourceState.CopySrc);
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(
                mipLevel, width, height, depthOrLayers, aspect, 0,
                textureImpl.Is3D, alignedRow, texelSize);
            vkCmdCopyImageToBuffer(commandBuffer, textureImpl.Image, Tracker.LayoutForTexture(textureImpl, VulkanResourceState.CopySrc), staging.Buffer, 1, &copy);
            Tracker.RestoreImageToIdle(commandBuffer, textureImpl);
            fence = SubmitOneShotBlockingLocked(commandBuffer);
        }

        VkResult readResult = vkWaitForFences(NativeDevice, 1, &fence, true, ulong.MaxValue);
        VulkanException.ThrowIfFailed(readResult, "Failed to wait for readback completion");
        RecycleOneShot(commandBuffer, fence);

        if (texelSize > 0)
        {
            ulong tightRow = (ulong)width * texelSize;
            if (alignedRow == tightRow)
            {
                Buffer.MemoryCopy(staging.Mapped, dest, dataSize, dataSize);
            }
            else
            {
                // unpack aligned rows back into the tight destination layout
                ulong tightLayerStride = tightRow * height;
                ulong alignedLayerStride = alignedRow * height;
                byte* srcLayer = (byte*)staging.Mapped;
                byte* dstLayer = dest;
                for (uint layer = 0; layer < depthOrLayers; layer++)
                {
                    for (uint row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(
                            srcLayer + (ulong)layer * alignedLayerStride + (ulong)row * alignedRow,
                            dstLayer + (ulong)layer * tightLayerStride + (ulong)row * tightRow,
                            tightRow,
                            tightRow);
                    }
                }
            }
        }
        else
        {
            Buffer.MemoryCopy(staging.Mapped, dest, dataSize, dataSize);
        }

        staging.Destroy(this);
    }

    // ===== asynchronous texture readback =====

    protected override unsafe void BeginReadTextureCore(
        GPUTexture texture,
        byte* dest,
        uint dataSize,
        GPUTextureReadbackRequest request,
        uint mipLevel = 0)
    {
        VulkanTexture textureImpl = (VulkanTexture)texture;
        VkImageAspectFlags aspect = VulkanUtility.AspectToVulkan(TextureAspect.All, textureImpl.VkFormat);
        uint width = textureImpl.MipWidth(mipLevel);
        uint height = textureImpl.MipHeight(mipLevel);
        uint depthOrLayers = textureImpl.Is3D ? textureImpl.MipDepth(mipLevel) : textureImpl.ArrayLayers;

        StagingBuffer staging;
        ulong tightRow = 0;
        ulong alignedRow = 0;
        uint texelSize = VulkanUtility.PixelFormatSize(textureImpl.PixelFormat);
        if (texelSize > 0)
        {
            tightRow = (ulong)width * texelSize;
            alignedRow = VulkanUtility.AlignUp(tightRow, VulkanUtility.TexelRowAlignment);
            staging = StagingBuffer.Create(this, alignedRow * height * depthOrLayers, writable: false);
        }
        else
        {
            staging = StagingBuffer.Create(this, dataSize, writable: false);
        }

        VkCommandBuffer commandBuffer;
        VkFence fence = CreateFenceNative(signaled: false);
        lock (_queueLock)
        {
            commandBuffer = BeginOneShotLocked();
            Tracker.TransitionTexture(commandBuffer, textureImpl, VulkanResourceState.CopySrc);
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(
                mipLevel, width, height, depthOrLayers, aspect, 0,
                textureImpl.Is3D, alignedRow, texelSize);
            vkCmdCopyImageToBuffer(commandBuffer, textureImpl.Image, Tracker.LayoutForTexture(textureImpl, VulkanResourceState.CopySrc), staging.Buffer, 1, &copy);
            Tracker.RestoreImageToIdle(commandBuffer, textureImpl);
            SubmitOneShotLocked(commandBuffer, fence);
        }

        _readbackArrivals.Enqueue(new PendingReadback
        {
            Fence = fence,
            CommandBuffer = commandBuffer,
            Staging = staging,
            Destination = dest,
            DataSize = dataSize,
            Height = height,
            Layers = depthOrLayers,
            TightRow = tightRow,
            AlignedRow = alignedRow,
            Request = request,
        });
    }

    private unsafe void ProcessReadbacks()
    {
        // arrivals (worker threads) are lock-free; retiring runs on the frame
        // pump only, and its slow work (row repack, staging destroy) holds no lock
        while (_readbackArrivals.TryDequeue(out PendingReadback arrival))
        {
            _activeReadbacks.Add(arrival);
        }

        for (int i = _activeReadbacks.Count - 1; i >= 0; i--)
        {
            PendingReadback readback = _activeReadbacks[i];
            VkResult status = vkGetFenceStatus(NativeDevice, readback.Fence);
            if (status == VkResult.NotReady)
            {
                continue;
            }
            _activeReadbacks.RemoveAt(i);

            if (status == VkResult.Success)
            {
                try
                {
                    CopyReadbackRows(readback);
                    readback.Request.Complete();
                }
                catch (Exception e)
                {
                    readback.Request.Fail(e);
                }
            }
            else
            {
                readback.Request.Fail(new GraphicsException($"Texture readback failed (VkResult: {status})"));
            }

            readback.Staging.Destroy(this);
            vkDestroyFence(NativeDevice, readback.Fence, null);
            RecycleOneShot(readback.CommandBuffer, default);
        }
    }

    private unsafe void CopyReadbackRows(in PendingReadback readback)
    {
        if (readback.AlignedRow == readback.TightRow || readback.TightRow == 0)
        {
            Buffer.MemoryCopy(readback.Staging.Mapped, readback.Destination, readback.DataSize, readback.DataSize);
            return;
        }

        // unpack aligned rows into the tight destination layout
        ulong alignedLayerStride = readback.AlignedRow * readback.Height;
        ulong tightLayerStride = readback.TightRow * readback.Height;
        byte* srcLayer = (byte*)readback.Staging.Mapped;
        byte* dstLayer = readback.Destination;
        for (uint layer = 0; layer < readback.Layers; layer++)
        {
            for (uint row = 0; row < readback.Height; row++)
            {
                Buffer.MemoryCopy(
                    srcLayer + (ulong)layer * alignedLayerStride + (ulong)row * readback.AlignedRow,
                    dstLayer + (ulong)layer * tightLayerStride + (ulong)row * readback.TightRow,
                    readback.TightRow,
                    readback.TightRow);
            }
        }
    }

    // ===== frame lifecycle =====

    protected override void OnEndFrameCore()
    {
        ProcessReadbacks();
        ProcessUploads();
        ProcessOneShots();
        RotateUploadArena();
        ProcessRetiredDescriptorSets();
        ProcessDisposals();
        PruneLiveObjects();
        FrameCounter++;
    }

    private long _pruneCounter;

    /// <summary>Drops dead weak references from the teardown registry so the list
    /// does not grow with every resource ever created.</summary>
    private void PruneLiveObjects()
    {
        if ((++_pruneCounter & 255) != 0)
        {
            return;
        }
        lock (_liveObjectsLock)
        {
            for (int i = _liveObjects.Count - 1; i >= 0; i--)
            {
                if (!_liveObjects[i].IsAlive)
                {
                    _liveObjects.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Retires completed asynchronous uploads: destroys their staging
    /// buffers and returns the command buffers to the upload pool. Non-blocking;
    /// must run on the main thread (OnEndFrameCore).</summary>
    private unsafe void ProcessUploads()
    {
        // swap the live queue out under the submission lock, then drain and
        // retire outside it (the pump is the only consumer)
        Queue<PendingUpload> arrivals;
        lock (_queueLock)
        {
            if (_uploadsLive.Count == 0)
            {
                return;
            }
            arrivals = _uploadsLive;
            _uploadsLive = _uploadsDrain ?? new Queue<PendingUpload>(16);
            _uploadsDrain = null;
        }
        while (arrivals.TryDequeue(out PendingUpload arrival))
        {
            _activeUploads.Add(arrival);
        }
        _uploadsDrain = arrivals;

        for (int i = _activeUploads.Count - 1; i >= 0; i--)
        {
            PendingUpload upload = _activeUploads[i];
            if (vkGetFenceStatus(NativeDevice, upload.Fence) != VkResult.Success)
            {
                continue;
            }
            _activeUploads.RemoveAt(i);

            upload.Staging.Destroy(this);
            lock (_oneShotPoolLock)
            {
                _oneShotFree.Push(upload.CommandBuffer);
            }
            _ = vkResetFences(NativeDevice, 1, &upload.Fence);
            lock (_asyncFenceLock)
            {
                _asyncFencePool.Push(upload.Fence);
            }
        }
    }

    protected override void DisposeCore()
    {
        while (_readbackArrivals.TryDequeue(out PendingReadback arrival))
        {
            _activeReadbacks.Add(arrival);
        }
        foreach (PendingReadback readback in _activeReadbacks)
        {
            readback.Request.Fail(new ObjectDisposedException(nameof(VulkanDevice), "GPU device was disposed before texture readback completed."));
            readback.Staging.Destroy(this);
            vkDestroyFence(NativeDevice, readback.Fence, null);
        }
        _activeReadbacks.Clear();

        // force-destroy wrappers the engine still holds (native handles are
        // still valid here); in-flight work is fenced by the wait idle below
        vkDeviceWaitIdle(NativeDevice);
        // the wait idle above completes every pending upload; release their
        // resources while the pools still exist
        while (_uploadsLive.Count > 0 || _uploadsDrain is { Count: > 0 })
        {
            PendingUpload upload = _uploadsLive.Count > 0
                ? _uploadsLive.Dequeue()
                : _uploadsDrain!.Dequeue();
            upload.Staging.Destroy(this);
            vkDestroyFence(NativeDevice, upload.Fence, null);
        }
        _activeUploads.Clear();

        // release upload arena chunks; queued copies/inits never got flushed
        // and only reference the chunks destroyed here
        lock (_arenaLock)
        {
            for (int slot = 0; slot < ArenaSlotCount; slot++)
            {
                foreach (ArenaChunk chunk in ArenaSlot(slot))
                {
                    vmaDestroyBuffer(Allocator, chunk.Buffer, chunk.Allocation);
                }
                ArenaSlot(slot).Clear();
                if (_retiredArenaChunks[slot] is { } retired)
                {
                    foreach (ArenaChunk chunk in retired)
                    {
                        vmaDestroyBuffer(Allocator, chunk.Buffer, chunk.Allocation);
                    }
                    retired.Clear();
                }
            }
            foreach (ArenaChunk chunk in _freeArenaChunks)
            {
                vmaDestroyBuffer(Allocator, chunk.Buffer, chunk.Allocation);
            }
            _freeArenaChunks.Clear();
            lock (_copiesLock)
            {
                _copiesLive.Clear();
                _copiesDrain?.Clear();
            }
            _drainedCopies.Clear();
            _transitionedBuffers.Clear();
            _pendingTextureInits.Clear();
        }

        DestroyTrackedObjects();

        // the engine may not dispose every wrapper deterministically; run the
        // finalizers NOW so their deferred native destroys queue while the
        // device can still execute them
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // destroy the default bind groups BEFORE flushing the disposal queue so
        // their native objects go through the same deferred path
        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupStorageBufferWithCounter.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();
        BindGroupTexture3DRead.Destroy();

        // dispose everything the deferred queue still holds
        while (_disposalArrivals.TryDequeue(out PendingDisposal arrival))
        {
            _agingDisposals.Add(arrival);
        }
        foreach (PendingDisposal disposal in _agingDisposals)
        {
            DestroyDisposal(disposal);
        }
        _agingDisposals.Clear();
        // retiring sets is pointless at teardown — pool destruction frees them
        _retiredSetArrivals.Clear();
        _agingRetiredSets.Clear();

        if (_commandPool.Handle != 0)
        {
            vkDestroyCommandPool(NativeDevice, _commandPool, null);
            _commandPool = default;
        }
        if (_uploadPool.Handle != 0)
        {
            // frees all one-shot command buffers implicitly
            vkDestroyCommandPool(NativeDevice, _uploadPool, null);
            _uploadPool = default;
        }
        while (true)
        {
            VkFence blockingFence;
            lock (_blockingFenceLock)
            {
                if (_blockingFencePool.Count == 0)
                {
                    break;
                }
                blockingFence = _blockingFencePool.Pop();
            }
            vkDestroyFence(NativeDevice, blockingFence, null);
        }
        while (true)
        {
            VkFence asyncFence;
            lock (_asyncFenceLock)
            {
                if (_asyncFencePool.Count == 0)
                {
                    break;
                }
                asyncFence = _asyncFencePool.Pop();
            }
            vkDestroyFence(NativeDevice, asyncFence, null);
        }

        if (Allocator.Handle != 0)
        {
            vmaDestroyAllocator(Allocator);
            Allocator = default;
        }

        if (_timelineSemaphore.Handle != 0)
        {
            vkDestroySemaphore(NativeDevice, _timelineSemaphore, null);
            _timelineSemaphore = default;
        }

        if (NativeDevice.Handle != 0)
        {
            vkDestroyDevice(NativeDevice, null);
            NativeDevice = default;
        }
        if (_messenger.Handle != 0)
        {
            vkDestroyDebugUtilsMessengerEXT(Instance, _messenger, null);
            _messenger = default;
        }
        if (Instance.Handle != 0)
        {
            vkDestroyInstance(Instance, null);
            Instance = default;
        }
    }

    // ===== staging buffer =====

    private struct StagingBuffer
    {
        public VkBuffer Buffer;
        public VmaAllocation Allocation;
        public void* Mapped;
        public ulong Size;

        public static StagingBuffer Create(VulkanDevice device, ulong size, bool writable)
        {
            VkBufferCreateInfo bufferInfo = new()
            {
                size = size,
                usage = writable ? VkBufferUsageFlags.TransferSrc : VkBufferUsageFlags.TransferDst,
                sharingMode = VkSharingMode.Exclusive,
            };

            VmaAllocationCreateInfo allocInfo = new()
            {
                usage = VmaMemoryUsage.Auto,
                requiredFlags = VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
            };

            StagingBuffer staging = default;
            staging.Size = size;
            VkResult result = vmaCreateBuffer(device.Allocator, &bufferInfo, &allocInfo, &staging.Buffer, &staging.Allocation, null);
            VulkanException.ThrowIfFailed(result, "Failed to create staging buffer");

            result = vmaMapMemory(device.Allocator, staging.Allocation, &staging.Mapped);
            VulkanException.ThrowIfFailed(result, "Failed to map staging buffer");
            return staging;
        }

        public void Destroy(VulkanDevice device)
        {
            if (Mapped != null)
            {
                vmaUnmapMemory(device.Allocator, Allocation);
                Mapped = null;
            }
            if (Buffer.Handle != 0)
            {
                vmaDestroyBuffer(device.Allocator, Buffer, Allocation);
                Buffer = default;
                Allocation = default;
            }
        }
    }
}
