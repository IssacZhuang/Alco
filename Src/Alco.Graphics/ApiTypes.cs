// Public API types of Alco.Graphics.

using System.Numerics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A two-dimensional extent in texels.</summary>
public record struct Extent2D
{
    public uint X;
    public uint Y;

    public Extent2D(uint x, uint y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>A three-dimensional extent in texels.</summary>
public record struct Extent3D
{
    public uint X;
    public uint Y;
    public uint Z;

    public Extent3D(uint x, uint y, uint z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>Failure categories carried by <see cref="GPUException"/> and presentation status results.</summary>
public enum Error : byte
{
    None,
    Unsupported,
    DeviceLost,
    DriverError,
    OutOfMemory,
}

/// <summary>A device or resource creation failure.</summary>
public sealed class GPUException : Exception
{
    /// <summary>The backend-independent failure category.</summary>
    public Error Error { get; }

    /// <summary>Creates an exception describing a graphics creation failure.</summary>
    /// <param name="error">The failure category.</param>
    /// <param name="message">The failed operation and backend diagnostic.</param>
    public GPUException(Error error, string message) : base(message)
    {
        Error = error;
    }
}

/// <summary>Receives synchronous diagnostics without throwing; fatal errors still abort after the callback returns.</summary>
/// <param name="message">The backend diagnostic.</param>
/// <param name="fatal">Whether execution will terminate after the callback returns.</param>
public delegate void ErrorCallback(string message, bool fatal);

/// <summary>The backing memory class of a GPU heap.</summary>
public enum MemoryType : byte
{
    CpuVisible,
    GpuOnly,
    Readback,
}

/// <summary>A size in bytes together with its alignment requirement.</summary>
public record struct SizeAlign
{
    public ulong Size;
    public ulong Align;
}

/// <summary>A shader-visible buffer view identifying a backing buffer and a byte offset within it.</summary>
public record struct GPUBufferView
{
    /// <summary>The device-local backing-buffer index; zero denotes an empty view.</summary>
    public uint Index;

    /// <summary>The byte offset within the backing buffer.</summary>
    public uint Offset;

    /// <summary>Advances the view within its backing buffer.</summary>
    /// <param name="byteOffset">The additional offset in bytes.</param>
    /// <returns>A view of the same buffer at the advanced offset.</returns>
    public readonly GPUBufferView OffsetBy(uint byteOffset)
    {
        return new GPUBufferView { Index = Index, Offset = Offset + byteOffset };
    }
}

/// <summary>A byte range within a backing buffer. Subranges do not own the buffer.</summary>
public struct GPURange
{
    /// <summary>The backing buffer; null in an empty range.</summary>
    public GPUHeapOwner? Buffer;

    /// <summary>The byte offset within the backing buffer.</summary>
    public ulong Offset;

    /// <summary>The range size in bytes.</summary>
    public ulong Size;

    /// <summary>Returns the remaining range after a byte offset.</summary>
    /// <param name="byteOffset">The offset relative to this range.</param>
    /// <returns>The remaining subrange.</returns>
    public readonly GPURange Slice(ulong byteOffset)
    {
        return new GPURange { Buffer = Buffer, Offset = Offset + byteOffset, Size = Size - byteOffset };
    }

    /// <summary>Returns a subrange without changing its backing buffer.</summary>
    /// <param name="byteOffset">The offset relative to this range.</param>
    /// <param name="byteSize">The subrange size in bytes.</param>
    /// <returns>The specified subrange.</returns>
    public readonly GPURange Slice(ulong byteOffset, ulong byteSize)
    {
        return new GPURange { Buffer = Buffer, Offset = Offset + byteOffset, Size = byteSize };
    }

    /// <summary>The shader-visible buffer view covering this range, or an empty view for an empty range.</summary>
    public readonly GPUBufferView View => Buffer == null || Buffer.ViewIndex == 0
        ? default
        : new GPUBufferView { Index = Buffer.ViewIndex, Offset = checked((uint)Offset) };
}

/// <summary>A heap allocation exposing a buffer range and an optional CPU mapping.</summary>
/// <typeparam name="T">Element type of the CPU mapping; offsets and sizes are always in bytes.</typeparam>
public unsafe struct GPUAllocation<T> where T : unmanaged
{
    /// <summary>CPU mapping; null for GPU-only memory. Treat upload mappings as write-only.</summary>
    public T* Cpu;

    /// <summary>The backing buffer; null for CPU-only storage or an empty allocation.</summary>
    public GPUHeapOwner? Buffer;

    /// <summary>The byte offset within the backing buffer.</summary>
    public ulong Offset;

    /// <summary>Allocation size in bytes.</summary>
    public ulong SizeBytes;

    /// <summary>The shader-visible view, or an empty view for CPU-only storage.</summary>
    public readonly GPUBufferView View => Buffer == null || Buffer.ViewIndex == 0 ? default :
        new GPUBufferView { Index = Buffer.ViewIndex, Offset = (uint)Offset };

    /// <summary>CPU mapping as a span of <typeparamref name="T"/>.</summary>
    /// <returns>The mapped elements, or an empty span when no CPU mapping exists.</returns>
    public readonly Span<T> CpuSpan => Cpu == null ? Span<T>.Empty : new(Cpu, checked((int)(SizeBytes / (ulong)sizeof(T))));

    /// <summary>The resource range covered by this allocation, or an empty range for CPU-only storage.</summary>
    public readonly GPURange GPURange => Buffer == null ? default : new GPURange { Buffer = Buffer, Offset = Offset, Size = SizeBytes };
}

/// <summary>A linear GPU heap; copies alias the same allocation.</summary>
public unsafe struct GPUHeap
{
    /// <summary>The mapped range; release it exactly once through <see cref="Destroy"/>.</summary>
    public GPUAllocation<byte> Range;

    /// <summary>The backing allocation; null in a default, uninitialized heap.</summary>
    public GPUHeapOwner? Owner;

    /// <summary>Releases the heap; call exactly once per created heap.</summary>
    public void Destroy() => Owner?.Destroy();
}

/// <summary>A dedicated texture-placement heap; copies alias the same allocation.</summary>
public struct GPUTextureHeap
{
    /// <summary>Heap size in bytes.</summary>
    public ulong Size;

    /// <summary>The backing allocation; null in a default, uninitialized heap.</summary>
    public GPUTextureHeapOwner? Owner;

    /// <summary>Releases the heap; call exactly once per created heap.</summary>
    public void Destroy() => Owner?.Destroy();
}

/// <summary>A completion value on a timeline semaphore.</summary>
public struct TimelinePoint
{
    /// <summary>The timeline to wait on; null combined with <see cref="Wait()"/> returning immediately.</summary>
    public GPUTimelineSemaphore? Semaphore;
    public ulong Value;

    /// <summary>Blocks the calling thread until the GPU reaches this point; a null semaphore returns immediately.</summary>
    public void Wait() => Semaphore?.Wait(Value);
}

/// <summary>One queue submission; <see cref="Completion"/> is required.</summary>
public ref struct SubmitDesc
{
    public ReadOnlySpan<GPUCommandBuffer> Commands;
    public ReadOnlySpan<TimelinePoint> Waits; // GPU waits cover all command stages.
    public TimelinePoint Completion; // Required for every submission.
}

/// <summary>Concrete texture formats; <see cref="Undefined"/> terminates the list of concrete formats.</summary>
public enum Format : byte
{
    R8Srgb,
    Rg8Srgb,
    Rgba8Srgb,
    Bgra8Srgb,

    Rgba4Unorm,
    R5g5b5a1Unorm,
    R5g6b5Unorm,

    R8Unorm,
    Rg8Unorm,
    Rgba8Unorm,
    Bgra8Unorm,
    R16Unorm,
    Rg16Unorm,
    Rgba16Unorm,

    R8Uint,
    Rg8Uint,
    Rgba8Uint,
    Bgra8Uint,
    R16Uint,
    Rg16Uint,
    Rgba16Uint,
    R32Uint,
    Rg32Uint,
    Rgba32Uint,

    R16Float,
    Rg16Float,
    Rgba16Float,
    R32Float,
    Rg32Float,
    Rgba32Float,

    Rgb10a2Unorm,
    Rg11b10Float,

    D16Unorm,
    D24UnormS8Uint,
    D32Float,
    S8Uint,
    D32FloatS8Uint,

    EacRg,
    Astc4x4Srgb,
    Astc4x4Unorm,
    Bc3Srgb,
    Bc3Unorm,
    Bc5Rg,
    Bc6HUfloat,
    Bc6HSfloat,
    Bc7Srgb,
    Bc7Unorm,

    /// <summary>BC1 RGBA with sRGB color and one-bit alpha.</summary>
    Bc1Srgb,
    /// <summary>BC1 RGBA with normalized unsigned color and one-bit alpha.</summary>
    Bc1Unorm,
    /// <summary>BC2 RGBA with sRGB color and four-bit alpha.</summary>
    Bc2Srgb,
    /// <summary>BC2 RGBA with normalized unsigned color and four-bit alpha.</summary>
    Bc2Unorm,
    /// <summary>BC4 red with normalized unsigned values.</summary>
    Bc4Unorm,
    /// <summary>BC4 red with normalized signed values.</summary>
    Bc4Snorm,

    Undefined, // Must remain last; preceding values are concrete texture formats.
}

/// <summary>Texel-block geometry of a <see cref="Format"/>.</summary>
public record struct TextureFormatInfo
{
    public Extent2D BlockExtent;
    public uint BytesPerBlock;
    public bool Depth;
    public bool Stencil;
}

/// <summary>The dimensionality of a texture.</summary>
public enum TextureType : byte
{
    OneD,
    TwoD,
    ThreeD,
    Cube,
    TwoDArray,
    CubeArray,
}

[Flags]
public enum TextureUsage : uint
{
    None = 0,
    Sampled = 1u << 0,
    Storage = 1u << 1,
    ColorAttachment = 1u << 2,
    DepthStencilAttachment = 1u << 3,
    TransferSource = 1u << 4,
    TransferDestination = 1u << 5,
}

/// <summary>The resource view a texture descriptor heap slot exposes.</summary>
public enum TextureDescriptorType : byte
{
    Sampled,
    Storage,
}

/// <summary>Selects which aspect of a combined depth/stencil texture a descriptor uses.</summary>
public enum TextureAspect : byte
{
    Automatic,
    Color,
    Depth,
    Stencil,
}

/// <summary>Texture sampling filters.</summary>
public enum Filter : byte
{
    Nearest,
    Linear,
}

/// <summary>Texture address wrapping modes.</summary>
public enum AddressMode : byte
{
    Repeat,
    MirroredRepeat,
    ClampToEdge,
}

/// <summary>Comparison operators for depth and sampler comparisons.</summary>
public enum CompareOp : byte
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always,
}

/// <summary>Vertex assembly modes shared by graphics backends.</summary>
public enum PrimitiveTopology : byte
{
    /// <summary>Independent triangles; the default topology.</summary>
    TriangleList,
    /// <summary>Independent points; the vertex shader must output a point size of 1.0.</summary>
    PointList,
    /// <summary>Independent one-pixel-wide line segments.</summary>
    LineList,
    /// <summary>Connected one-pixel-wide line segments.</summary>
    LineStrip,
    /// <summary>Connected triangles sharing successive vertex pairs.</summary>
    TriangleStrip,
}

/// <summary>Triangle culling modes, named by winding in screen space.</summary>
public enum CullMode : byte
{
    None,
    Clockwise,
    CounterClockwise,
}

/// <summary>Triangle rasterization fill modes; wireframe draws one-pixel-wide edges and is not guaranteed pixel-identical across backends.</summary>
public enum FillMode : byte
{
    Solid,
    Wireframe,
}

/// <summary>Blend input selectors.</summary>
public enum BlendFactor : byte
{
    Zero,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
    SourceAlphaSaturate,
}

/// <summary>Color blend arithmetic operators.</summary>
public enum BlendOp : byte
{
    Add,
    Subtract,
    ReverseSubtract,
    Minimum,
    Maximum,
}

/// <summary>Index buffer element types.</summary>
public enum IndexType : byte
{
    UInt16,
    UInt32,
}

/// <summary>Render-pass attachment contents at pass begin.</summary>
public enum LoadOp : byte
{
    Load,
    Clear,
    Discard,
}

/// <summary>Render-pass attachment contents at pass end.</summary>
public enum StoreOp : byte
{
    Store,
    Discard,
}

/// <summary>Stencil buffer update operators.</summary>
public enum StencilOp : byte
{
    Keep,
    Zero,
    Replace,
    IncrementClamp,
    DecrementClamp,
    Invert,
    IncrementWrap,
    DecrementWrap,
}

// Ordered by Vulkan's logical execution order where stages are comparable.
// A barrier's before execution scope includes the selected and logically earlier
// stages; its after execution scope includes the selected and logically later
// stages. Vertex and task/mesh are alternative graphics branches, DepthStencilTests
// spans early and late tests around pixel, compute and transfer are separate
// pipelines, host is a pseudo-stage, and None/AllCommands are special masks.
[Flags]
public enum Stage : ulong
{
    None = 0,
    Indirect = 1ul << 0,
    IndexInput = 1ul << 1,
    Vertex = 1ul << 2,
    Task = 1ul << 3,
    Mesh = 1ul << 4,
    DepthStencilTests = 1ul << 5,
    Pixel = 1ul << 6,
    ColorOutput = 1ul << 7,
    Compute = 1ul << 8,
    Transfer = 1ul << 9,
    Host = 1ul << 10, // Barrier destination only, paired with HostRead.
    AllCommands = 1ul << 11, // All GPU command stages; excludes host.
}

[Flags]
public enum Access : ulong
{
    None = 0,
    TransferRead = 1ul << 0,
    TransferWrite = 1ul << 1,
    ShaderRead = 1ul << 2,
    ShaderWrite = 1ul << 3,
    ColorRead = 1ul << 4,
    ColorWrite = 1ul << 5,
    DepthStencilRead = 1ul << 6,
    DepthStencilWrite = 1ul << 7,
    IndirectRead = 1ul << 8,
    IndexRead = 1ul << 9,
    HostRead = 1ul << 10,
    DescriptorRead = 1ul << 11,
}

/// <summary>Immutable device capabilities, valid for the device's lifetime.</summary>
public sealed class DeviceCaps
{
    /// <summary>Driver-reported device name.</summary>
    public string DeviceName { get; internal set; } = "";

    // Flat queue indices: general first, then compute-only, then copy-only. Swapchains present on queue zero.
    /// <summary>Total number of queues.</summary>
    public uint QueueCount { get; internal set; }

    /// <summary>Number of general graphics + compute queues.</summary>
    public uint GeneralQueueCount { get; internal set; }

    /// <summary>Number of compute-only queues.</summary>
    public uint ComputeQueueCount { get; internal set; }

    /// <summary>Number of copy-only queues.</summary>
    public uint CopyQueueCount { get; internal set; }

    // Copy-only texture offsets/extents align to these texel-block counts, except at mip edges. Zero means whole mip levels only.
    /// <summary>Copy-queue texture copy granularity in texel blocks.</summary>
    public Extent3D CopyTextureGranularity { get; internal set; } = new(1, 1, 1);

    /// <summary>Maximum root (push data) size in bytes.</summary>
    public ulong MaxPushDataSize { get; internal set; }

    // Slot counts descriptor heaps receive when created without an explicit capacity; explicit capacities stay caller-chosen.
    /// <summary>The capacity of texture descriptor heaps created without an explicit capacity.</summary>
    public uint TextureDescriptorHeapCapacity { get; internal set; }

    /// <summary>The capacity of sampler descriptor heaps created without an explicit capacity.</summary>
    public uint SamplerDescriptorHeapCapacity { get; internal set; }

    // Common element size for suballocating GPUTextureHeap storage; every SizeAlign::align divides this value.
    /// <summary>Texture heap placement alignment in bytes.</summary>
    public ulong TextureHeapAlignment { get; internal set; }

    /// <summary>Nanoseconds per timestamp tick; zero when profiling is unavailable.</summary>
    public float TimestampPeriodNs { get; internal set; }

    /// <summary>Fractional filtering precision, for conservative sampled-field bounds.</summary>
    public uint SubTexelPrecisionBits { get; internal set; }

    /// <summary>BC block compression support.</summary>
    public bool TextureCompressionBc { get; internal set; }

    /// <summary>ASTC block compression support.</summary>
    public bool TextureCompressionAstc { get; internal set; }

    /// <summary>16-bit storage input/output support.</summary>
    public bool StorageInputOutput16 { get; internal set; }

    /// <summary>Indirect mesh-drawing support.</summary>
    public bool IndirectMeshDraw { get; internal set; }
}

/// <summary>Swapchain color spaces.</summary>
public enum ColorSpace : uint
{
    Srgb,
    ExtendedSrgbLinear,
}

internal enum SurfaceKind : byte
{
    None,
    Win32,
    MetalLayer,
    Xlib,
    Xcb,
    Wayland,
}

/// <summary>A borrowed native presentation source; its window, display, and layer must outlive the swapchain.</summary>
public readonly struct SurfaceSource
{
    internal SurfaceKind Kind { get; init; }
    internal nint Display { get; init; }
    internal nint Handle { get; init; }
    internal nuint WindowId { get; init; }

    /// <summary>Describes a Win32 window.</summary>
    /// <param name="hwnd">The HWND.</param>
    /// <param name="hInstance">The HINSTANCE that owns the window.</param>
    /// <returns>The native surface source.</returns>
    public static SurfaceSource FromWin32(nint hwnd, nint hInstance) => new() { Kind = SurfaceKind.Win32, Display = hInstance, Handle = hwnd };

    /// <summary>Describes a CAMetalLayer managed by the host application.</summary>
    /// <param name="layer">The CAMetalLayer pointer.</param>
    /// <returns>The native surface source.</returns>
    public static SurfaceSource FromMetalLayer(nint layer) => new() { Kind = SurfaceKind.MetalLayer, Handle = layer };

    /// <summary>Describes an Xlib window.</summary>
    /// <param name="display">The Xlib Display pointer.</param>
    /// <param name="window">The native Window ID.</param>
    /// <returns>The native surface source.</returns>
    public static SurfaceSource FromXlib(nint display, nuint window) => new() { Kind = SurfaceKind.Xlib, Display = display, WindowId = window };

    /// <summary>Describes an XCB window.</summary>
    /// <param name="connection">The xcb_connection_t pointer.</param>
    /// <param name="window">The native window ID.</param>
    /// <returns>The native surface source.</returns>
    public static SurfaceSource FromXcb(nint connection, uint window) => new() { Kind = SurfaceKind.Xcb, Display = connection, WindowId = window };

    /// <summary>Describes a Wayland surface.</summary>
    /// <param name="display">The wl_display pointer.</param>
    /// <param name="surface">The wl_surface pointer.</param>
    /// <returns>The native surface source.</returns>
    public static SurfaceSource FromWayland(nint display, nint surface) => new() { Kind = SurfaceKind.Wayland, Display = display, Handle = surface };
}

// Win32 swapchain lifecycle, reconfiguration, acquire, and presentation stay on the window's message-pump thread.
/// <summary>The native surface and presentation configuration of one window.</summary>
public struct SwapchainDesc
{
    /// <summary>The borrowed native surface source.</summary>
    public SurfaceSource Surface;

    /// <summary>The initial framebuffer extent in pixels; a zero dimension suspends presentation.</summary>
    public Extent2D Extent;

    /// <summary>The presentation format; Undefined selects the default non-sRGB presentation format for the color space.</summary>
    public Format Format;

    /// <summary>The presentation color space.</summary>
    public ColorSpace ColorSpace;

    /// <summary>The requested image count, from one through eight; the platform determines the actual count.</summary>
    public uint DesiredImageCount;

    /// <summary>Whether presentation is synchronized to the display refresh.</summary>
    public bool VSync;

    /// <summary>Enables the Metal Steam-overlay presentation path; ignored by other backends.</summary>
    public bool SteamOverlayCompatibility;

    /// <summary>Initializes an sRGB, display-synchronized swapchain description with two requested images.</summary>
    public SwapchainDesc()
    {
        Format = Format.Undefined;
        DesiredImageCount = 2;
        VSync = true;
    }
}

/// <summary>The graphics backend selected when creating a device.</summary>
public enum GraphicsBackend : byte
{
    /// <summary>Uses ALCO_GRAPHICS_BACKEND when set. Otherwise selects Dx12 on Windows, Metal on macOS, or Vulkan on Linux.</summary>
    Auto,

    /// <summary>Selects Vulkan.</summary>
    Vulkan,

    /// <summary>Selects Direct3D 12.</summary>
    Dx12,

    /// <summary>Selects Metal.</summary>
    Metal = 4,

    /// <summary>Selects Vulkan 1.2 with descriptor indexing and no mesh shaders.</summary>
    VulkanCompact = 5,

    /// <summary>Selects the virtual no-GPU backend for headless testing without hardware GPU access.</summary>
    NoGPU = 6,
}

/// <summary>Device backend, queues, and profiling configuration; presentation devices do not require a window at creation.</summary>
public unsafe struct DeviceDesc
{
    /// <summary>Whether to omit native presentation support; defaults to true.</summary>
    public bool Headless;

    // Counts are capped to each family's capacity. A nonzero request requires that kind of queue to be available.
    /// <summary>General graphics + compute queue request; must be nonzero.</summary>
    public uint DesiredQueueCount; // 1

    /// <summary>Compute-only queue request.</summary>
    public uint DesiredComputeQueueCount; // 0

    /// <summary>Copy-only queue request.</summary>
    public uint DesiredCopyQueueCount; // 0

    /// <summary>Timestamp slots per command buffer; zero disables timestamps.</summary>
    public uint TimestampQueryCount; // 256

    /// <summary>The graphics backend to use; defaults to <see cref="GraphicsBackend.Auto"/>.</summary>
    public GraphicsBackend Backend;

    /// <summary>Optional directory for persistent GPU pipeline cache files; null or empty disables on-disk persistence.</summary>
    public string? PipelineCacheDirectory;

    /// <summary>Initializes a headless device description with one general queue and 256 timestamp slots per command buffer.</summary>
    public DeviceDesc()
    {
        Headless = true;
        DesiredQueueCount = 1;
        TimestampQueryCount = 256;
    }
}

/// <summary>A borrowed presentation image, valid until its swapchain is presented, reconfigured, or destroyed.</summary>
public struct SwapchainFrame
{
    /// <summary>The borrowed attachment view; null while presentation is unavailable. Do not destroy this view.</summary>
    public GPURenderView? RenderView;

    /// <summary>The acquired image's framebuffer extent in pixels.</summary>
    public Extent2D Extent;

    /// <summary>
    /// The borrowed 2D, single-mip, single-slice attachment and transfer texture; null while unavailable.
    /// Use only on queue zero; do not destroy, initialize, or bind descriptors for it.
    /// </summary>
    public GPUTexture? Texture;
}

/// <summary>A texture creation description.</summary>
public record struct TextureDesc
{
    public TextureType Type; // TwoD
    public Extent3D Extent; // 1,1,1
    public uint MipLevels; // 1
    public uint LayerCount; // 1; Vulkan array layers; cube faces are individual layers.
    public Format Format; // Rgba8Unorm
    public bool MutableFormat; // Allow format-compatible descriptor views, but could lose DCC.
    public TextureUsage Usage; // Sampled

    /// <summary>One sample by default, or four for a single-mip 2D or 2D-array render attachment.</summary>
    /// <remarks>At four samples, use only ColorAttachment or DepthStencilAttachment usage, without mutable formats.</remarks>
    public uint SampleCount;

    /// <summary>Initializes a single-sample 2D RGBA8 texture for sampling.</summary>
    public TextureDesc()
    {
        Type = TextureType.TwoD;
        Extent = new Extent3D(1, 1, 1);
        MipLevels = 1;
        LayerCount = 1;
        Format = Format.Rgba8Unorm;
        MutableFormat = false;
        Usage = TextureUsage.Sampled;
        SampleCount = 1;
    }
}

/// <summary>A render-target view of one mip level and slice.</summary>
public record struct RenderViewDesc
{
    public uint MipLevel;
    public uint Slice; // Physical array slice; cube faces are individual slices.
}

/// <summary>A texture descriptor heap slot description.</summary>
public record struct TextureDescriptorDesc
{
    public Format Format; // Undefined inherits the texture format.
    public TextureAspect Aspect; // Automatic selects color, or depth before stencil.
    public uint BaseMip;
    public uint MipCount; // Zero selects every remaining mip level.
    public uint BaseLayer; // Vulkan array layer; cube faces are individual layers.
    public uint LayerCount; // Zero selects every remaining layer.

    public TextureDescriptorDesc()
    {
        Format = Format.Undefined;
        Aspect = TextureAspect.Automatic;
    }
}

/// <summary>A buffer/texture copy region; depth/stencil transfers require zero texture origin and a complete mip per selected slice.</summary>
public record struct TextureCopyDesc
{
    public uint MipLevel;
    public uint BaseSlice; // Physical array slice; cube faces are individual slices.
    public uint SliceCount; // Zero selects every remaining physical slice.
    public Extent3D Offset;
    public Extent3D Extent; // Zero components select the remaining mip extent.
    public ulong RowPitchBytes;   // Zero is tightly packed.
    public ulong SlicePitchBytes; // Zero is tightly packed.
}

/// <summary>
/// A texture-to-texture copy region. Origins are block-aligned; partial blocks reach both mip edges.
/// Depth/stencil copies require zero origins and complete mip subresources per selected slice.
/// </summary>
public record struct TextureRegionCopyDesc
{
    /// <summary>The source mip level.</summary>
    public uint SourceMipLevel;

    /// <summary>The destination mip level.</summary>
    public uint DestinationMipLevel;

    /// <summary>The first source physical slice; cube faces are individual slices.</summary>
    public uint SourceBaseSlice;

    /// <summary>The first destination physical slice; cube faces are individual slices.</summary>
    public uint DestinationBaseSlice;

    /// <summary>The slice count; zero selects all remaining source slices. For 3D textures, use one slice and zero base slices.</summary>
    public uint SliceCount;

    /// <summary>The source texel offset within the selected mip.</summary>
    public Extent3D SourceOffset;

    /// <summary>The destination texel offset within the selected mip.</summary>
    public Extent3D DestinationOffset;

    /// <summary>The texel extent; zero components select the remaining source mip extent, which must fit the destination without clamping.</summary>
    public Extent3D Extent;
}

/// <summary>A sampler description.</summary>
public record struct SamplerDesc
{
    public Filter MinFilter; // Linear
    public Filter MagFilter; // Linear
    public Filter MipFilter; // Linear
    public AddressMode AddressU; // Repeat
    public AddressMode AddressV; // Repeat
    public AddressMode AddressW; // Repeat
    public bool Anisotropic; // Uses the API's fixed 4x profile.
    public bool CompareEnabled;
    public CompareOp Compare; // LessEqual

    public SamplerDesc()
    {
        MinFilter = Filter.Linear;
        MagFilter = Filter.Linear;
        MipFilter = Filter.Linear;
        AddressU = AddressMode.Repeat;
        AddressV = AddressMode.Repeat;
        AddressW = AddressMode.Repeat;
        Anisotropic = false;
        CompareEnabled = false;
        Compare = CompareOp.LessEqual;
    }
}

/// <summary>Blend arithmetic for one color component.</summary>
public record struct BlendComponentState
{
    public BlendFactor Source; // One
    public BlendFactor Destination; // Zero
    public BlendOp Operation; // Add

    public BlendComponentState()
    {
        Source = BlendFactor.One;
        Destination = BlendFactor.Zero;
        Operation = BlendOp.Add;
    }
}

/// <summary>Color and alpha blending configuration.</summary>
public record struct BlendState
{
    /// <summary>Blending disabled (opaque overwrite).</summary>
    public static readonly BlendState Opaque = new()
    {
        Enabled = false,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    /// <summary>Standard straight-alpha source-over blending.</summary>
    public static readonly BlendState AlphaBlend = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    /// <summary>Additive alpha-weighted blending.</summary>
    public static readonly BlendState Additive = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
    };

    /// <summary>Premultiplied-alpha source-over blending.</summary>
    public static readonly BlendState PremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    /// <summary>Non-premultiplied alpha blending with symmetric alpha weights.</summary>
    public static readonly BlendState NonPremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    /// <summary>Multiplicative color blending.</summary>
    public static readonly BlendState Multiply = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.DestinationColor, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    /// <summary>Straight-alpha color blending that preserves the maximum destination alpha.</summary>
    public static readonly BlendState AlphaBlendNoAccumulation = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.One, Operation = BlendOp.Maximum },
    };

    public bool Enabled;
    public BlendComponentState Color;
    public BlendComponentState Alpha;

    public BlendState()
    {
        Color = new BlendComponentState();
        Alpha = new BlendComponentState();
    }
}

/// <summary>One color output target of a pipeline.</summary>
public record struct ColorTargetDesc
{
    public Format Format; // Undefined
    public BlendState Blend;
    public byte WriteMask; // 0xf

