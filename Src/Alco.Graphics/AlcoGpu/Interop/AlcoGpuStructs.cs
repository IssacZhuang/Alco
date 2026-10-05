using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Mirrors the raw C ABI of the alco-gpu Rust cdylib
/// (<c>Src/Alco.Graphics.Native/alco-gpu/src/abi.rs</c>). Layouts must stay
/// field-order identical; bools are <see cref="uint"/>, enums are
/// <see cref="uint"/> with Alco numeric values, optionals use
/// <see cref="AlcoNone"/> sentinels and arrays are (pointer, count) pairs.
/// </summary>
internal static unsafe partial class AlcoGpuAbi
{
    /// <summary>ABI major version implemented by the native library.</summary>
    public const uint AbiMajor = 1;

    /// <summary>ABI minor version implemented by the native library.</summary>
    public const uint AbiMinor = 3;

    /// <summary>Sentinel for "no value" in optional uint fields.</summary>
    public const uint AlcoNone = uint.MaxValue;

    /// <summary>Boolean true in u32 bool fields.</summary>
    public const uint AlcoTrue = 1;

    /// <summary>Boolean false in u32 bool fields.</summary>
    public const uint AlcoFalse = 0;

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

    /// <summary>Backend request values (mirror of GraphicsBackend native subset).</summary>
    public static class BackendRequest
    {
        public const uint Auto = 1;
        public const uint Vulkan = 2;
        public const uint Dx12 = 3;
        public const uint Metal = 4;
    }

    /// <summary>Resolved backend values reported by <see cref="AlcoDeviceInfo"/>.</summary>
    public static class BackendResolved
    {
        public const uint Vulkan = 2;
        public const uint Dx12 = 3;
        public const uint Metal = 4;
        public const uint Gl = 5;
        public const uint Null = 6;
    }

    /// <summary>Capability bits reported by <see cref="AlcoDeviceInfo.Caps"/>.</summary>
    public static class Caps
    {
        public const ulong PassthroughShaders = 1ul << 0;
        public const ulong MetalLib = 1ul << 1;
        public const ulong MultiDrawIndirect = 1ul << 2;
        public const ulong TimestampInsidePasses = 1ul << 3;
    }
}

/// <summary>Opaque native object handle (index + generation).</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct AlcoHandle
{
    public readonly ulong Value;

    public AlcoHandle(ulong value)
    {
        Value = value;
    }

    public static AlcoHandle Null => default;

    public bool IsNull => Value == 0;

    /// <inheritdoc />
    public override string ToString() => $"alco:0x{Value:X16}";
}

