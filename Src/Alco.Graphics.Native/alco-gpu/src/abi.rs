//! Raw C ABI contract types (see `Docs/AlcoGpuAbi.md`).
//!
//! Layout contract: every struct here is `#[repr(C)]`; consumers mirror them
//! field-by-field. Booleans are `u32` (`TRUE = 1`), enum values are `u32`
//! (non-flags enums are defined at the bottom of this module and materialized
//! after validation in `convert.rs`), optional values use `NONE = u32::MAX`
//! sentinels, arrays are `(pointer, count)` pairs, and strings are
//! NUL-terminated UTF-8.

/// ABI major version. Increment on any breaking layout/semantic change.
/// v3: entry-point symbols lost the `alco_` prefix (parent-first naming).
/// v4: ShaderModuleDesc gained the caller-owned `passthrough` consumption switch.
pub const ABI_MAJOR: u32 = 4;
/// ABI minor version. Increment on additive changes.
pub const ABI_MINOR: u32 = 0;

/// Sentinel for "no value" in optional `u32` fields.
pub const NONE: u32 = u32::MAX;
/// Boolean true in `u32` bool fields.
pub const TRUE: u32 = 1;
/// Boolean false in `u32` bool fields.
pub const FALSE: u32 = 0;

/// Status code returned by every fallible ABI entry point.
#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Status(pub u32);

impl Status {
    pub const OK: Self = Self(0);
    pub const INVALID_HANDLE: Self = Self(1);
    pub const INVALID_ARGUMENT: Self = Self(2);
    pub const VALIDATION: Self = Self(3);
    pub const OUT_OF_MEMORY: Self = Self(4);
    pub const DEVICE_LOST: Self = Self(5);
    pub const PANIC: Self = Self(6);
    pub const UNSUPPORTED: Self = Self(7);
    /// A polled operation is still in flight; poll again later.
    pub const NOT_READY: Self = Self(8);

    #[inline]
    pub fn is_ok(self) -> bool {
        self.0 == 0
    }
}

/// Owned device pointer; children retain its cleanup context independently.
pub type DeviceHandle = crate::handle::Handle<crate::device::DeviceOwner>;
/// Owned buffer pointer.
pub type BufferHandle = crate::handle::Handle<crate::objects::BufferObj>;
/// Owned regular or acquired texture pointer.
pub type TextureHandle = crate::handle::Handle<crate::objects::TextureObj>;
/// Owned texture view pointer.
pub type TextureViewHandle = crate::handle::Handle<crate::objects::TextureViewObj>;
/// Owned sampler pointer.
pub type SamplerHandle = crate::handle::Handle<crate::objects::SamplerObj>;
/// Owned shader module pointer.
pub type ShaderModuleHandle = crate::handle::Handle<crate::objects::ShaderModuleObj>;
/// Owned bind-group layout pointer.
pub type BindGroupLayoutHandle = crate::handle::Handle<crate::objects::BindGroupLayoutObj>;
/// Owned bind-group pointer.
pub type BindGroupHandle = crate::handle::Handle<crate::objects::BindGroupObj>;
/// Owned timestamp query set pointer.
pub type QuerySetHandle = crate::handle::Handle<crate::objects::QuerySetObj>;
/// Owned graphics pipeline pointer.
pub type GraphicsPipelineHandle = crate::handle::Handle<crate::pipeline::GraphicsPipelineObj>;
/// Owned compute pipeline pointer.
pub type ComputePipelineHandle = crate::handle::Handle<crate::pipeline::ComputePipelineObj>;
/// Owned command encoder pointer, consumed by finish or destroy.
pub type EncoderHandle = crate::handle::Handle<crate::commands::EncoderObj>;
/// Owned command buffer pointer, consumed by submit or destroy.
pub type CommandBufferHandle = crate::handle::Handle<crate::commands::CommandBufferObj>;
/// Caller-exclusive render pass pointer, consumed by end or release.
pub type RenderPassHandle = crate::handle::Handle<crate::commands::RenderPassObj>;
/// Caller-exclusive compute pass pointer, consumed by end or release.
pub type ComputePassHandle = crate::handle::Handle<crate::commands::ComputePassObj>;
/// Caller-exclusive bundle encoder pointer, consumed by finish or destroy.
pub type BundleEncoderHandle = crate::handle::Handle<crate::commands::BundleEncoderObj>;
/// Owned render bundle pointer.
pub type RenderBundleHandle = crate::handle::Handle<crate::commands::RenderBundleObj>;
/// Owned surface reference, released only by surface destroy.
pub type SurfaceHandle = crate::handle::Handle<crate::surface::SurfaceObj>;