    /// <summary>Initializes a color target with default blending and all color channels writable.</summary>
    public ColorTargetDesc()
    {
        Format = Format.Undefined;
        Blend = new BlendState();
        WriteMask = 0xf;
    }
}

/// <summary>Rasterizer configuration.</summary>
public record struct RasterizationState
{
    /// <summary>Solid fill with no face culling.</summary>
    public static readonly RasterizationState CullNone = new()
    {
        Cull = CullMode.None,
        Fill = FillMode.Solid,
    };

    public CullMode Cull; // None
    public FillMode Fill; // Solid
    public float DepthBiasConstant;
    public float DepthBiasClamp;
    public float DepthBiasSlope;
}

/// <summary>A top-left framebuffer rectangle with positive dimensions and a zero-to-one depth range.</summary>
public record struct Viewport
{
    public float X;
    public float Y;
    public float Width; // 1
    public float Height; // 1
    public float MinDepth;
    public float MaxDepth; // 1

    public Viewport()
    {
        Width = 1.0f;
        Height = 1.0f;
        MaxDepth = 1.0f;
    }
}

/// <summary>A scissor rectangle.</summary>
public record struct Scissor
{
    public int X;
    public int Y;
    public uint Width; // 1
    public uint Height; // 1

    public Scissor()
    {
        Width = 1;
        Height = 1;
    }
}

/// <summary>Stencil test and update configuration for one face.</summary>
public record struct StencilFaceState
{
    public CompareOp Compare; // Always
    public StencilOp Fail; // Keep
    public StencilOp Pass; // Keep
    public StencilOp DepthFail; // Keep
    public byte Reference;

