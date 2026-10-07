using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Mirrors the raw C ABI of the alco-gpu Rust cdylib
/// (<c>Src/Alco.Graphics.Native/alco-gpu/src/abi.rs</c>). Layouts must stay
/// field-order identical; bools are <see cref="uint"/>, enums are
/// <see cref="uint"/> with Alco numeric values, optionals use
/// <see cref="None"/> sentinels and arrays are (pointer, count) pairs.
/// </summary>
internal static unsafe partial class AlcoGPU
{
    /// <summary>ABI major version implemented by the native library.</summary>
    public const uint AbiMajor = 4;

    /// <summary>ABI minor version implemented by the native library.</summary>
    public const uint AbiMinor = 0;

    /// <summary>Sentinel for "no value" in optional uint fields.</summary>
    public const uint None = uint.MaxValue;

    /// <summary>Boolean true in u32 bool fields.</summary>
    public const uint True = 1;

    /// <summary>Boolean false in u32 bool fields.</summary>
    public const uint False = 0;

    /// <summary>Status codes returned by fallible native entry points.</summary>
    public static class Status
    {
        public const uint Ok = 0;
        public const uint InvalidHandle = 1;
        public const uint InvalidArgument = 2;
        public const uint Validation = 3;
        public const uint OutOfMemory = 4;
        public const uint DeviceLost = 5;
        public const uint Panic = 6;
        public const uint Unsupported = 7;
        /// <summary>A polled operation is still in flight.</summary>
        public const uint NotReady = 8;
    }

    /// <summary>Log levels crossing the native log callback (mirror of the native log level values).</summary>
    public static class LogLevel
    {
        /// <summary>Disables native log forwarding entirely.</summary>
        public const uint Off = 0;
        public const uint Error = 1;
        public const uint Warn = 2;
        public const uint Info = 3;
        public const uint Debug = 4;
        public const uint Trace = 5;
    }

    /// <summary>Backend request values (mirror of GraphicsBackend native subset).</summary>
    public static class BackendRequest
    {
        public const uint Auto = 1;
        public const uint Vulkan = 2;
        public const uint Dx12 = 3;
        public const uint Metal = 4;
    }

    /// <summary>Resolved backend values reported by <see cref="DeviceInfo"/>.</summary>
    public static class BackendResolved
    {
        public const uint Vulkan = 2;
        public const uint Dx12 = 3;
        public const uint Metal = 4;
        public const uint Gl = 5;
        public const uint Null = 6;
    }

    /// <summary>Capability bits reported by <see cref="DeviceInfo.Capabilities"/>.</summary>
    public static class Capabilities
    {
        public const ulong PassthroughShaders = 1ul << 0;
        public const ulong MetalLib = 1ul << 1;
        public const ulong MultiDrawIndirect = 1ul << 2;
        public const ulong TimestampInsidePasses = 1ul << 3;
    }

    /// <summary>Flag bits of <see cref="ShaderModuleDesc.Flags"/> (mirror of shader_module_flags).</summary>
    public static class ShaderModuleFlags
    {
        /// <summary>
        /// SPIR-V input already matches Naga's coordinate convention (Slang's direct
        /// emission): skip the GL-style Y adjustment during translation.
        /// </summary>
        public const uint SpirvAdjustedCoordinates = 1u << 0;
    }
}

/// <summary>
/// Additional ABI constants: surface tags, acquire statuses, present modes and
/// composite alpha modes (numeric values fixed by the native layer).
/// </summary>
internal static partial class AlcoGPU
{
    /// <summary>Surface creation tags (mirrors SurfaceDesc::tag).</summary>
    public static class SurfaceTag
    {
        public const uint Win32 = 0;
        public const uint MetalLayer = 1;
        public const uint Wayland = 2;
        public const uint Xcb = 3;
        public const uint Xlib = 4;
        public const uint Android = 5;
    }

    /// <summary>Acquire statuses reported by surface_get_current_texture.</summary>
    public static class AcquireStatus
    {
        public const uint SuccessOptimal = 0;
        public const uint SuccessSuboptimal = 1;
        public const uint Timeout = 2;
        public const uint Outdated = 3;
        public const uint Lost = 4;
        public const uint Error = 5;
    }

    /// <summary>Present mode values (mirrors SurfaceConfig::present_mode).</summary>
    public static class PresentMode
    {
        public const uint Fifo = 0;
        public const uint Immediate = 1;
        public const uint Mailbox = 2;
    }