/// `Backend` values: request values plus `RESOLVED_*` values reported by
/// `DeviceInfo`.
pub mod backend {
    pub const AUTO: u32 = 1;
    pub const VULKAN: u32 = 2;
    pub const DX12: u32 = 3;
    pub const METAL: u32 = 4;
    /// Resolved backend values reported by `DeviceInfo`.
    pub const RESOLVED_VULKAN: u32 = 2;
    pub const RESOLVED_DX12: u32 = 3;
    pub const RESOLVED_METAL: u32 = 4;
    pub const RESOLVED_GL: u32 = 5;
    pub const RESOLVED_NULL: u32 = 6;
}

/// Device capability bits.
pub mod capabilities {
    /// Adapter/device supports PASSTHROUGH_SHADERS (required for DXIL/MSL/MetalLib).
    pub const PASSTHROUGH_SHADERS: u64 = 1 << 0;
    /// MetalLib passthrough source accepted by this build (Apple platforms).
    pub const METALLIB: u64 = 1 << 1;
    /// MULTI_DRAW_INDIRECT native extension available.
    pub const MULTI_DRAW_INDIRECT: u64 = 1 << 2;
    /// TIMESTAMP_QUERY_INSIDE_PASSES native feature available.
    pub const TIMESTAMP_INSIDE_PASSES: u64 = 1 << 3;
}

/// `LogLevel` values for the log callback — same numeric values as
/// wgpu-native's `WGPULogLevel`.
pub mod log_level {
    /// Disables forwarding entirely (argument to `set_log_level`).
    pub const OFF: u32 = 0;
    pub const ERROR: u32 = 1;
    pub const WARN: u32 = 2;
    pub const INFO: u32 = 3;
    pub const DEBUG: u32 = 4;
    pub const TRACE: u32 = 5;
}

/// Device creation descriptor. The caller performs all feature gating and
/// passes the final required feature set.
#[repr(C)]
pub struct DeviceDesc {
    /// One of the `backend::*` request values (AUTO/VULKAN/DX12/METAL).
    pub backend: u32,
    /// TRUE to enable wgpu validation layers/logging.
    pub debug: u32,
    /// Required Alco feature bits.
    pub required_features: u64,
    /// Immediate buffer (push constants) size in bytes.
    pub push_constants_size: u32,
    /// NUL-terminated UTF-8 debug label (may be null).
    pub name: *const std::ffi::c_char,
}

/// Static device information returned at creation time. All borrowed strings
/// are owned by the device and remain valid until the device is destroyed.
#[repr(C)]
pub struct DeviceInfo {
    /// Resolved backend (`backend::RESOLVED_*`).
    pub backend: u32,
    /// NUL-terminated adapter name (borrowed from the device).
    pub adapter_name: *const std::ffi::c_char,
    pub vendor: u32,
    pub device: u32,
    /// Supported Alco feature bits (subset of `DeviceDesc::required_features` space).
    pub supported_features: u64,
    /// Capability bits (`capabilities::*`).
    pub capabilities: u64,
    pub max_bind_groups: u32,
    pub max_immediate_size: u32,
    /// Queue timestamp period in nanoseconds (1.0 when unsupported).
    pub timestamp_period_ns: f32,
}

