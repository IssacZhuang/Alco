// Minimal Vulkan enumerations and bitmasks required by Alco.Graphics.

namespace Vulkan;

[Flags]
internal enum VkAccessFlags2 : ulong
{
    None = 0,
    IndirectCommandRead = 0x1UL,
    IndexRead = 0x2UL,
    UniformRead = 0x8UL,
    ColorAttachmentRead = 0x80UL,
    ColorAttachmentWrite = 0x100UL,
    DepthStencilAttachmentRead = 0x200UL,
    DepthStencilAttachmentWrite = 0x400UL,
    TransferRead = 0x800UL,
    TransferWrite = 0x1000UL,
    HostRead = 0x2000UL,
    MemoryRead = 0x8000UL,
    MemoryWrite = 0x10000UL,
    ShaderSampledRead = 0x100000000UL,
    ShaderStorageRead = 0x200000000UL,
    ShaderStorageWrite = 0x400000000UL,
    SamplerHeapReadEXT = 0x200000000000000UL,
    ResourceHeapReadEXT = 0x400000000000000UL,
}

[Flags]
internal enum VkAddressCommandFlagsKHR
{
    None = 0,
    FullyBound = 0x2,
    StorageBufferUsage = 0x4,
}

internal enum VkAttachmentLoadOp
{
    Load = 0,
    Clear = 1,
    DontCare = 2,
    None = 1000400000,
}

internal enum VkAttachmentStoreOp
{
    Store = 0,
    DontCare = 1,
    None = 1000301000,
}

internal enum VkBlendFactor
{
    Zero = 0,
    One = 1,
    SrcColor = 2,
    OneMinusSrcColor = 3,
    DstColor = 4,
    OneMinusDstColor = 5,
    SrcAlpha = 6,
    OneMinusSrcAlpha = 7,
    DstAlpha = 8,
    OneMinusDstAlpha = 9,
    ConstantColor = 10,
    OneMinusConstantColor = 11,
    ConstantAlpha = 12,
    OneMinusConstantAlpha = 13,
    SrcAlphaSaturate = 14,
    Src1Color = 15,
    OneMinusSrc1Color = 16,
    Src1Alpha = 17,
    OneMinusSrc1Alpha = 18,
}

internal enum VkBlendOp
{
    Add = 0,
    Subtract = 1,
    ReverseSubtract = 2,
    Min = 3,
    Max = 4,
}

internal enum VkBorderColor
{
    FloatTransparentBlack = 0,
}

[Flags]
internal enum VkBufferCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkBufferUsageFlags
{
    None = 0,
    TransferSrc = 0x1,
    TransferDst = 0x2,
    StorageBuffer = 0x20,
    IndexBuffer = 0x40,
    IndirectBuffer = 0x100,
    ShaderDeviceAddress = 0x20000,
    DescriptorHeapEXT = 0x10000000,
}

[Flags]
internal enum VkColorComponentFlags
{
    None = 0,
    R = 0x1,
    G = 0x2,
    B = 0x4,
    A = 0x8,
    All = 0xF,
}

internal enum VkColorSpaceKHR
{
    SrgbNonLinear = 0,
    ExtendedSrgbLinearEXT = 1000104002,
}

internal enum VkCommandBufferLevel
{
    Primary = 0,
    Secondary = 1,
}

[Flags]
internal enum VkCommandBufferResetFlags
{
    None = 0,
    ReleaseResources = 0x1,
}

[Flags]
internal enum VkCommandBufferUsageFlags
{
    None = 0,
    OneTimeSubmit = 0x1,
    RenderPassContinue = 0x2,
    SimultaneousUse = 0x4,
}

[Flags]
internal enum VkCommandPoolCreateFlags
{
    None = 0,
    Transient = 0x1,
    ResetCommandBuffer = 0x2,
    Protected = 0x4,
}

[Flags]
internal enum VkCommandPoolResetFlags
{
    None = 0,
    ReleaseResources = 0x1,
}