    /// <summary>Composite alpha mode values (mirrors SurfaceConfig::alpha_mode).</summary>
    public static class AlphaMode
    {
        public const uint Auto = 0;
        public const uint Opaque = 1;
        public const uint PreMultiplied = 2;
        public const uint PostMultiplied = 3;
        public const uint Inherit = 4;
    }
    /// <summary>Opaque ABI3 pointer to an owned native Device wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DeviceHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public DeviceHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static DeviceHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native Buffer wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct BufferHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public BufferHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static BufferHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native Texture wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct TextureHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public TextureHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static TextureHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native TextureView wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct TextureViewHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public TextureViewHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static TextureViewHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native Sampler wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SamplerHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public SamplerHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static SamplerHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native ShaderModule wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ShaderModuleHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public ShaderModuleHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static ShaderModuleHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native BindGroupLayout wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct BindGroupLayoutHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public BindGroupLayoutHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static BindGroupLayoutHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native BindGroup wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct BindGroupHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public BindGroupHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static BindGroupHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native QuerySet wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct QuerySetHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public QuerySetHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static QuerySetHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native GraphicsPipeline wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct GraphicsPipelineHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public GraphicsPipelineHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static GraphicsPipelineHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native ComputePipeline wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ComputePipelineHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public ComputePipelineHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static ComputePipelineHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native Encoder wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct EncoderHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public EncoderHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static EncoderHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native CommandBuffer wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CommandBufferHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public CommandBufferHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static CommandBufferHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native RenderPass wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RenderPassHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public RenderPassHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static RenderPassHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native ComputePass wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ComputePassHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public ComputePassHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static ComputePassHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native BundleEncoder wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct BundleEncoderHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public BundleEncoderHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static BundleEncoderHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native RenderBundle wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RenderBundleHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public RenderBundleHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static RenderBundleHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Opaque ABI3 pointer to an owned native Surface wrapper.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SurfaceHandle
    {
        /// <summary>The native wrapper pointer; zero represents no object.</summary>
        public readonly nint Value;

        /// <summary>Wraps a native object pointer without changing its ownership.</summary>
        /// <param name="value">The native wrapper pointer.</param>
        public SurfaceHandle(nint value) => Value = value;

        /// <summary>Gets the null native pointer.</summary>
        public static SurfaceHandle Null => default;

        /// <summary>Gets whether this pointer is null.</summary>
        public bool IsNull => Value == 0;

        /// <inheritdoc />
        public override string ToString() => $"alco:0x{Value:X}";
    }