/// <summary>Device creation descriptor (mirrors AlcoDeviceDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoDeviceDesc
{
    /// <summary>One of <see cref="AlcoGpuAbi.BackendRequest"/> values.</summary>
    public uint Backend;

    /// <summary><see cref="AlcoGpuAbi.AlcoTrue"/> to enable validation.</summary>
    public uint Debug;

    /// <summary>Required Alco feature bits (mirrors <see cref="GPUFeatures"/>).</summary>
    public ulong RequiredFeatures;

    /// <summary>Immediate buffer (push constants) size in bytes.</summary>
    public uint PushConstantsSize;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Static device information returned at creation (mirrors AlcoDeviceInfo).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoDeviceInfo
{
    /// <summary>One of <see cref="AlcoGpuAbi.BackendResolved"/> values.</summary>
    public uint Backend;

    /// <summary>Borrowed adapter name, valid until device destroy.</summary>
    public byte* AdapterName;

    public uint Vendor;
    public uint Device;

    /// <summary>Supported Alco feature bits.</summary>
    public ulong SupportedFeatures;

    /// <summary>Capability bits (<see cref="AlcoGpuAbi.Caps"/>).</summary>
    public ulong Caps;

    public uint MaxBindGroups;
    public uint MaxImmediateSize;

    /// <summary>Queue timestamp period in nanoseconds (1.0 when unsupported).</summary>
    public float TimestampPeriodNs;
}

/// <summary>One queued device message (mirrors AlcoDeviceMessage).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoDeviceMessage
{
    /// <summary>0 = error, 1 = warning, 2 = info.</summary>
    public uint Severity;

    /// <summary>0 = generic, 1 = device lost.</summary>
    public uint Kind;

    /// <summary>Borrowed message, valid until the next pop on this device.</summary>
    public byte* Message;
}

/// <summary>Build information (mirrors AlcoBuildInfo).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBuildInfo
{
    /// <summary>Numeric wgpu version: (major &lt;&lt; 16) | minor.</summary>
    public uint WgpuVersion;

    /// <summary>Borrowed build identifier string (process lifetime).</summary>
    public byte* AlcoBuild;
}

/// <summary>Last-error record (mirrors AlcoErrorInfo).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoErrorInfo
{
    /// <summary>Matching status code for the failed call.</summary>
    public uint Status;

    /// <summary>Borrowed message, valid until the next alco call on this thread.</summary>
    public byte* Message;
}

/// <summary>
/// Additional ABI constants: surface tags, acquire statuses, present modes and
/// composite alpha modes (numeric values fixed by the native layer).
/// </summary>
internal static partial class AlcoGpuAbi
{
    /// <summary>Surface creation tags (mirrors AlcoSurfaceDesc::tag).</summary>
    public static class SurfaceTag
    {
        public const uint Win32 = 0;
        public const uint MetalLayer = 1;
        public const uint Wayland = 2;
        public const uint Xcb = 3;
        public const uint Xlib = 4;
        public const uint Android = 5;
    }

    /// <summary>Acquire statuses reported by alco_surface_get_current_texture.</summary>
    public static class AcquireStatus
    {
        public const uint SuccessOptimal = 0;
        public const uint SuccessSuboptimal = 1;
        public const uint Timeout = 2;
        public const uint Outdated = 3;
        public const uint Lost = 4;
        public const uint Error = 5;
    }

    /// <summary>Present mode values (mirrors AlcoSurfaceConfig::present_mode).</summary>
    public static class PresentModeAbi
    {
        public const uint Fifo = 0;
        public const uint Immediate = 1;
        public const uint Mailbox = 2;
    }

    /// <summary>Composite alpha mode values (mirrors AlcoSurfaceConfig::alpha_mode).</summary>
    public static class AlphaModeAbi
    {
        public const uint Auto = 0;
        public const uint Opaque = 1;
        public const uint PreMultiplied = 2;
        public const uint PostMultiplied = 3;
        public const uint Inherit = 4;
    }
}

/// <summary>Buffer creation descriptor (mirrors AlcoBufferDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBufferDesc
{
    /// <summary>Size in bytes.</summary>
    public ulong Size;

    /// <summary><see cref="BufferUsage"/> bits.</summary>
    public uint Usage;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Texture creation descriptor (mirrors AlcoTextureDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoTextureDesc
{
    /// <summary><see cref="TextureDimension"/> value.</summary>
    public uint Dimension;

    /// <summary><see cref="PixelFormat"/> value.</summary>
    public uint Format;

    /// <summary><see cref="TextureUsage"/> bits.</summary>
    public uint Usage;

    public uint Width;
    public uint Height;
    public uint DepthOrArrayLayers;
    public uint MipLevelCount;
    public uint SampleCount;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Texture introspection (mirrors AlcoTextureInfo).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoTextureInfo
{
    public uint Width;
    public uint Height;
    public uint DepthOrArrayLayers;
    public uint MipLevelCount;

    /// <summary><see cref="PixelFormat"/> value.</summary>
    public uint Format;
}

/// <summary>Texture view creation descriptor (mirrors AlcoTextureViewDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoTextureViewDesc
{
    /// <summary><see cref="TextureViewDimension"/> value.</summary>
    public uint Dimension;
    public uint BaseMipLevel;
    public uint MipLevelCount;
    public uint BaseArrayLayer;
    public uint ArrayLayerCount;

    /// <summary><see cref="TextureAspect"/> value.</summary>
    public uint Aspect;

    /// <summary><see cref="PixelFormat"/> value (0 keeps the texture format).</summary>
    public uint Format;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Sampler creation descriptor (mirrors AlcoSamplerDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoSamplerDesc
{
    /// <summary><see cref="FilterMode"/> value.</summary>
    public uint MinFilter;

    /// <summary><see cref="FilterMode"/> value.</summary>
    public uint MagFilter;

    /// <summary><see cref="FilterMode"/> value.</summary>
    public uint MipmapFilter;

    /// <summary><see cref="AddressMode"/> values for U/V/W.</summary>
    public uint AddressU;
    public uint AddressV;
    public uint AddressW;

    public float LodMinClamp;
    public float LodMaxClamp;

    /// <summary><see cref="CompareFunction"/> value; 0 disables comparison.</summary>
    public uint Compare;

    /// <summary>Max anisotropy (1 = disabled).</summary>
    public ushort MaxAnisotropy;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Shader module creation descriptor (mirrors AlcoShaderModuleDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoShaderModuleDesc
{
    /// <summary><see cref="ShaderLanguage"/> value (SpirV/Wgsl/Dxil/Msl/MetalLib).</summary>
    public uint Language;

    /// <summary>Shader bytecode/data.</summary>
    public byte* Data;

    /// <summary>Byte count (SPIR-V is divided by 4 natively into dwords).</summary>
    public uint Size;

    /// <summary>NUL-terminated UTF-8 default entry point (may be null).</summary>
    public byte* EntryPoint;

    public uint WorkgroupX;
    public uint WorkgroupY;
    public uint WorkgroupZ;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>One bind group layout entry (mirrors AlcoBindGroupLayoutEntry).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBindGroupLayoutEntry
{
    public uint Binding;

    /// <summary><see cref="ShaderStage"/> visibility bits.</summary>
    public uint Visibility;

    /// <summary><see cref="BindingType"/> value.</summary>
    public uint Type;

    /// <summary>Sampler bindings: 0 filtering, 1 non-filtering, 2 comparison.</summary>
    public uint SamplerKind;

    /// <summary>Texture bindings: <see cref="TextureSampleType"/> value.</summary>
    public uint TextureSampleType;

    /// <summary>Texture/storage bindings: <see cref="TextureViewDimension"/> value.</summary>
    public uint ViewDimension;

    /// <summary>Storage textures: <see cref="AccessMode"/> bits.</summary>
    public uint StorageAccess;

    /// <summary>Storage textures: <see cref="PixelFormat"/> value.</summary>
    public uint StorageFormat;
}

/// <summary>Bind group layout descriptor (mirrors AlcoBindGroupLayoutDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBindGroupLayoutDesc
{
    public AlcoBindGroupLayoutEntry* Entries;
    public uint EntryCount;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>One bind group entry (mirrors AlcoBindGroupEntry).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBindGroupEntry
{
    public uint Binding;

    /// <summary>Buffer, texture-view or sampler handle.</summary>
    public AlcoHandle Resource;

    public ulong Offset;
    public ulong Size;

    /// <summary>Resource kind: 0 buffer, 1 texture view, 2 sampler. Handles from
    /// different per-type tables may collide numerically, so the kind must be explicit.</summary>
    public uint Kind;
}

/// <summary>Bind group descriptor (mirrors AlcoBindGroupDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBindGroupDesc
{
    public AlcoHandle Layout;
    public AlcoBindGroupEntry* Entries;
    public uint EntryCount;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>One vertex attribute (mirrors AlcoVertexElement).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoVertexElement
{
    public uint Location;
    public uint Offset;

    /// <summary><see cref="VertexFormat"/> value.</summary>
    public uint Format;
}

/// <summary>One vertex buffer layout (mirrors AlcoVertexLayout).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoVertexLayout
{
    public uint Stride;

    /// <summary><see cref="VertexStepMode"/> value.</summary>
    public uint StepMode;

    public AlcoVertexElement* Elements;
    public uint ElementCount;
}

/// <summary>Blend factors+operation for one channel (mirrors AlcoBlendComponent).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBlendComponent
{
    /// <summary><see cref="BlendFactor"/> values.</summary>
    public uint SrcFactor;
    public uint DstFactor;

    /// <summary><see cref="BlendOperation"/> value.</summary>
    public uint Operation;
}

/// <summary>Blend state for color+alpha channels (mirrors AlcoBlendState).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBlendState
{
    public AlcoBlendComponent Color;
    public AlcoBlendComponent Alpha;
}

/// <summary>Stencil face state (mirrors AlcoStencilFace).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoStencilFace
{
    /// <summary><see cref="CompareFunction"/> value (0 = Always).</summary>
    public uint Compare;

    /// <summary><see cref="StencilOperation"/> values.</summary>
    public uint StencilFailOp;
    public uint DepthFailOp;
    public uint PassOp;
}

/// <summary>Depth-stencil state (mirrors AlcoDepthStencilState).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoDepthStencilState
{
    /// <summary>Boolean u32; depth writes enabled.</summary>
    public uint DepthWriteEnabled;

    /// <summary>Boolean u32; ignored by wgpu (no depth bounds test).</summary>
    public uint DepthBoundsTestEnabled;

    /// <summary><see cref="CompareFunction"/> value (0 disables depth test).</summary>
    public uint DepthCompare;

    public AlcoStencilFace Front;
    public AlcoStencilFace Back;
    public uint StencilReadMask;
    public uint StencilWriteMask;
}

/// <summary>Graphics pipeline descriptor (mirrors AlcoGraphicsPipelineDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoGraphicsPipelineDesc
{
    public AlcoHandle* BindGroupLayouts;
    public uint BindGroupLayoutCount;

    public AlcoHandle VertexModule;

    /// <summary>NUL-terminated UTF-8 vertex entry point.</summary>
    public byte* VertexEntry;

    public AlcoHandle FragmentModule;

    /// <summary>NUL-terminated UTF-8 fragment entry point.</summary>
    public byte* FragmentEntry;

    public AlcoVertexLayout* VertexLayouts;
    public uint VertexLayoutCount;

    /// <summary><see cref="FillMode"/> value (wireframe unsupported by wgpu).</summary>
    public uint FillMode;

    /// <summary><see cref="CullMode"/> value.</summary>
    public uint CullMode;

    /// <summary><see cref="FrontFace"/> value.</summary>
    public uint FrontFace;

    public AlcoBlendState Blend;
    public AlcoDepthStencilState DepthStencil;

    /// <summary><see cref="PixelFormat"/> value; <see cref="AlcoGpuAbi.AlcoNone"/> when unused.</summary>
    public uint DepthStencilFormat;

    /// <summary><see cref="PrimitiveTopology"/> value.</summary>
    public uint Topology;

    /// <summary><see cref="PixelFormat"/> values, one per color target.</summary>
    public uint* ColorFormats;
    public uint ColorFormatCount;

    /// <summary>Active color writes; <see cref="AlcoGpuAbi.AlcoNone"/> writes all.</summary>
    public uint FragmentOutputCount;

    /// <summary>Immediate (push constant) size in bytes.</summary>
    public uint ImmediateSize;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Compute pipeline descriptor (mirrors AlcoComputePipelineDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoComputePipelineDesc
{
    public AlcoHandle* BindGroupLayouts;
    public uint BindGroupLayoutCount;

    public AlcoHandle ComputeModule;

    /// <summary>NUL-terminated UTF-8 compute entry point.</summary>
    public byte* ComputeEntry;

    /// <summary>Immediate (push constant) size in bytes.</summary>
    public uint ImmediateSize;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>One color attachment of a render pass (mirrors AlcoColorAttachment).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoColorAttachment
{
    public AlcoHandle View;
    public AlcoHandle ResolveView;

    /// <summary><see cref="AttachmentLoadOp"/> value.</summary>
    public uint LoadOp;

    /// <summary>0 store, 1 discard.</summary>
    public uint StoreOp;

    public fixed float ClearColor[4];
}

/// <summary>Depth-stencil attachment of a render pass (mirrors AlcoDepthStencilAttachment).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoDepthStencilAttachment
{
    public AlcoHandle View;

    /// <summary><see cref="AttachmentLoadOp"/> value; <see cref="AlcoGpuAbi.AlcoNone"/> = read-only.</summary>
    public uint DepthLoadOp;

    /// <summary>0 store, 1 discard; <see cref="AlcoGpuAbi.AlcoNone"/> = read-only.</summary>
    public uint DepthStoreOp;

    public float DepthClear;

    /// <summary><see cref="AttachmentLoadOp"/> value; <see cref="AlcoGpuAbi.AlcoNone"/> = read-only.</summary>
    public uint StencilLoadOp;

    /// <summary>0 store, 1 discard; <see cref="AlcoGpuAbi.AlcoNone"/> = read-only.</summary>
    public uint StencilStoreOp;

    public uint StencilClear;
}

/// <summary>Pass timestamp writes (mirrors AlcoTimestampWrites).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoTimestampWrites
{
    public AlcoHandle QuerySet;

    /// <summary>Query index; <see cref="AlcoGpuAbi.AlcoNone"/> skips the write.</summary>
    public uint BeginningIndex;

    /// <summary>Query index; <see cref="AlcoGpuAbi.AlcoNone"/> skips the write.</summary>
    public uint EndIndex;
}

/// <summary>Render pass descriptor (mirrors AlcoRenderPassDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoRenderPassDesc
{
    public AlcoColorAttachment* ColorAttachments;
    public uint ColorAttachmentCount;
    public AlcoDepthStencilAttachment* DepthStencil;
    public AlcoTimestampWrites* TimestampWrites;
}

/// <summary>Copy data layout (mirrors AlcoCopyLayout).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoCopyLayout
{
    public ulong Offset;

    /// <summary>Bytes per row; <see cref="AlcoGpuAbi.AlcoNone"/> = tightly packed.</summary>
    public uint BytesPerRow;

    /// <summary>Rows per image; <see cref="AlcoGpuAbi.AlcoNone"/> = tightly packed.</summary>
    public uint RowsPerImage;
}

/// <summary>Copy origin (mirrors AlcoOrigin3D).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoOrigin3D
{
    public uint X;
    public uint Y;
    public uint Z;
}

/// <summary>Copy extent (mirrors AlcoExtent3D).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoExtent3D
{
    public uint Width;
    public uint Height;
    public uint DepthOrArrayLayers;
}

/// <summary>Render bundle encoder descriptor (mirrors AlcoBundleEncoderDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoBundleEncoderDesc
{
    /// <summary><see cref="PixelFormat"/> values, one per color target.</summary>
    public uint* ColorFormats;
    public uint ColorFormatCount;

    /// <summary><see cref="PixelFormat"/> value; <see cref="AlcoGpuAbi.AlcoNone"/> when unused.</summary>
    public uint DepthStencilFormat;

    /// <summary>Boolean u32 flags for read-only depth/stencil.</summary>
    public uint DepthReadOnly;
    public uint StencilReadOnly;

    public uint SampleCount;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Surface creation descriptor (mirrors AlcoSurfaceDesc).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoSurfaceDesc
{
    /// <summary>One of <see cref="AlcoGpuAbi.SurfaceTag"/> values.</summary>
    public uint Tag;

    /// <summary>Platform window/surface handle.</summary>
    public ulong Handle;

    /// <summary>Platform display/connection handle (X11/Wayland).</summary>
    public ulong Display;

    /// <summary>NUL-terminated UTF-8 debug label (may be null).</summary>
    public byte* Name;
}

/// <summary>Surface capabilities (mirrors AlcoSurfaceCaps) with inline arrays.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AlcoSurfaceCaps
{
    public fixed uint Formats[64];
    public uint FormatCount;
    public fixed uint PresentModes[8];
    public uint PresentModeCount;
}

/// <summary>Surface configuration (mirrors AlcoSurfaceConfig).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AlcoSurfaceConfig
{
    /// <summary><see cref="TextureUsage"/> bits.</summary>
    public uint Usage;

    /// <summary><see cref="PixelFormat"/> value.</summary>
    public uint Format;

    public uint Width;
    public uint Height;

    /// <summary>One of <see cref="AlcoGpuAbi.PresentModeAbi"/> values.</summary>
    public uint PresentMode;

    /// <summary>One of <see cref="AlcoGpuAbi.AlphaModeAbi"/> values.</summary>
    public uint AlphaMode;

    public uint DesiredFrameLatency;
}