internal enum VkCompareOp
{
    Never = 0,
    Less = 1,
    Equal = 2,
    LessOrEqual = 3,
    Greater = 4,
    NotEqual = 5,
    GreaterOrEqual = 6,
    Always = 7,
}

internal enum VkComponentSwizzle
{
    Identity = 0,
}

[Flags]
internal enum VkCompositeAlphaFlagsKHR
{
    None = 0,
    Opaque = 0x1,
    PreMultiplied = 0x2,
    PostMultiplied = 0x4,
    Inherit = 0x8,
}

[Flags]
internal enum VkCullModeFlags
{
    None = 0,
    Front = 0x1,
    Back = 0x2,
    FrontAndBack = 0x3,
}

[Flags]
internal enum VkDebugUtilsMessageSeverityFlagsEXT
{
    None = 0,
    Warning = 0x100,
    Error = 0x1000,
}

[Flags]
internal enum VkDebugUtilsMessageTypeFlagsEXT
{
    None = 0,
    General = 0x1,
    Validation = 0x2,
    Performance = 0x4,
}

[Flags]
internal enum VkDebugUtilsMessengerCallbackDataFlagsEXT
{
    None = 0,
}

[Flags]
internal enum VkDebugUtilsMessengerCreateFlagsEXT
{
    None = 0,
}

[Flags]
internal enum VkDependencyFlags
{
    None = 0,
}

[Flags]
internal enum VkDescriptorBindingFlags
{
    None = 0,
    UpdateAfterBind = 0x1,
    UpdateUnusedWhilePending = 0x2,
    PartiallyBound = 0x4,
    VariableDescriptorCount = 0x8,
}

internal enum VkDescriptorMappingSourceEXT
{
    PushAddress = 6,
}

[Flags]
internal enum VkDescriptorPoolCreateFlags
{
    None = 0,
    UpdateAfterBind = 0x2,
}

[Flags]
internal enum VkDescriptorSetLayoutCreateFlags
{
    None = 0,
    UpdateAfterBindPool = 0x2,
}

internal enum VkDescriptorType
{
    Sampler = 0,
    SampledImage = 2,
    StorageImage = 3,
}

[Flags]
internal enum VkDeviceCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkDeviceQueueCreateFlags
{
    None = 0,
    Protected = 0x1,
    InternallySynchronizedKHR = 0x4,
}

internal enum VkDriverId
{
    AMDProprietary = 1,
}

internal enum VkDynamicState
{
    StencilCompareMask = 6,
    StencilWriteMask = 7,
    StencilReference = 8,
    ViewportWithCount = 1000267003,
    ScissorWithCount = 1000267004,
    DepthTestEnable = 1000267006,
    DepthWriteEnable = 1000267007,
    DepthCompareOp = 1000267008,
    StencilTestEnable = 1000267010,
    StencilOp = 1000267011,
}

[Flags]
internal enum VkFenceCreateFlags
{
    None = 0,
    Signaled = 0x1,
}

internal enum VkFilter
{
    Nearest = 0,
    Linear = 1,
    CubicEXT = 1000015000,
    CubicIMG = 1000015000,
}