    public StencilFaceState()
    {
        Compare = CompareOp.Always;
        Fail = StencilOp.Keep;
        Pass = StencilOp.Keep;
        DepthFail = StencilOp.Keep;
        Reference = 0;
    }
}

/// <summary>Depth and stencil test configuration.</summary>
public record struct DepthStencilState
{
    /// <summary>Depth testing and writing disabled (compare Never).</summary>
    public static readonly DepthStencilState None = new() { DepthTest = false, DepthWrite = false, DepthCompare = CompareOp.Never };

    /// <summary>Standard depth testing and depth writes enabled (LessEqual).</summary>
    public static readonly DepthStencilState Write = new() { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.LessEqual };

    /// <summary>Standard read-only depth testing enabled (LessEqual).</summary>
    public static readonly DepthStencilState Read = new() { DepthTest = true, DepthWrite = false, DepthCompare = CompareOp.LessEqual };

    /// <summary>Reverse-Z depth testing and depth writes enabled (GreaterEqual).</summary>
    public static readonly DepthStencilState WriteReverseZ = new() { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.GreaterEqual };

    /// <summary>Reverse-Z read-only depth testing enabled (GreaterEqual).</summary>
    public static readonly DepthStencilState ReadReverseZ = new() { DepthTest = true, DepthWrite = false, DepthCompare = CompareOp.GreaterEqual };