/// One queued device message (validation error, device lost, native warning).
/// Severity mirrors the old wgpu-native log/error callback levels.
#[repr(C)]
pub struct DeviceMessage {
    /// 0 = error, 1 = warning, 2 = info.
    pub severity: u32,
    /// 0 = generic, 1 = device lost.
    pub kind: u32,
    /// NUL-terminated message (borrowed until the next `device_pop_message`).
    pub message: *const std::ffi::c_char,
}

/// Build/runtime information for `build_info`.
#[repr(C)]
pub struct BuildInfo {
    /// `(major << 16) | minor` of the wgpu-types crate linked into this binary.
    pub wgpu_version: u32,
    /// NUL-terminated alco-gpu build identifier (git hash + profile).
    pub build_id: *const std::ffi::c_char,
}

/// Last-error record for `get_last_error`.
#[repr(C)]
pub struct ErrorInfo {
    /// Matching `Status` for the failed call.
    pub status: u32,
    /// NUL-terminated message (borrowed until the next failure on this thread).
    pub message: *const std::ffi::c_char,
}

// ---------------------------------------------------------------------------
// ABI enums
//
// Non-flags enums are defined here as `#[repr(u32)]` with explicit
// discriminants, plus a `TryFrom<u32>` that rejects unknown values. ABI
// struct fields stay plain `u32` — a `repr(u32)` enum field receiving an
// out-of-range value over FFI is UB — so the enums are only materialized
// after validation at the conversion boundary (`convert.rs`). Flags-style
// values (usages, visibility stages, features) are raw bit fields.
// ---------------------------------------------------------------------------