internal enum VkFormat
{
    Undefined = 0,
    R4G4B4A4UnormPack16 = 2,
    R5G6B5UnormPack16 = 4,
    R5G5B5A1UnormPack16 = 6,
    R8Unorm = 9,
    R8Uint = 13,
    R8Srgb = 15,
    R8G8Unorm = 16,
    R8G8Uint = 20,
    R8G8Srgb = 22,
    R8G8B8A8Unorm = 37,
    R8G8B8A8Uint = 41,
    R8G8B8A8Srgb = 43,
    B8G8R8A8Unorm = 44,
    B8G8R8A8Uint = 48,
    B8G8R8A8Srgb = 50,
    A2B10G10R10UnormPack32 = 64,
    R16Unorm = 70,
    R16Uint = 74,
    R16Sfloat = 76,
    R16G16Unorm = 77,
    R16G16Uint = 81,
    R16G16Sfloat = 83,
    R16G16B16A16Unorm = 91,
    R16G16B16A16Uint = 95,
    R16G16B16A16Sfloat = 97,
    R32Uint = 98,
    R32Sfloat = 100,
    R32G32Uint = 101,
    R32G32Sfloat = 103,
    R32G32B32A32Uint = 107,
    R32G32B32A32Sfloat = 109,
    B10G11R11UfloatPack32 = 122,
    D16Unorm = 124,
    D32Sfloat = 126,
    S8Uint = 127,
    D24UnormS8Uint = 129,
    D32SfloatS8Uint = 130,
    Bc1RgbaUnormBlock = 133,
    Bc1RgbaSrgbBlock = 134,
    Bc2UnormBlock = 135,
    Bc2SrgbBlock = 136,
    Bc3UnormBlock = 137,
    Bc3SrgbBlock = 138,
    Bc4UnormBlock = 139,
    Bc4SnormBlock = 140,
    Bc5UnormBlock = 141,
    Bc6hUfloatBlock = 143,
    Bc6hSfloatBlock = 144,
    Bc7UnormBlock = 145,
    Bc7SrgbBlock = 146,
    EacR11G11UnormBlock = 155,
    Astc4x4UnormBlock = 157,
    Astc4x4SrgbBlock = 158,
}

[Flags]
internal enum VkFormatFeatureFlags
{
    None = 0,
}

[Flags]
internal enum VkFormatFeatureFlags2 : ulong
{
    SampledImage = 0x1UL,
    StorageImage = 0x2UL,
    ColorAttachment = 0x80UL,
    DepthStencilAttachment = 0x200UL,
    TransferSrc = 0x4000UL,
    TransferDst = 0x8000UL,
    StorageReadWithoutFormat = 0x80000000UL,
    StorageWriteWithoutFormat = 0x100000000UL,
}

internal enum VkFrontFace
{
    CounterClockwise = 0,
    Clockwise = 1,
}

[Flags]
internal enum VkImageAspectFlags
{
    Color = 0x1,
    Depth = 0x2,
    Stencil = 0x4,
    None = 0,
}

[Flags]
internal enum VkImageCreateFlags
{
    None = 0,
    MutableFormat = 0x8,
    CubeCompatible = 0x10,
}

internal enum VkImageLayout
{
    Undefined = 0,
    General = 1,
    PresentSrcKHR = 1000001002,
}

internal enum VkImageTiling
{
    Optimal = 0,
    Linear = 1,
    DrmFormatModifierEXT = 1000158000,
}

internal enum VkImageType
{
    Image1D = 0,
    Image2D = 1,
    Image3D = 2,
}

[Flags]
internal enum VkImageUsageFlags
{
    None = 0,
    TransferSrc = 0x1,
    TransferDst = 0x2,
    Sampled = 0x4,
    Storage = 0x8,
    ColorAttachment = 0x10,
    DepthStencilAttachment = 0x20,
}

[Flags]
internal enum VkImageViewCreateFlags
{
    None = 0,
    FragmentDensityMapDynamicEXT = 0x1,
    DescriptorBufferCaptureReplayEXT = 0x4,
    FragmentDensityMapDeferredEXT = 0x2,
}

internal enum VkImageViewType
{
    Image1D = 0,
    Image2D = 1,
    Image3D = 2,
    ImageCube = 3,
    Image2DArray = 5,
    ImageCubeArray = 6,
}

internal enum VkIndexType
{
    Uint16 = 0,
    Uint32 = 1,
    Uint8 = 1000265000,
    NoneKHR = 1000165000,
}

[Flags]
internal enum VkInstanceCreateFlags
{
    None = 0,
    EnumeratePortabilityKHR = 0x1,
}

internal enum VkInternalAllocationType
{
    Executable = 0,
}

internal enum VkLogicOp
{
    Clear = 0,
}

[Flags]
internal enum VkMemoryAllocateFlags
{
    None = 0,
    DeviceAddress = 0x2,
}

