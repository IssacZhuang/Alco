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
    public int QueueFamilyIndex { get; private set; }
    public VmaAllocator Allocator { get; private set; }

    public VulkanResourceTracker Tracker { get; } = new();

    public VulkanDeviceFeatures Features { get; private set; } = new();

    public override GraphicsBackend Backend => GraphicsBackend.Vulkan;

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
    private readonly List<VkCommandBuffer> _oneShotFree = new();

    // vkQueueSubmit requires external synchronization, and uploads can run on
    // threadpool threads concurrently with the render thread
    private readonly object _queueLock = new();
    private readonly List<VkFence> _blockingFencePool = new();

    // ===== asynchronous queue uploads (wgpu queue-write semantics: submit
    // without waiting, retire the staging buffer once the fence signals) =====
    private struct PendingUpload
    {
        public VkFence Fence;
        public VkCommandBuffer CommandBuffer;
        public StagingBuffer Staging;
    }
    private readonly List<PendingUpload> _pendingUploads = new();
    // kept separate from _queueLock so ProcessUploads never nests the queue
    // lock; submit paths take _queueLock first, then this one
    private readonly object _pendingUploadsLock = new();

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
    private readonly List<PendingBufferCopy> _pendingCopies = new();
    private readonly object _pendingCopiesLock = new();
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
        lock (_pendingCopiesLock)
        {
            List<ArenaChunk> chunks = ArenaSlot((int)(_uploadFrame % ArenaSlotCount));
            int index = -1;
            ArenaChunk chunk = default;
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
                ulong chunkSize = Math.Max(8u * 1024 * 1024, VulkanUtility.AlignUp((ulong)size, 65536));
                chunk = CreateArenaChunk(chunkSize);
                chunks.Add(chunk);
                index = chunks.Count - 1;
            }

            Buffer.MemoryCopy(data, (byte*)chunk.Mapped + chunk.Used, size, size);
            _pendingCopies.Add(new PendingBufferCopy
            {
                Destination = buffer,
                DestinationOffset = bufferOffset,
                Source = chunk.Buffer,
                SourceOffset = chunk.Used,
                Size = size,
            });
            chunk.Used += size;
            chunks[index] = chunk;
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

    /// <summary>Records every pending buffer copy into <paramref name="commandBuffer"/>:
    /// one CopyDst transition per unique destination, all copy commands, then one
    /// visibility barrier.</summary>
    private void RecordPendingCopiesLocked(VkCommandBuffer commandBuffer)
    {
        lock (_pendingCopiesLock)
        {
            if (_pendingCopies.Count == 0)
            {
                return;
            }

            HashSet<VulkanBuffer> transitioned = new();
            foreach (PendingBufferCopy copy in _pendingCopies)
            {
                if (transitioned.Add(copy.Destination))
                {
                    Tracker.TransitionBuffer(commandBuffer, copy.Destination, VulkanResourceState.CopyDst);
                }
            }

            foreach (PendingBufferCopy copy in _pendingCopies)
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

            Tracker.MakeWritesVisible(commandBuffer, VulkanResourceState.CopyDst);
            _pendingCopies.Clear();
        }
    }

    /// <summary>Submits the pending buffer copies as one one-shot command buffer so
    /// they execute before whatever is submitted next (same queue, in-order).
    /// The command buffer is retired through the pending-upload fence list — it
    /// must not return to the free pool while the GPU may still be executing it.
    /// Caller must hold <see cref="_queueLock"/>.</summary>
    private void FlushPendingCopiesLocked()
    {
        lock (_pendingCopiesLock)
        {
            if (_pendingCopies.Count == 0)
            {
                return;
            }
        }

        VkCommandBuffer commandBuffer = BeginOneShotLocked();
        RecordPendingCopiesLocked(commandBuffer);
        SubmitOneShotAsyncLocked(commandBuffer, default);
    }

    /// <summary>Releases the arena chunks of the slot the next frame will write
    /// into (their last GPU read finished at least two frames ago).</summary>
    private unsafe void RotateUploadArena()
    {
        lock (_pendingCopiesLock)
        {
            int slot = (int)((_uploadFrame + 1) % ArenaSlotCount);
            List<ArenaChunk> chunks = ArenaSlot(slot);
            foreach (ArenaChunk chunk in chunks)
            {
                vmaDestroyBuffer(Allocator, chunk.Buffer, chunk.Allocation);
            }
            chunks.Clear();
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
    private readonly List<PendingReadback> _pendingReadbacks = new();

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
    private readonly List<PendingDisposal> _disposals = new();

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

        // prefer a discrete GPU, then the first device
        PhysicalDevice = devices[0];
        VkPhysicalDeviceProperties bestProperties = default;
        vkGetPhysicalDeviceProperties(PhysicalDevice, &bestProperties);
        for (uint i = 1; i < deviceCount; i++)
        {
            VkPhysicalDeviceProperties properties = default;
            vkGetPhysicalDeviceProperties(devices[i], &properties);
            bool candidateDiscrete = properties.deviceType == VkPhysicalDeviceType.DiscreteGpu;
            bool bestDiscrete = bestProperties.deviceType == VkPhysicalDeviceType.DiscreteGpu;
            if (candidateDiscrete && !bestDiscrete)
            {
                PhysicalDevice = devices[i];
                bestProperties = properties;
            }
        }
    }

    private void CreateLogicalDevice()
    {
        // features (query with the 1.3 chain, then enable what we need);
        // must use new() — 'default' leaves the internal sType at 0
        VkPhysicalDeviceVulkan13Features vulkan13Features = new()
        {
            synchronization2 = VkBool32.True,
            dynamicRendering = VkBool32.True,
        };
        VkPhysicalDeviceFeatures2 features2 = new()
        {
            pNext = &vulkan13Features,
        };
        vkGetPhysicalDeviceFeatures2(PhysicalDevice, &features2);

        VkPhysicalDeviceFeatures queried = features2.features;
        Features = new VulkanDeviceFeatures
        {
            SamplerAnisotropy = queried.samplerAnisotropy == VkBool32.True,
            DepthBounds = queried.depthBounds == VkBool32.True,
            TextureCompressionBC = queried.textureCompressionBC == VkBool32.True,
        };

        _supportedFeatures = GPUFeatures.None;
        if (Features.TextureCompressionBC)
        {
            _supportedFeatures |= GPUFeatures.TextureCompressionBC;
        }

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

    /// <summary>Allocates a primary command buffer from the frame pool
    /// (main render thread only).</summary>
    public VkCommandBuffer AllocateCommandBuffer()
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

    /// <summary>Allocates a secondary command buffer from the frame pool (used by
    /// cached render bundle replays; pool allocation requires the queue lock
    /// because bundles may be recorded on worker threads).</summary>
    public VkCommandBuffer AllocateSecondaryCommandBuffer()
    {
        lock (_queueLock)
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

    /// <summary>Waits for the command buffer's previous submission, resets the fence
    /// and the command buffer so recording can start again.</summary>
    public void PrepareCommandBuffer(VulkanCommandBuffer commandBuffer)
    {
        VkFence fence = commandBuffer.InFlightFence;
        VkResult result = vkWaitForFences(NativeDevice, 1, &fence, true, ulong.MaxValue);
        VulkanException.ThrowIfFailed(result, "Failed to wait for command buffer fence");
        _ = vkResetFences(NativeDevice, 1, &fence);
        _ = vkResetCommandBuffer(commandBuffer.NativeCommandBuffer, VkCommandBufferResetFlags.None);
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
        if (_oneShotFree.Count > 0)
        {
            commandBuffer = _oneShotFree[^1];
            _oneShotFree.RemoveAt(_oneShotFree.Count - 1);
        }
        else
        {
            commandBuffer = AllocateUploadCommandBuffer();
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
    }

    /// <summary>Ends and submits a one-shot buffer with a pooled fence. The caller
    /// must hold <see cref="_queueLock"/>; returns the fence to wait on.</summary>
    private VkFence SubmitOneShotBlockingLocked(VkCommandBuffer commandBuffer)
    {
        vkEndCommandBuffer(commandBuffer).ThrowOnFailure();

        VkFence fence;
        if (_blockingFencePool.Count > 0)
        {
            fence = _blockingFencePool[^1];
            _blockingFencePool.RemoveAt(_blockingFencePool.Count - 1);
        }
        else
        {
            fence = CreateFenceNative(signaled: false);
        }

        VkCommandBuffer native = commandBuffer;
        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
        };
        VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
        VulkanException.ThrowIfFailed(result, "Failed to submit one-shot command buffer");
        return fence;
    }

    /// <summary>Ends and submits a one-shot buffer WITHOUT waiting for it. The
    /// fence and staging buffer are retired later by <see cref="ProcessUploads"/>.
    /// The caller must hold <see cref="_queueLock"/>.</summary>
    private void SubmitOneShotAsyncLocked(VkCommandBuffer commandBuffer, StagingBuffer staging)
    {
        vkEndCommandBuffer(commandBuffer).ThrowOnFailure();

        VkFence fence = CreateFenceNative(signaled: false);
        VkCommandBuffer native = commandBuffer;
        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
        };
        VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
        VulkanException.ThrowIfFailed(result, "Failed to submit one-shot command buffer");

        lock (_pendingUploadsLock)
        {
            _pendingUploads.Add(new PendingUpload
            {
                Fence = fence,
                CommandBuffer = commandBuffer,
                Staging = staging,
            });
        }
    }

    /// <summary>Moves a freshly created image from UNDEFINED into the tracker's
    /// GENERAL idle state. Binding a never-used image inside a pass cannot record
    /// a barrier, so the transition happens once here at creation time.</summary>
    public void InitializeTextureLayout(VulkanTexture texture)
    {
        if (Tracker.GetTextureState(texture) != VulkanResourceState.Undefined)
        {
            return;
        }

        // asynchronous like every queue upload: in-order queue execution makes
        // the transition land before any later submission that binds the texture
        lock (_queueLock)
        {
            VkCommandBuffer commandBuffer = BeginOneShotLocked();
            Tracker.TransitionTexture(commandBuffer, texture, VulkanResourceState.Idle);
            SubmitOneShotAsyncLocked(commandBuffer, default);
        }
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

    /// <summary>Returns a finished one-shot buffer and its fence to the pools.
    /// A null fence (owned externally, e.g. async readback) is skipped.</summary>
    private void RecycleOneShot(VkCommandBuffer commandBuffer, VkFence fence)
    {
        lock (_queueLock)
        {
            if (fence.Handle != 0)
            {
                _ = vkResetFences(NativeDevice, 1, &fence);
                _blockingFencePool.Add(fence);
            }
            _oneShotFree.Add(commandBuffer);
        }
    }



    /// <summary>Submits the present-layout transition for a swapchain image. The
    /// submission is ordered before present through the swapchain's semaphores.</summary>
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
            VkFence fence = swapchain.CurrentFrameFence;

            VkCommandBuffer native = commandBuffer;
            VkSemaphore nativeWait = waitSemaphore;
            VkSemaphore nativeSignal = signalSemaphore;
            VkPipelineStageFlags waitStage = VkPipelineStageFlags.ColorAttachmentOutput;

            VkSubmitInfo submitInfo = new()
            {
                commandBufferCount = 1,
                pCommandBuffers = &native,
                waitSemaphoreCount = waitSemaphore.Handle != 0 ? 1u : 0u,
                pWaitSemaphores = &nativeWait,
                pWaitDstStageMask = &waitStage,
                signalSemaphoreCount = 1,
                pSignalSemaphores = &nativeSignal,
            };

            // the slot fence is waited+reset by BeginFrame each cycle; a stray
            // signaled state (skipped acquire) would fail the submit
            _ = vkResetFences(NativeDevice, 1, &fence);
            VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, fence);
            VulkanException.ThrowIfFailed(result, "Failed to submit present barrier");
            _oneShotFree.Add(commandBuffer);
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
        _disposals.Add(new PendingDisposal
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

    /// <summary>Deferred free of a secondary command buffer (must not free while
    /// the GPU may still execute it; the disposal delay covers frames in flight).</summary>
    public void QueueSecondaryCommandBufferFree(VkCommandBuffer commandBuffer)
        => QueueDisposal(DisposalKind.SecondaryCommandBuffer, (ulong)commandBuffer.Handle);

    /// <summary>Monotonic frame counter (main thread, once per OnEndFrame) used by
    /// render bundles to key cached secondary command buffers per in-flight frame.</summary>
    internal static long FrameCounter;

    private void ProcessDisposals()
    {
        for (int i = _disposals.Count - 1; i >= 0; i--)
        {
            PendingDisposal disposal = _disposals[i];
            if (disposal.FramesLeft > 0)
            {
                disposal.FramesLeft--;
                _disposals[i] = disposal;
                continue;
            }

            DestroyDisposal(disposal);
            _disposals.RemoveAt(i);
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
            case DisposalKind.SecondaryCommandBuffer:
            {
                // the pool is shared with worker-thread one-shot allocation, so
                // free under the queue lock
                VkCommandBuffer commandBuffer = new((nint)disposal.Handle);
                lock (_queueLock)
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

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        VulkanCommandBuffer commandBufferImpl = (VulkanCommandBuffer)commandBuffer;
        VulkanSwapchain? swapchain = _activeSwapchain;

        VkSemaphore waitSemaphore = default;
        VkSemaphore signalSemaphore = default;
        if (swapchain != null)
        {
            waitSemaphore = swapchain.PendingAcquireSemaphore;
            signalSemaphore = swapchain.TakeSubmitSemaphore();
        }

        VkCommandBuffer native = commandBufferImpl.NativeCommandBuffer;
        VkSemaphore nativeWait = waitSemaphore;
        VkSemaphore nativeSignal = signalSemaphore;
        VkPipelineStageFlags waitStage = VkPipelineStageFlags.ColorAttachmentOutput;

        VkSubmitInfo submitInfo = new()
        {
            commandBufferCount = 1,
            pCommandBuffers = &native,
            waitSemaphoreCount = waitSemaphore.Handle != 0 ? 1u : 0u,
            pWaitSemaphores = &nativeWait,
            pWaitDstStageMask = &waitStage,
            signalSemaphoreCount = signalSemaphore.Handle != 0 ? 1u : 0u,
            pSignalSemaphores = &nativeSignal,
        };

        lock (_queueLock)
        {
            FlushPendingCopiesLocked();
            VkResult result = vkQueueSubmit(Queue, 1, &submitInfo, commandBufferImpl.InFlightFence);
            VulkanException.ThrowIfFailed(result, $"Failed to submit command buffer '{commandBuffer.Name}'");
        }

        if (swapchain != null && waitSemaphore.Handle != 0)
        {
            swapchain.ConsumeAcquireSemaphore();
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
            RecordPendingCopiesLocked(commandBuffer);
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
        ulong alignedRow;
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
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(mipLevel, width, height, depthOrLayers, aspect, 0);
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
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(mipLevel, width, height, depthOrLayers, aspect, 0);
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
            VkBufferImageCopy copy = VulkanUtility.GetBufferImageCopy(mipLevel, width, height, depthOrLayers, aspect, 0);
            vkCmdCopyImageToBuffer(commandBuffer, textureImpl.Image, Tracker.LayoutForTexture(textureImpl, VulkanResourceState.CopySrc), staging.Buffer, 1, &copy);
            Tracker.RestoreImageToIdle(commandBuffer, textureImpl);
            SubmitOneShotLocked(commandBuffer, fence);
        }

        _pendingReadbacks.Add(new PendingReadback
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
        for (int i = _pendingReadbacks.Count - 1; i >= 0; i--)
        {
            PendingReadback readback = _pendingReadbacks[i];
            VkResult status = vkGetFenceStatus(NativeDevice, readback.Fence);
            if (status == VkResult.NotReady)
            {
                continue;
            }
            if (status != VkResult.Success)
            {
                readback.Request.Fail(new GraphicsException($"Texture readback failed (VkResult: {status})"));
                readback.Staging.Destroy(this);
                vkDestroyFence(NativeDevice, readback.Fence, null);
                RecycleOneShot(readback.CommandBuffer, default);
                _pendingReadbacks.RemoveAt(i);
                continue;
            }

            try
            {
                CopyReadbackRows(readback);
                readback.Request.Complete();
            }
            catch (Exception e)
            {
                readback.Request.Fail(e);
            }

            readback.Staging.Destroy(this);
            vkDestroyFence(NativeDevice, readback.Fence, null);
            RecycleOneShot(readback.CommandBuffer, default);
            _pendingReadbacks.RemoveAt(i);
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
        RotateUploadArena();
        ProcessDisposals();
        FrameCounter++;
    }

    /// <summary>Retires completed asynchronous uploads: destroys their staging
    /// buffers and returns the command buffers to the upload pool. Non-blocking;
    /// must run on the main thread (OnEndFrameCore).</summary>
    private unsafe void ProcessUploads()
    {
        List<VkCommandBuffer> retired = null;
        lock (_pendingUploadsLock)
        {
            for (int i = _pendingUploads.Count - 1; i >= 0; i--)
            {
                PendingUpload upload = _pendingUploads[i];
                if (vkGetFenceStatus(NativeDevice, upload.Fence) != VkResult.Success)
                {
                    continue;
                }
                upload.Staging.Destroy(this);
                vkDestroyFence(NativeDevice, upload.Fence, null);
                (retired ??= new List<VkCommandBuffer>()).Add(upload.CommandBuffer);
                _pendingUploads.RemoveAt(i);
            }
        }

        if (retired != null)
        {
            // released _pendingUploadsLock first: submit paths take
            // _queueLock then _pendingUploadsLock, so never nest the other way
            lock (_queueLock)
            {
                foreach (VkCommandBuffer commandBuffer in retired)
                {
                    _oneShotFree.Add(commandBuffer);
                }
            }
        }
    }

    protected override void DisposeCore()
    {
        foreach (PendingReadback readback in _pendingReadbacks)
        {
            readback.Request.Fail(new ObjectDisposedException(nameof(VulkanDevice), "GPU device was disposed before texture readback completed."));
            readback.Staging.Destroy(this);
            vkDestroyFence(NativeDevice, readback.Fence, null);
            RecycleOneShot(readback.CommandBuffer, default);
        }
        _pendingReadbacks.Clear();

        // force-destroy wrappers the engine still holds (native handles are
        // still valid here); in-flight work is fenced by the wait idle below
        vkDeviceWaitIdle(NativeDevice);
        // the wait idle above completes every pending upload; release their
        // resources while the pools still exist
        lock (_pendingUploadsLock)
        {
            foreach (PendingUpload upload in _pendingUploads)
            {
                upload.Staging.Destroy(this);
                vkDestroyFence(NativeDevice, upload.Fence, null);
                _oneShotFree.Add(upload.CommandBuffer);
            }
            _pendingUploads.Clear();
        }

        // release upload arena chunks
        lock (_pendingCopiesLock)
        {
            for (int slot = 0; slot < ArenaSlotCount; slot++)
            {
                foreach (ArenaChunk chunk in ArenaSlot(slot))
                {
                    vmaDestroyBuffer(Allocator, chunk.Buffer, chunk.Allocation);
                }
                ArenaSlot(slot).Clear();
            }
            _pendingCopies.Clear();
        }

        DestroyTrackedObjects();

        // the engine may not dispose every wrapper deterministically; run the
        // finalizers NOW so their deferred native destroys queue while the
        // device can still execute them
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // dispose everything the deferred queue still holds
        foreach (PendingDisposal disposal in _disposals)
        {
            DestroyDisposal(disposal);
        }
        _disposals.Clear();

        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupStorageBufferWithCounter.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();
        BindGroupTexture3DRead.Destroy();

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
        foreach (VkFence fence in _blockingFencePool)
        {
            vkDestroyFence(NativeDevice, fence, null);
        }
        _blockingFencePool.Clear();

        if (Allocator.Handle != 0)
        {
            vmaDestroyAllocator(Allocator);
            Allocator = default;
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