    /// <summary>Device creation descriptor (mirrors DeviceDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceDesc
    {
        /// <summary>One of <see cref="AlcoGPU.BackendRequest"/> values.</summary>
        public uint Backend;

        /// <summary><see cref="AlcoGPU.True"/> to enable validation.</summary>
        public uint Debug;

        /// <summary>Required Alco feature bits (<see cref="GPUFeatures"/>).</summary>
        public GPUFeatures RequiredFeatures;

        /// <summary>Immediate buffer (push constants) size in bytes.</summary>
        public uint PushConstantsSize;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Static device information returned at creation (mirrors DeviceInfo).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfo
    {
        /// <summary>One of <see cref="AlcoGPU.BackendResolved"/> values.</summary>
        public uint Backend;

        /// <summary>Borrowed adapter name, valid until device destroy.</summary>
        public byte* AdapterName;

        /// <summary>Adapter vendor identifier.</summary>
        public uint Vendor;
        /// <summary>Adapter device identifier.</summary>
        public uint Device;

        /// <summary>Supported Alco feature bits (<see cref="GPUFeatures"/>).</summary>
        public GPUFeatures SupportedFeatures;

        /// <summary>Capability bits (<see cref="AlcoGPU.Capabilities"/>).</summary>
        public ulong Capabilities;

        /// <summary>Maximum number of bind groups.</summary>
        public uint MaxBindGroups;
        /// <summary>Maximum immediate data size in bytes.</summary>
        public uint MaxImmediateSize;

        /// <summary>Queue timestamp period in nanoseconds (1.0 when unsupported).</summary>
        public float TimestampPeriodNs;
    }

    /// <summary>One queued device message (mirrors DeviceMessage).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceMessage
    {
        /// <summary>0 = error, 1 = warning, 2 = info.</summary>
        public uint Severity;

        /// <summary>0 = generic, 1 = device lost.</summary>
        public uint Kind;

        /// <summary>Borrowed message, valid until the next pop on this device.</summary>
        public byte* Message;
    }

    /// <summary>Build information (mirrors BuildInfo).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BuildInfo
    {
        /// <summary>Numeric wgpu version: (major &lt;&lt; 16) | minor.</summary>
        public uint WgpuVersion;

        /// <summary>Borrowed build identifier string (process lifetime).</summary>
        public byte* BuildId;
    }

    /// <summary>Last-error record (mirrors ErrorInfo).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ErrorInfo
    {
        /// <summary>Matching status code for the failed call.</summary>
        public uint Status;

        /// <summary>Borrowed message, valid until the next failure on this thread.</summary>
        public byte* Message;
    }

    /// <summary>Buffer creation descriptor (mirrors BufferDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferDesc
    {
        /// <summary>Size in bytes.</summary>
        public ulong Size;

        /// <summary><see cref="BufferUsage"/> bits.</summary>
        public BufferUsage Usage;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Texture creation descriptor (mirrors TextureDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TextureDesc
    {
        /// <summary><see cref="TextureDimension"/> value.</summary>
        public TextureDimension Dimension;

        /// <summary><see cref="PixelFormat"/> value.</summary>
        public PixelFormat Format;

        /// <summary><see cref="TextureUsage"/> bits.</summary>
        public TextureUsage Usage;

        public uint Width;
        public uint Height;
        public uint DepthOrArrayLayers;
        public uint MipLevelCount;
        public uint SampleCount;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Texture introspection (mirrors TextureInfo).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TextureInfo
    {
        public uint Width;
        public uint Height;
        public uint DepthOrArrayLayers;
        public uint MipLevelCount;

        /// <summary><see cref="PixelFormat"/> value.</summary>
        public PixelFormat Format;
    }

    /// <summary>Texture view creation descriptor (mirrors TextureViewDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TextureViewDesc
    {
        /// <summary><see cref="TextureViewDimension"/> value.</summary>
        public TextureViewDimension Dimension;
        /// <summary>First mip level included in the view.</summary>
        public uint BaseMipLevel;
        /// <summary>Mip level count; zero selects the remaining view range.</summary>
        public uint MipLevelCount;
        /// <summary>First array layer included in the view.</summary>
        public uint BaseArrayLayer;
        /// <summary>Array layer count; zero selects the remaining range.</summary>
        public uint ArrayLayerCount;

        /// <summary><see cref="TextureAspect"/> value.</summary>
        public TextureAspect Aspect;

        /// <summary><see cref="PixelFormat"/> value; <see cref="PixelFormat.Undefined"/> keeps the texture format.</summary>
        public PixelFormat Format;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Sampler creation descriptor (mirrors SamplerDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SamplerDesc
    {
        /// <summary><see cref="FilterMode"/> value.</summary>
        public FilterMode MinFilter;

        /// <summary><see cref="FilterMode"/> value.</summary>
        public FilterMode MagFilter;

        /// <summary><see cref="FilterMode"/> value.</summary>
        public FilterMode MipmapFilter;

        /// <summary><see cref="AddressMode"/> value for U.</summary>
        public AddressMode AddressU;
        /// <summary><see cref="AddressMode"/> value for V.</summary>
        public AddressMode AddressV;
        /// <summary><see cref="AddressMode"/> value for W.</summary>
        public AddressMode AddressW;

        public float LodMinClamp;
        public float LodMaxClamp;

        /// <summary><see cref="CompareFunction"/> value; <see cref="CompareFunction.Undefined"/> disables comparison.</summary>
        public CompareFunction Compare;

        /// <summary>Max anisotropy (1 = disabled).</summary>
        public ushort MaxAnisotropy;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Shader module creation descriptor (mirrors ShaderModuleDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderModuleDesc
    {
        /// <summary><see cref="ShaderLanguage"/> value (SpirV/Wgsl/Dxil/Msl/MetalLib).</summary>
        public ShaderLanguage Language;

        /// <summary>Shader bytecode/data.</summary>
        public byte* Data;

        /// <summary>Byte count (SPIR-V is divided by 4 natively into dwords).</summary>
        public uint Size;

        /// <summary>NUL-terminated UTF-8 default entry point (may be null).</summary>
        public byte* EntryPoint;

        /// <summary>Declared workgroup width for passthrough shaders.</summary>
        public uint WorkgroupX;
        /// <summary>Declared workgroup height for passthrough shaders.</summary>
        public uint WorkgroupY;
        /// <summary>Declared workgroup depth for passthrough shaders.</summary>
        public uint WorkgroupZ;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;

        /// <summary><see cref="ShaderModuleFlags"/> bit set; unknown bits are ignored.</summary>
        public uint Flags;

        /// <summary>TRUE to consume the bytes through passthrough, FALSE to consume them through Naga translation (mirrors passthrough).</summary>
        public uint Passthrough;
    }

    /// <summary>One bind group layout entry (mirrors BindGroupLayoutEntry).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BindGroupLayoutEntry
    {
        /// <summary>Shader binding index.</summary>
        public uint Binding;

        /// <summary><see cref="ShaderStage"/> visibility bits.</summary>
        public ShaderStage Visibility;

        /// <summary><see cref="BindingType"/> value.</summary>
        public BindingType Type;

        /// <summary>Sampler bindings: 0 filtering, 1 non-filtering, 2 comparison.</summary>
        public uint SamplerKind;

        /// <summary>Texture bindings: <see cref="TextureSampleType"/> value.</summary>
        public TextureSampleType TextureSampleType;

        /// <summary>Texture/storage bindings: <see cref="TextureViewDimension"/> value.</summary>
        public TextureViewDimension ViewDimension;

        /// <summary>Storage textures: <see cref="AccessMode"/> value.</summary>
        public AccessMode StorageAccess;

        /// <summary>Storage textures: <see cref="PixelFormat"/> value.</summary>
        public PixelFormat StorageFormat;
    }

    /// <summary>Bind group layout descriptor (mirrors BindGroupLayoutDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BindGroupLayoutDesc
    {
        public BindGroupLayoutEntry* Entries;
        public uint EntryCount;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>One bind group entry (mirrors BindGroupEntry).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BindGroupEntry
    {
        /// <summary>Shader binding index.</summary>
        public uint Binding;

        /// <summary>Buffer, texture-view or sampler wrapper pointer.</summary>
        public nint Resource;

        /// <summary>Byte offset within a bound buffer.</summary>
        public ulong Offset;
        /// <summary>Byte size of the buffer or bound range; zero selects the remaining range.</summary>
        public ulong Size;

        /// <summary>Resource kind: 0 buffer, 1 texture view, 2 sampler. Determines the
        /// concrete wrapper type behind the heterogeneous resource pointer.</summary>
        public uint Kind;
    }

    /// <summary>Bind group descriptor (mirrors BindGroupDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BindGroupDesc
    {
        public BindGroupLayoutHandle Layout;
        public BindGroupEntry* Entries;
        public uint EntryCount;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>One vertex attribute (mirrors VertexElement).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VertexElement
    {
        /// <summary>Shader vertex-input location.</summary>
        public uint Location;
        /// <summary>Byte offset of this attribute within a vertex.</summary>
        public uint Offset;

        /// <summary><see cref="VertexFormat"/> value.</summary>
        public VertexFormat Format;
    }

    /// <summary>One vertex buffer layout (mirrors VertexLayout).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VertexLayout
    {
        /// <summary>Byte stride between vertices.</summary>
        public uint Stride;

        /// <summary><see cref="VertexStepMode"/> value.</summary>
        public VertexStepMode StepMode;

        public VertexElement* Elements;
        public uint ElementCount;
    }

    /// <summary>Blend factors+operation for one channel (mirrors BlendComponent).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendComponent
    {
        /// <summary><see cref="BlendFactor"/> value (source).</summary>
        public BlendFactor SrcFactor;
        /// <summary><see cref="BlendFactor"/> value (destination).</summary>
        public BlendFactor DstFactor;

        /// <summary><see cref="BlendOperation"/> value.</summary>
        public BlendOperation Operation;
    }

    /// <summary>Blend state for color+alpha channels (mirrors BlendState).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendState
    {
        public BlendComponent Color;
        public BlendComponent Alpha;
    }

    /// <summary>Stencil face state (mirrors StencilFace).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StencilFace
    {
        /// <summary><see cref="CompareFunction"/> value (0 = Always).</summary>
        public CompareFunction Compare;

        /// <summary><see cref="StencilOperation"/> value (stencil fail).</summary>
        public StencilOperation StencilFailOp;
        /// <summary><see cref="StencilOperation"/> value (depth fail).</summary>
        public StencilOperation DepthFailOp;
        /// <summary><see cref="StencilOperation"/> value (pass).</summary>
        public StencilOperation PassOp;
    }

    /// <summary>Depth-stencil state (mirrors DepthStencilState).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DepthStencilState
    {
        /// <summary>Boolean u32; depth writes enabled.</summary>
        public uint DepthWriteEnabled;

        /// <summary>Boolean u32; ignored by wgpu (no depth bounds test).</summary>
        public uint DepthBoundsTestEnabled;

        /// <summary><see cref="CompareFunction"/> value; <see cref="CompareFunction.Undefined"/> disables the depth test.</summary>
        public CompareFunction DepthCompare;

        public StencilFace Front;
        public StencilFace Back;
        public uint StencilReadMask;
        public uint StencilWriteMask;
    }

    /// <summary>Graphics pipeline descriptor (mirrors GraphicsPipelineDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GraphicsPipelineDesc
    {
        public BindGroupLayoutHandle* BindGroupLayouts;
        public uint BindGroupLayoutCount;

        public ShaderModuleHandle VertexModule;

        /// <summary>NUL-terminated UTF-8 vertex entry point.</summary>
        public byte* VertexEntry;

        public ShaderModuleHandle FragmentModule;

        /// <summary>NUL-terminated UTF-8 fragment entry point.</summary>
        public byte* FragmentEntry;

        public VertexLayout* VertexLayouts;
        public uint VertexLayoutCount;

        /// <summary><see cref="FillMode"/> value (wireframe unsupported by wgpu).</summary>
        public FillMode FillMode;

        /// <summary><see cref="CullMode"/> value.</summary>
        public CullMode CullMode;

        /// <summary><see cref="FrontFace"/> value.</summary>
        public FrontFace FrontFace;

        public BlendState Blend;
        public DepthStencilState DepthStencil;

        /// <summary><see cref="PixelFormat"/> value; <see cref="AlcoGPU.None"/> when unused.</summary>
        public uint DepthStencilFormat;

        /// <summary><see cref="PrimitiveTopology"/> value.</summary>
        public PrimitiveTopology Topology;

        /// <summary><see cref="PixelFormat"/> values, one per color target.</summary>
        public uint* ColorFormats;
        public uint ColorFormatCount;

        /// <summary>Active color writes; <see cref="AlcoGPU.None"/> writes all.</summary>
        public uint FragmentOutputCount;

        /// <summary>Immediate (push constant) size in bytes.</summary>
        public uint ImmediateSize;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Compute pipeline descriptor (mirrors ComputePipelineDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ComputePipelineDesc
    {
        public BindGroupLayoutHandle* BindGroupLayouts;
        public uint BindGroupLayoutCount;

        public ShaderModuleHandle ComputeModule;

        /// <summary>NUL-terminated UTF-8 compute entry point.</summary>
        public byte* ComputeEntry;

        /// <summary>Immediate (push constant) size in bytes.</summary>
        public uint ImmediateSize;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>One color attachment of a render pass (mirrors ColorAttachment).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorAttachment
    {
        public TextureViewHandle View;
        /// <summary>Resolve target view; a null handle when unused.</summary>
        public TextureViewHandle ResolveView;

        /// <summary><see cref="AttachmentLoadOp"/> value.</summary>
        public uint LoadOp;

        /// <summary>0 store, 1 discard.</summary>
        public uint StoreOp;

        /// <summary>RGBA clear value used by the Clear load operation.</summary>
        public fixed float ClearColor[4];
    }

    /// <summary>Depth-stencil attachment of a render pass (mirrors DepthStencilAttachment).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DepthStencilAttachment
    {
        public TextureViewHandle View;

        /// <summary><see cref="AttachmentLoadOp"/> value; <see cref="AlcoGPU.None"/> = read-only.</summary>
        public uint DepthLoadOp;

        /// <summary>0 store, 1 discard; <see cref="AlcoGPU.None"/> = read-only.</summary>
        public uint DepthStoreOp;

        public float DepthClear;

        /// <summary><see cref="AttachmentLoadOp"/> value; <see cref="AlcoGPU.None"/> = read-only.</summary>
        public uint StencilLoadOp;

        /// <summary>0 store, 1 discard; <see cref="AlcoGPU.None"/> = read-only.</summary>
        public uint StencilStoreOp;

        public uint StencilClear;
    }

    /// <summary>Pass timestamp writes (mirrors TimestampWrites).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TimestampWrites
    {
        public QuerySetHandle QuerySet;

        /// <summary>Query index; <see cref="AlcoGPU.None"/> skips the write.</summary>
        public uint BeginningIndex;

        /// <summary>Query index; <see cref="AlcoGPU.None"/> skips the write.</summary>
        public uint EndIndex;
    }

    /// <summary>Render pass descriptor (mirrors RenderPassDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPassDesc
    {
        public ColorAttachment* ColorAttachments;
        public uint ColorAttachmentCount;
        public DepthStencilAttachment* DepthStencil;
        public TimestampWrites* TimestampWrites;
    }

    /// <summary>Copy data layout (mirrors CopyLayout).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyLayout
    {
        /// <summary>Byte offset of the first copied texel in the buffer.</summary>
        public ulong Offset;

        /// <summary>Bytes per row; <see cref="AlcoGPU.None"/> = tightly packed.</summary>
        public uint BytesPerRow;

        /// <summary>Rows per image; <see cref="AlcoGPU.None"/> = tightly packed.</summary>
        public uint RowsPerImage;
    }

    /// <summary>Copy origin (mirrors Origin3D).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Origin3D
    {
        public uint X;
        public uint Y;
        public uint Z;
    }

    /// <summary>Copy extent (mirrors Extent3D).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Extent3D
    {
        public uint Width;
        public uint Height;
        public uint DepthOrArrayLayers;
    }

    /// <summary>Texture subresource range for clears (mirrors SubresourceRange).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SubresourceRange
    {
        /// <summary><see cref="TextureAspect"/> value (None reads as all).</summary>
        public TextureAspect Aspect;
        /// <summary>First cleared mip level.</summary>
        public uint BaseMipLevel;
        /// <summary>Cleared mip level count; zero or <see cref="AlcoGPU.None"/> = the rest.</summary>
        public uint MipLevelCount;
        /// <summary>First cleared array layer.</summary>
        public uint BaseArrayLayer;
        /// <summary>Cleared array layer count; zero or <see cref="AlcoGPU.None"/> = the rest.</summary>
        public uint ArrayLayerCount;
    }

    /// <summary>Render bundle encoder descriptor (mirrors BundleEncoderDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BundleEncoderDesc
    {
        /// <summary><see cref="PixelFormat"/> values, one per color target.</summary>
        public uint* ColorFormats;
        public uint ColorFormatCount;

        /// <summary><see cref="PixelFormat"/> value; <see cref="AlcoGPU.None"/> when unused.</summary>
        public uint DepthStencilFormat;

        /// <summary>Boolean u32 flags for read-only depth/stencil.</summary>
        public uint DepthReadOnly;
        public uint StencilReadOnly;

        public uint SampleCount;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Surface creation descriptor (mirrors SurfaceDesc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SurfaceDesc
    {
        /// <summary>One of <see cref="AlcoGPU.SurfaceTag"/> values.</summary>
        public uint Tag;

        /// <summary>Platform window/surface handle.</summary>
        public ulong Handle;

        /// <summary>Platform display/connection handle (X11/Wayland).</summary>
        public ulong Display;

        /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
        public byte* Name;
    }

    /// <summary>Surface capabilities (mirrors SurfaceCapabilities) with inline arrays.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SurfaceCapabilities
    {
        public fixed uint Formats[64];
        public uint FormatCount;
        public fixed uint PresentModes[8];
        public uint PresentModeCount;
    }

    /// <summary>Surface configuration (mirrors SurfaceConfig).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SurfaceConfig
    {
        /// <summary><see cref="TextureUsage"/> bits.</summary>
        public TextureUsage Usage;

        /// <summary><see cref="PixelFormat"/> value.</summary>
        public PixelFormat Format;

        public uint Width;
        public uint Height;

        /// <summary>One of <see cref="AlcoGPU.PresentMode"/> values.</summary>
        public uint PresentMode;

        /// <summary>One of <see cref="AlcoGPU.AlphaMode"/> values.</summary>
        public uint AlphaMode;

        /// <summary>Desired maximum number of frames in flight, clamped to at least one.</summary>
        public uint DesiredFrameLatency;
    }
}