[Flags]
internal enum VkMemoryHeapFlags
{
    None = 0,
    DeviceLocal = 0x1,
    MultiInstance = 0x2,
    TileMemoryQCOM = 0x8,
}

[Flags]
internal enum VkMemoryMapFlags
{
    None = 0,
    PlacedEXT = 0x1,
}

[Flags]
internal enum VkMemoryPropertyFlags
{
    None = 0,
    DeviceLocal = 0x1,
    HostVisible = 0x2,
    HostCoherent = 0x4,
    HostCached = 0x8,
    LazilyAllocated = 0x10,
    Protected = 0x20,
    DeviceCoherentAMD = 0x40,
    DeviceUncachedAMD = 0x80,
}

internal enum VkObjectType
{
    Unknown = 0,
}

internal enum VkPhysicalDeviceType
{
    DiscreteGpu = 2,
}

internal enum VkPipelineBindPoint
{
    Graphics = 0,
    Compute = 1,
}

[Flags]
internal enum VkPipelineCacheCreateFlags
{
    None = 0,
    ExternallySynchronized = 0x1,
    InternallySynchronizedMergeKHR = 0x8,
}

[Flags]
internal enum VkPipelineColorBlendStateCreateFlags
{
    None = 0,
    RasterizationOrderAttachmentAccessEXT = 0x1,
}

[Flags]
internal enum VkPipelineCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineCreateFlags2 : ulong
{
    DescriptorHeapEXT = 0x1000000000UL,
}

[Flags]
internal enum VkPipelineDepthStencilStateCreateFlags
{
    None = 0,
    RasterizationOrderAttachmentDepthAccessEXT = 0x1,
    RasterizationOrderAttachmentStencilAccessEXT = 0x2,
}

[Flags]
internal enum VkPipelineDynamicStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineInputAssemblyStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineLayoutCreateFlags
{
    None = 0,
    IndependentSetsEXT = 0x2,
    NoTaskShaderKHR = 0x4,
}

[Flags]
internal enum VkPipelineMultisampleStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineRasterizationStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineShaderStageCreateFlags
{
    None = 0,
    AllowVaryingSubgroupSize = 0x1,
    RequireFullSubgroups = 0x2,
}

[Flags]
internal enum VkPipelineStageFlags2 : ulong
{
    None = 0,
    DrawIndirect = 0x2UL,
    VertexShader = 0x8UL,
    FragmentShader = 0x80UL,
    EarlyFragmentTests = 0x100UL,
    LateFragmentTests = 0x200UL,
    ColorAttachmentOutput = 0x400UL,
    ComputeShader = 0x800UL,
    Host = 0x4000UL,
    AllCommands = 0x10000UL,
    Copy = 0x100000000UL,
    IndexInput = 0x1000000000UL,
    TaskShaderEXT = 0x80000UL,
    MeshShaderEXT = 0x100000UL,
}

[Flags]
internal enum VkPipelineTessellationStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineVertexInputStateCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkPipelineViewportStateCreateFlags
{
    None = 0,
}

internal enum VkPolygonMode
{
    Fill = 0,
    Line = 1,
    Point = 2,
    FillRectangleNV = 1000153000,
}

internal enum VkPresentModeKHR
{
    Immediate = 0,
    Fifo = 2,
}

internal enum VkPrimitiveTopology
{
    PointList = 0,
    LineList = 1,
    LineStrip = 2,
    TriangleList = 3,
    TriangleStrip = 4,
    TriangleFan = 5,
    LineListWithAdjacency = 6,
    LineStripWithAdjacency = 7,
    TriangleListWithAdjacency = 8,
    TriangleStripWithAdjacency = 9,
    PatchList = 10,
}

[Flags]
internal enum VkQueryControlFlags
{
    None = 0,
    Precise = 0x1,
}

[Flags]
internal enum VkQueryPipelineStatisticFlags
{
    None = 0,
}