/// Declares an ABI enum: `#[repr(u32)]` with explicit discriminants, plus a
/// fallible `u32` parse that rejects unknown values (the offending value is
/// returned as the error).
macro_rules! abi_enum {
    (
        $(#[$doc:meta])*
        $name:ident {
            $(
                $(#[$variant_doc:meta])*
                $variant:ident = $value:expr
            ),* $(,)?
        }
    ) => {
        $(#[$doc])*
        #[repr(u32)]
        #[allow(non_camel_case_types)] // variant names intentionally keep the ABI member spelling
        #[derive(Debug, Clone, Copy, PartialEq, Eq)]
        pub enum $name {
            $(
                $(#[$variant_doc])*
                $variant = $value,
            )*
        }

        impl TryFrom<u32> for $name {
            type Error = u32;

            #[inline]
            fn try_from(value: u32) -> Result<Self, Self::Error> {
                match value {
                    $($value => Ok($name::$variant),)*
                    _ => Err(value),
                }
            }
        }
    };
}

abi_enum! {
    /// Texture pixel formats (0..95; 0 = undefined).
    PixelFormat {
        Undefined = 0, R8Unorm = 1, R8Snorm = 2, R8Uint = 3,
        R8Sint = 4, R16Uint = 5, R16Sint = 6, R16Float = 7,
        RG8Unorm = 8, RG8Snorm = 9, RG8Uint = 10, RG8Sint = 11,
        R32Float = 12, R32Uint = 13, R32Sint = 14, RG16Uint = 15,
        RG16Sint = 16, RG16Float = 17, RGBA8Unorm = 18, RGBA8UnormSrgb = 19,
        RGBA8Snorm = 20, RGBA8Uint = 21, RGBA8Sint = 22, BGRA8Unorm = 23,
        BGRA8UnormSrgb = 24, RGB10A2Uint = 25, RGB10A2Unorm = 26, RG11B10Ufloat = 27,
        RGB9E5Ufloat = 28, RG32Float = 29, RG32Uint = 30, RG32Sint = 31,
        RGBA16Uint = 32, RGBA16Sint = 33, RGBA16Float = 34, RGBA32Float = 35,
        RGBA32Uint = 36, RGBA32Sint = 37, Stencil8 = 38, Depth16Unorm = 39,
        Depth24Plus = 40, Depth24PlusStencil8 = 41, Depth32Float = 42, Depth32FloatStencil8 = 43,
        BC1RGBAUnorm = 44, BC1RGBAUnormSrgb = 45, BC2RGBAUnorm = 46, BC2RGBAUnormSrgb = 47,
        BC3RGBAUnorm = 48, BC3RGBAUnormSrgb = 49, BC4RUnorm = 50, BC4RSnorm = 51,
        BC5RGUnorm = 52, BC5RGSnorm = 53, BC6HRGBUfloat = 54, BC6HRGBFloat = 55,
        BC7RGBAUnorm = 56, BC7RGBAUnormSrgb = 57, ETC2RGB8Unorm = 58, ETC2RGB8UnormSrgb = 59,
        ETC2RGB8A1Unorm = 60, ETC2RGB8A1UnormSrgb = 61, ETC2RGBA8Unorm = 62, ETC2RGBA8UnormSrgb = 63,
        EACR11Unorm = 64, EACR11Snorm = 65, EACRG11Unorm = 66, EACRG11Snorm = 67,
        ASTC4x4Unorm = 68, ASTC4x4UnormSrgb = 69, ASTC5x4Unorm = 70, ASTC5x4UnormSrgb = 71,
        ASTC5x5Unorm = 72, ASTC5x5UnormSrgb = 73, ASTC6x5Unorm = 74, ASTC6x5UnormSrgb = 75,
        ASTC6x6Unorm = 76, ASTC6x6UnormSrgb = 77, ASTC8x5Unorm = 78, ASTC8x5UnormSrgb = 79,
        ASTC8x6Unorm = 80, ASTC8x6UnormSrgb = 81, ASTC8x8Unorm = 82, ASTC8x8UnormSrgb = 83,
        ASTC10x5Unorm = 84, ASTC10x5UnormSrgb = 85, ASTC10x6Unorm = 86, ASTC10x6UnormSrgb = 87,
        ASTC10x8Unorm = 88, ASTC10x8UnormSrgb = 89, ASTC10x10Unorm = 90, ASTC10x10UnormSrgb = 91,
        ASTC12x10Unorm = 92, ASTC12x10UnormSrgb = 93, ASTC12x12Unorm = 94, ASTC12x12UnormSrgb = 95
    }
}

abi_enum! {
    /// Texture dimensions.
    TextureDimension {
        Texture1D = 0, Texture2D = 1, Texture3D = 2
    }
}

abi_enum! {
    /// Texture view dimensions.
    TextureViewDimension {
        Undefined = 0, Texture1D = 1, Texture1DArray = 2, Texture2D = 3,
        Texture2DArray = 4, Texture3D = 5, Cube = 6, CubeArray = 7
    }
}

abi_enum! {
    /// Texture aspects.
    TextureAspect {
        None = 0, All = 1, StencilOnly = 2, DepthOnly = 3
    }
}

abi_enum! {
    /// Sampler address modes.
    AddressMode {
        Repeat = 0, MirrorRepeat = 1, ClampToEdge = 2
    }
}

abi_enum! {
    /// Sampler filter modes.
    /// `None` is a valid member but rejected for samplers.
    FilterMode {
        None = 0, Nearest = 1, Linear = 2
    }
}

abi_enum! {
    /// Comparison functions.
    /// `Undefined` means "no comparison" in optional fields.
    CompareFunction {
        Undefined = 0, Never = 1, Less = 2, LessEqual = 3,
        Equal = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7,
        Always = 8
    }
}

abi_enum! {
    /// Blend factors.
    BlendFactor {
        Zero = 0, One = 1, Src = 2, OneMinusSrc = 3,
        SrcAlpha = 4, OneMinusSrcAlpha = 5, Dst = 6, OneMinusDst = 7,
        DstAlpha = 8, OneMinusDstAlpha = 9, SrcAlphaSaturated = 10, Constant = 11,
        OneMinusConstant = 12
    }
}

abi_enum! {
    /// Blend operations.
    BlendOperation {
        Add = 0, Subtract = 1, ReverseSubtract = 2, Min = 3,
        Max = 4
    }
}

abi_enum! {
    /// Face cull modes.
    CullMode {
        None = 0, Front = 1, Back = 2
    }
}

abi_enum! {
    /// Front-face winding orders.
    FrontFace {
        CounterClockwise = 0, Clockwise = 1
    }
}

abi_enum! {
    /// Primitive topologies.
    PrimitiveTopology {
        PointList = 0, LineList = 1, LineStrip = 2, TriangleList = 3,
        TriangleStrip = 4
    }
}

abi_enum! {
    /// Index buffer formats.
    /// `Undefined` is only valid with non-indexed draws.
    IndexFormat {
        Undefined = 0, UInt16 = 1, UInt32 = 2
    }
}

abi_enum! {
    /// Stencil operations.
    StencilOperation {
        Keep = 0, Zero = 1, Replace = 2, Invert = 3,
        IncrementClamp = 4, DecrementClamp = 5, IncrementWrap = 6, DecrementWrap = 7
    }
}

abi_enum! {
    /// Vertex attribute formats.
    /// `Undefined` never appears in real layouts.
    VertexFormat {
        Undefined = 0, Uint8x2 = 1, Uint8x4 = 2, Sint8x2 = 3,
        Sint8x4 = 4, Unorm8x2 = 5, Unorm8x4 = 6, Snorm8x2 = 7,
        Snorm8x4 = 8, Uint16x2 = 9, Uint16x4 = 10, Sint16x2 = 11,
        Sint16x4 = 12, Unorm16x2 = 13, Unorm16x4 = 14, Snorm16x2 = 15,
        Snorm16x4 = 16, Float16x2 = 17, Float16x4 = 18, Float32 = 19,
        Float32x2 = 20, Float32x3 = 21, Float32x4 = 22, Uint32 = 23,
        Uint32x2 = 24, Uint32x3 = 25, Uint32x4 = 26, Sint32 = 27,
        Sint32x2 = 28, Sint32x3 = 29, Sint32x4 = 30
    }
}

abi_enum! {
    /// Vertex buffer step modes.
    /// `NotUsed` has no wgpu counterpart.
    VertexStepMode {
        Vertex = 0, Instance = 1, NotUsed = 2
    }
}

abi_enum! {
    /// Texture binding sample types.
    TextureSampleType {
        None = 0, Float = 1, UnfilterableFloat = 2, Depth = 3,
        Sint = 4, Uint = 5
    }
}

abi_enum! {
    /// Bind-group-layout binding types.
    BindingType {
        Undefined = 0, UniformBuffer = 1, StorageBuffer = 2, Sampler = 3,
        Texture = 4, StorageTexture = 5, SamplerComparison = 6
    }
}

abi_enum! {
    /// Storage texture access modes.
    /// Read|Write compose like flags but storage bindings use exclusive values.
    AccessMode {
        None = 0, Read = 1, Write = 2, ReadWrite = 3
    }
}

abi_enum! {
    /// Shader module source languages.
    /// `SLANG` is never a valid input; sources arrive fully compiled.
    ShaderLanguage {
        Undefined = 0, SLANG = 1, SPIRV = 2, WGSL = 3,
        DXIL = 4, MSL = 5, MetalLib = 6
    }
}

abi_enum! {
    /// Attachment load operations.
    AttachmentLoadOp {
        Load = 0, Clear = 1
    }
}

abi_enum! {
    /// Attachment store operations.
    AttachmentStoreOp {
        Store = 0, Discard = 1
    }
}
