// Public API types of Alco.Graphics.

using System.Numerics;

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

/// <summary>The backing memory class of a GPU heap.</summary>
public enum MemoryType : byte
{
    CpuVisible,
    GpuOnly,
    Readback,
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

/// <summary>A color attachment binding for a high-level render pass.</summary>
public struct ColorAttachment
{
    /// <summary>The destination color target.</summary>
    public RenderTarget Target;

    /// <summary>Optional single-sample resolve target for a four-sample color attachment.</summary>
    public RenderTarget ResolveTarget;

    /// <summary>The operation performed on attachment contents at pass begin.</summary>
    public LoadOp Load;

    /// <summary>The operation performed on attachment contents at pass end.</summary>
    public StoreOp Store;

    /// <summary>The clear color in RGBA order when <see cref="Load"/> is <see cref="LoadOp.Clear"/>.</summary>
    public Vector4 Clear;

    /// <summary>Initializes a color attachment with default load/store behavior and opaque black clear color.</summary>
    public ColorAttachment()
    {
        Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
    }
}

/// <summary>A depth attachment binding for a high-level render pass.</summary>
public struct DepthAttachment
{
    /// <summary>The destination depth target.</summary>
    public RenderTarget Target;

    /// <summary>The operation performed on depth contents at pass begin.</summary>
    public LoadOp Load;

    /// <summary>The operation performed on depth contents at pass end.</summary>
    public StoreOp Store;

    /// <summary>The clear depth value when <see cref="Load"/> is <see cref="LoadOp.Clear"/>.</summary>
    public float Clear;

    /// <summary>Initializes a depth attachment with default clear depth 1.0.</summary>
    public DepthAttachment()
    {
        Clear = 1.0f;
    }
}

/// <summary>A stencil attachment binding for a high-level render pass.</summary>
public struct StencilAttachment
{
    /// <summary>The destination stencil target.</summary>
    public RenderTarget Target;

    /// <summary>The operation performed on stencil contents at pass begin.</summary>
    public LoadOp Load;

    /// <summary>The operation performed on stencil contents at pass end.</summary>
    public StoreOp Store;

    /// <summary>The clear stencil value when <see cref="Load"/> is <see cref="LoadOp.Clear"/>.</summary>
    public byte Clear;
}

/// <summary>A high-level render pass attachment description.</summary>
public ref struct RenderingDesc
{
    /// <summary>The color attachments bound to the pass.</summary>
    public ReadOnlySpan<ColorAttachment> Colors;

    /// <summary>The optional depth attachment.</summary>
    public DepthAttachment Depth;

    /// <summary>The optional stencil attachment.</summary>
    public StencilAttachment Stencil;

    /// <summary>Initializes a render pass description with no attachments and a depth clear value of one.</summary>
    public RenderingDesc()
    {
        Depth = new DepthAttachment();
    }
}