[Flags]
internal enum VkQueryPoolCreateFlags
{
    None = 0,
    ResetKHR = 0x1,
}

[Flags]
internal enum VkQueryResultFlags
{
    None = 0,
    Bit64 = 0x1,
}

internal enum VkQueryType
{
    Timestamp = 2,
}

[Flags]
internal enum VkQueueFlags
{
    None = 0,
    Graphics = 0x1,
    Compute = 0x2,
    Transfer = 0x4,
    SparseBinding = 0x8,
    Protected = 0x10,
}

[Flags]
internal enum VkRenderingFlags
{
    None = 0,
    ContentsSecondaryCommandBuffers = 0x1,
    Suspending = 0x2,
    Resuming = 0x4,
    EnableLegacyDitheringEXT = 0x8,
    ContentsInlineKHR = 0x10,
    PerLayerFragmentDensityVALVE = 0x20,
    FragmentRegionEXT = 0x40,
    CustomResolveEXT = 0x80,
    LocalReadConcurrentAccessControlKHR = 0x100,
}

[Flags]
internal enum VkResolveModeFlags
{
    None = 0,
    Average = 0x2,
}

internal enum VkResult
{
    Success = 0,
    NotReady = 1,
    Incomplete = 5,
    ErrorOutOfHostMemory = -1,
    ErrorOutOfDeviceMemory = -2,
    ErrorInitializationFailed = -3,
    ErrorDeviceLost = -4,
    ErrorMemoryMapFailed = -5,
    ErrorLayerNotPresent = -6,
    ErrorExtensionNotPresent = -7,
    ErrorFeatureNotPresent = -8,
    ErrorIncompatibleDriver = -9,
    ErrorTooManyObjects = -10,
    ErrorFormatNotSupported = -11,
    ErrorUnknown = -13,
    ErrorSurfaceLostKHR = -1000000000,
    SuboptimalKHR = 1000001003,
    ErrorOutOfDateKHR = -1000001004,
    ErrorInvalidShaderNV = -1000012000,
}

[Flags]
internal enum VkSampleCountFlags
{
    None = 0,
    Count1 = 0x1,
    Count2 = 0x2,
    Count4 = 0x4,
    Count8 = 0x8,
    Count16 = 0x10,
    Count32 = 0x20,
    Count64 = 0x40,
}

internal enum VkSamplerAddressMode
{
    Repeat = 0,
    MirroredRepeat = 1,
    ClampToEdge = 2,
    ClampToBorder = 3,
    MirrorClampToEdge = 4,
}

[Flags]
internal enum VkSamplerCreateFlags
{
    None = 0,
}

internal enum VkSamplerMipmapMode
{
    Nearest = 0,
    Linear = 1,
}

[Flags]
internal enum VkSemaphoreCreateFlags
{
    None = 0,
}

internal enum VkSemaphoreType
{
    Binary = 0,
    Timeline = 1,
}

[Flags]
internal enum VkSemaphoreWaitFlags
{
    None = 0,
    Any = 0x1,
}

internal enum VkShaderFloatControlsIndependence
{
    Bit32Only = 0,
    All = 1,
    None = 2,
}

[Flags]
internal enum VkShaderModuleCreateFlags
{
    None = 0,
}

[Flags]
internal enum VkShaderStageFlags
{
    None = 0,
    Vertex = 0x1,
    Fragment = 0x10,
    Compute = 0x20,
    TaskEXT = 0x40,
    MeshEXT = 0x80,
}

internal enum VkSharingMode
{
    Exclusive = 0,
    Concurrent = 1,
}

[Flags]
internal enum VkSpirvResourceTypeFlagsEXT
{
    None = 0,
    UniformBuffer = 0x20,
    ReadOnlyStorageBuffer = 0x40,
}

[Flags]
internal enum VkStencilFaceFlags
{
    None = 0,
    Front = 0x1,
    Back = 0x2,
    FrontAndBack = 0x3,
}