    /// <summary>Depth testing and writing disabled (compare Always).</summary>
    public static readonly DepthStencilState Default = new() { DepthTest = false, DepthWrite = false, DepthCompare = CompareOp.Always };

    public bool DepthTest;
    public bool DepthWrite;
    public CompareOp DepthCompare; // LessEqual
    public bool StencilTest;
    public byte StencilReadMask; // 0xff
    public byte StencilWriteMask; // 0xff
    public StencilFaceState Front;
    public StencilFaceState Back;

    /// <summary>Initializes disabled depth and stencil tests with default comparisons and full stencil masks.</summary>
    public DepthStencilState()
    {
        DepthCompare = CompareOp.LessEqual;
        StencilReadMask = 0xff;
        StencilWriteMask = 0xff;
        Front = new StencilFaceState();
        Back = new StencilFaceState();
    }
}

/// <summary>One programmable shader stage.</summary>
public ref struct ShaderStage
{
    /// <summary>SPIR-V on Vulkan, DXIL on Direct3D 12, and MSL source or precompiled metallib on Metal. Empty omits an optional stage.</summary>
    public ReadOnlySpan<byte> Code;

    /// <summary>Entry point name inside the compiled code.</summary>
    public string EntryPoint; // "main"

    /// <summary>Must match the compiled numthreads where applicable.</summary>
    public Extent3D ThreadgroupSize; // 1,1,1

    public ShaderStage()
    {
        Code = default;
        EntryPoint = "main";
        ThreadgroupSize = new Extent3D(1, 1, 1);
    }
}

/// <summary>A vertex/pixel pipeline description.</summary>
public ref struct GraphicsPipelineDesc
{
    public ShaderStage Vertex;
    public ShaderStage Pixel; // Empty omits the pixel stage, for depth-only rasterization.
    public ReadOnlySpan<ColorTargetDesc> ColorTargets;
    public Format DepthFormat; // Undefined
    public Format StencilFormat; // Undefined
    public RasterizationState Rasterization;

    /// <summary>One sample by default, or four. Must match the render pass color, depth, and stencil attachments.</summary>
    public uint SampleCount;

    /// <summary>Vertex assembly mode; defaults to TriangleList.</summary>
    /// <remarks>Indexed strips restart at 0xFFFF (UInt16) or 0xFFFFFFFF (UInt32); these indices must not appear in lists.</remarks>
    public PrimitiveTopology Topology;

    /// <summary>Initializes a triangle-list pipeline description with undefined depth and stencil formats.</summary>
    public GraphicsPipelineDesc()
    {
        Vertex = new ShaderStage();
        Pixel = new ShaderStage();
        DepthFormat = Format.Undefined;
        StencilFormat = Format.Undefined;
        SampleCount = 1;
    }
}

/// <summary>A task/mesh/pixel pipeline description.</summary>
public ref struct MeshPipelineDesc
{
    public ShaderStage Task; // Empty launches mesh workgroups directly; otherwise draws launch task workgroups.
    public ShaderStage Mesh;
    public ShaderStage Pixel; // Empty omits the pixel stage, for depth-only rasterization.
    public ReadOnlySpan<ColorTargetDesc> ColorTargets;
    public Format DepthFormat; // Undefined
    public Format StencilFormat; // Undefined
    public RasterizationState Rasterization;

    /// <summary>One sample by default, or four. Must match the render pass color, depth, and stencil attachments.</summary>
    public uint SampleCount;

    /// <summary>Initializes a single-sample mesh pipeline description without depth or stencil attachments.</summary>
    public MeshPipelineDesc()
    {
        Task = new ShaderStage();
        Mesh = new ShaderStage();
        Pixel = new ShaderStage();
        DepthFormat = Format.Undefined;
        StencilFormat = Format.Undefined;
        SampleCount = 1;
    }
}

/// <summary>A color attachment binding for a render pass.</summary>
public struct ColorAttachment
{
    public GPURenderView? RenderView;

