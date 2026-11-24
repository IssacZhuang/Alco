// Hardware-level ABI types and native backend descriptors under Alco.Graphics.LowLevel.

using System.Numerics;
using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>Receives synchronous diagnostics without throwing; fatal errors still abort after the callback returns.</summary>
/// <param name="message">The backend diagnostic.</param>
/// <param name="fatal">Whether execution will terminate after the callback returns.</param>
public delegate void ErrorCallback(string message, bool fatal);

/// <summary>A size in bytes together with its alignment requirement.</summary>
public record struct SizeAlign
{
    public ulong Size;
    public ulong Align;
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

/// <summary>A low-level color attachment binding using native render views.</summary>
public struct GPUColorAttachment
{
    public GPURenderView? RenderView;

    /// <summary>Averages a four-sample color attachment into a single-sample 2D or 2D-array color attachment at pass end.</summary>
    /// <remarks>Both views must match the render area and format. The target excludes storage usage and must be initialized
    /// on a general queue if placed. Resolving is not supported in suspended or resumed passes.</remarks>
    public GPURenderView? ResolveView;

    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public Vector4 Clear; // 0,0,0,1 in RGBA component order.

    public GPUColorAttachment()
    {
        Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
    }
}

/// <summary>A low-level depth attachment binding using a native render view.</summary>
public struct GPUDepthAttachment
{
    public GPURenderView? RenderView;
    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public float Clear; // 1

    public GPUDepthAttachment()
    {
        Clear = 1.0f;
    }
}

/// <summary>A low-level stencil attachment binding using a native render view.</summary>
public struct GPUStencilAttachment
{
    public GPURenderView? RenderView;
    public LoadOp Load; // Load
    public StoreOp Store; // Store
    public byte Clear;
}

/// <summary>A low-level render pass description using native render views.</summary>
public ref struct GPURenderingDesc
{
    public ReadOnlySpan<GPUColorAttachment> Colors;
    public GPUDepthAttachment Depth;
    public GPUStencilAttachment Stencil;

    /// <summary>Initializes a native render pass description with no attachments and a depth clear value of one.</summary>
    public GPURenderingDesc()
    {
        Depth = new GPUDepthAttachment();
    }
}

[Flags]
public enum RenderingFlags : uint
{
    None = 0,
    Suspending = 2,
    Resuming = 4,
}