internal enum VkStencilOp
{
    Keep = 0,
    Zero = 1,
    Replace = 2,
    IncrementAndClamp = 3,
    DecrementAndClamp = 4,
    Invert = 5,
    IncrementAndWrap = 6,
    DecrementAndWrap = 7,
}

internal enum VkStructureType
{
    ApplicationInfo = 0,
    InstanceCreateInfo = 1,
    DeviceQueueCreateInfo = 2,
    DeviceCreateInfo = 3,
    MemoryAllocateInfo = 5,
    FenceCreateInfo = 8,
    SemaphoreCreateInfo = 9,
    QueryPoolCreateInfo = 11,
    BufferCreateInfo = 12,
    ImageCreateInfo = 14,
    ImageViewCreateInfo = 15,
    ShaderModuleCreateInfo = 16,
    PipelineCacheCreateInfo = 17,
    PipelineShaderStageCreateInfo = 18,
    PipelineVertexInputStateCreateInfo = 19,
    PipelineInputAssemblyStateCreateInfo = 20,
    PipelineTessellationStateCreateInfo = 21,
    PipelineViewportStateCreateInfo = 22,
    PipelineRasterizationStateCreateInfo = 23,
    PipelineMultisampleStateCreateInfo = 24,
    PipelineDepthStencilStateCreateInfo = 25,
    PipelineColorBlendStateCreateInfo = 26,
    PipelineDynamicStateCreateInfo = 27,
    GraphicsPipelineCreateInfo = 28,
    ComputePipelineCreateInfo = 29,
    PipelineLayoutCreateInfo = 30,
    SamplerCreateInfo = 31,
    DescriptorSetLayoutCreateInfo = 32,
    DescriptorPoolCreateInfo = 33,
    DescriptorSetAllocateInfo = 34,
    WriteDescriptorSet = 35,
    CopyDescriptorSet = 36,
    CommandPoolCreateInfo = 39,
    CommandBufferAllocateInfo = 40,
    CommandBufferInheritanceInfo = 41,
    CommandBufferBeginInfo = 42,
    MemoryAllocateFlagsInfo = 1000060000,
    MemoryRequirements2 = 1000146003,
    PhysicalDeviceFeatures2 = 1000059000,
    PhysicalDeviceProperties2 = 1000059001,
    FormatProperties2 = 1000059002,
    ImageFormatProperties2 = 1000059003,
    PhysicalDeviceImageFormatInfo2 = 1000059004,
    ImageViewUsageCreateInfo = 1000117002,
    DescriptorSetLayoutSupport = 1000168001,
    PhysicalDeviceVulkan11Features = 49,
    PhysicalDeviceVulkan12Features = 51,
    PhysicalDeviceVulkan12Properties = 52,
    ImageFormatListCreateInfo = 1000147000,
    SemaphoreTypeCreateInfo = 1000207002,
    SemaphoreWaitInfo = 1000207004,
    BufferDeviceAddressInfo = 1000244001,
    DescriptorSetLayoutBindingFlagsCreateInfo = 1000161000,
    DescriptorSetVariableDescriptorCountAllocateInfo = 1000161003,
    DescriptorSetVariableDescriptorCountLayoutSupport = 1000161004,
    PhysicalDeviceVulkan13Features = 53,
    MemoryBarrier2 = 1000314000,
    BufferMemoryBarrier2 = 1000314001,
    ImageMemoryBarrier2 = 1000314002,
    DependencyInfo = 1000314003,
    SubmitInfo2 = 1000314004,
    SemaphoreSubmitInfo = 1000314005,
    CommandBufferSubmitInfo = 1000314006,
    PhysicalDeviceSynchronization2Features = 1000314007,
    CopyBufferInfo2 = 1000337000,
    CopyImageInfo2 = 1000337001,
    CopyBufferToImageInfo2 = 1000337002,
    CopyImageToBufferInfo2 = 1000337003,
    BufferCopy2 = 1000337006,
    ImageCopy2 = 1000337007,
    BufferImageCopy2 = 1000337009,
    FormatProperties3 = 1000360000,
    PhysicalDeviceMaintenance4Features = 1000413000,
    DeviceBufferMemoryRequirements = 1000413002,
    DeviceImageMemoryRequirements = 1000413003,
    RenderingInfo = 1000044000,
    RenderingAttachmentInfo = 1000044001,
    PipelineRenderingCreateInfo = 1000044002,
    PhysicalDeviceDynamicRenderingFeatures = 1000044003,
    PhysicalDeviceVulkan14Features = 55,
    PipelineCreateFlags2CreateInfo = 1000470005,
    SwapchainCreateInfoKHR = 1000001000,
    PresentInfoKHR = 1000001001,
    XlibSurfaceCreateInfoKHR = 1000004000,
    XcbSurfaceCreateInfoKHR = 1000005000,
    WaylandSurfaceCreateInfoKHR = 1000006000,
    Win32SurfaceCreateInfoKHR = 1000009000,
    PhysicalDeviceSurfaceInfo2KHR = 1000119000,
    SurfaceCapabilities2KHR = 1000119001,
    DebugUtilsObjectNameInfoEXT = 1000128000,
    DebugUtilsLabelEXT = 1000128002,
    DebugUtilsMessengerCallbackDataEXT = 1000128003,
    DebugUtilsMessengerCreateInfoEXT = 1000128004,
    ImageDescriptorInfoEXT = 1000135001,
    ResourceDescriptorInfoEXT = 1000135002,
    BindHeapInfoEXT = 1000135003,
    PushDataInfoEXT = 1000135004,
    DescriptorSetAndBindingMappingEXT = 1000135005,
    ShaderDescriptorSetAndBindingMappingInfoEXT = 1000135006,
    PhysicalDeviceDescriptorHeapPropertiesEXT = 1000135008,
    PhysicalDeviceDescriptorHeapFeaturesEXT = 1000135009,
    PhysicalDeviceExtendedDynamicStateFeaturesEXT = 1000267000,
    DeviceMemoryCopyKHR = 1000318000,
    CopyDeviceMemoryInfoKHR = 1000318001,
    DeviceMemoryImageCopyKHR = 1000318002,
    CopyDeviceMemoryImageInfoKHR = 1000318003,
    PhysicalDeviceDeviceAddressCommandsFeaturesKHR = 1000318006,
    BindIndexBuffer3InfoKHR = 1000318007,
    DrawIndirect2InfoKHR = 1000318009,
    DispatchIndirect2InfoKHR = 1000318011,
    PhysicalDeviceMeshShaderFeaturesEXT = 1000328000,
    PhysicalDeviceShaderUntypedPointersFeaturesKHR = 1000387000,
    SurfacePresentModeKHR = 1000274000,
    PhysicalDeviceSwapchainMaintenance1FeaturesKHR = 1000275000,
    SwapchainPresentFenceInfoKHR = 1000275001,
    SwapchainPresentModesCreateInfoKHR = 1000275002,
    ReleaseSwapchainImagesInfoKHR = 1000275005,
    PhysicalDeviceUnifiedImageLayoutsFeaturesKHR = 1000527000,
}

[Flags]
internal enum VkSubmitFlags
{
    None = 0,
    Protected = 0x1,
}

[Flags]
internal enum VkSurfaceTransformFlagsKHR
{
    None = 0,
}

[Flags]
internal enum VkSwapchainCreateFlagsKHR
{
    None = 0,
}

internal enum VkSystemAllocationScope
{
    Command = 0,
}

internal enum VkVertexInputRate
{
    Vertex = 0,
    Instance = 1,
}

[Flags]
internal enum VkWaylandSurfaceCreateFlagsKHR
{
    None = 0,
}

[Flags]
internal enum VkWin32SurfaceCreateFlagsKHR
{
    None = 0,
}

[Flags]
internal enum VkXcbSurfaceCreateFlagsKHR
{
    None = 0,
}

[Flags]
internal enum VkXlibSurfaceCreateFlagsKHR
{
    None = 0,
}