    /// <summary>Averages a four-sample color attachment into a single-sample 2D or 2D-array color attachment at pass end.</summary>
    /// <remarks>Both views must match the render area and format. The target excludes storage usage and must be initialized
    /// on a general queue if placed. Resolving is not supported in suspended or resumed passes.</remarks>
    public GPURenderView? ResolveView;

    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public Vector4 Clear; // 0,0,0,1 in RGBA component order.

    public ColorAttachment()
    {
        Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
    }
}

/// <summary>A depth attachment binding for a render pass.</summary>
public struct DepthAttachment
{
    public GPURenderView? RenderView;
    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public float Clear; // 1

    public DepthAttachment()
    {
        Clear = 1.0f;
    }
}

/// <summary>A stencil attachment binding for a render pass.</summary>
public struct StencilAttachment
{
    public GPURenderView? RenderView;
    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public byte Clear;
}

/// <summary>A render pass description.</summary>
public ref struct RenderingDesc
{
    public ReadOnlySpan<ColorAttachment> Colors;
    public DepthAttachment Depth;
    public StencilAttachment Stencil;

    /// <summary>Initializes a render pass description with no attachments and a depth clear value of one.</summary>
    public RenderingDesc()
    {
        Depth = new DepthAttachment();
    }
}

[Flags]
public enum RenderingFlags : uint
{
    None = 0,
    Suspending = 2,
    Resuming = 4,
}
