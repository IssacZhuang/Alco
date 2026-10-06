//! Raw C ABI types shared with the C# side (`Alco.Graphics/AlcoGpu/Interop/AlcoGpuStructs.cs`).
//!
//! Layout contract: every struct here is `#[repr(C)]` and mirrored field-by-field
//! in C# with `[StructLayout(LayoutKind.Sequential)]`. Booleans are `u32`
//! (`ALCO_TRUE = 1`), enums are `u32` with the same numeric values as the C#
//! enums, optional values use `ALCO_NONE = u32::MAX` sentinels, arrays are
//! `(pointer, count)` pairs, and strings are NUL-terminated UTF-8.

/// ABI major version. Increment on any breaking layout/semantic change.
pub const ABI_MAJOR: u32 = 2;
/// ABI minor version. Increment on additive changes.
pub const ABI_MINOR: u32 = 0;

/// Sentinel for "no value" in optional `u32` fields.
pub const ALCO_NONE: u32 = u32::MAX;
/// Boolean true in `u32` bool fields.
pub const ALCO_TRUE: u32 = 1;
/// Boolean false in `u32` bool fields.
pub const ALCO_FALSE: u32 = 0;

/// Status code returned by every fallible ABI entry point.
#[repr(transparent)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AlcoStatus(pub u32);

impl AlcoStatus {
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
pub type AlcoDeviceHandle = crate::handle::Handle<crate::device::DeviceOwner>;
/// Owned buffer pointer.
pub type AlcoBufferHandle = crate::handle::Handle<crate::objects::BufferObj>;
/// Owned regular or acquired texture pointer.
pub type AlcoTextureHandle = crate::handle::Handle<crate::objects::TextureObj>;
/// Owned texture view pointer.
pub type AlcoTextureViewHandle = crate::handle::Handle<crate::objects::TextureViewObj>;
/// Owned sampler pointer.
pub type AlcoSamplerHandle = crate::handle::Handle<crate::objects::SamplerObj>;
/// Owned shader module pointer.
pub type AlcoShaderModuleHandle = crate::handle::Handle<crate::objects::ShaderModuleObj>;
/// Owned bind-group layout pointer.
pub type AlcoBindGroupLayoutHandle = crate::handle::Handle<crate::objects::BindGroupLayoutObj>;
/// Owned bind-group pointer.
pub type AlcoBindGroupHandle = crate::handle::Handle<crate::objects::BindGroupObj>;
/// Owned timestamp query set pointer.
pub type AlcoQuerySetHandle = crate::handle::Handle<crate::objects::QuerySetObj>;
/// Owned graphics pipeline pointer.
pub type AlcoGraphicsPipelineHandle = crate::handle::Handle<crate::pipeline::GraphicsPipelineObj>;
/// Owned compute pipeline pointer.
pub type AlcoComputePipelineHandle = crate::handle::Handle<crate::pipeline::ComputePipelineObj>;
/// Owned command encoder pointer, consumed by finish or destroy.
pub type AlcoEncoderHandle = crate::handle::Handle<crate::commands::EncoderObj>;
/// Owned command buffer pointer, consumed by submit or destroy.
pub type AlcoCommandBufferHandle = crate::handle::Handle<crate::commands::CommandBufferObj>;
/// Caller-exclusive render pass pointer, consumed by end or release.
pub type AlcoRenderPassHandle = crate::handle::Handle<crate::commands::RenderPassObj>;
/// Caller-exclusive compute pass pointer, consumed by end or release.
pub type AlcoComputePassHandle = crate::handle::Handle<crate::commands::ComputePassObj>;
/// Caller-exclusive bundle encoder pointer, consumed by finish or destroy.
pub type AlcoBundleEncoderHandle = crate::handle::Handle<crate::commands::BundleEncoderObj>;
/// Owned render bundle pointer.
pub type AlcoRenderBundleHandle = crate::handle::Handle<crate::commands::RenderBundleObj>;
/// Owned surface reference, released only by surface destroy.
pub type AlcoSurfaceHandle = crate::handle::Handle<crate::surface::SurfaceObj>;

/// `AlcoBackend` values — mirrors C# `GraphicsBackend` restricted to what the
/// native layer understands.
pub mod backend {
    pub const AUTO: u32 = 1;
    pub const VULKAN: u32 = 2;
    pub const DX12: u32 = 3;
    pub const METAL: u32 = 4;
    /// Resolved backend values reported by `AlcoDeviceInfo`.
    pub const RESOLVED_VULKAN: u32 = 2;
    pub const RESOLVED_DX12: u32 = 3;
    pub const RESOLVED_METAL: u32 = 4;
    pub const RESOLVED_GL: u32 = 5;
    pub const RESOLVED_NULL: u32 = 6;
}

/// `AlcoDeviceCaps` bits — mirrors C# capability probing that today pokes
/// wgpu-native features/exports directly.
pub mod caps {
    /// Adapter/device supports PASSTHROUGH_SHADERS (required for DXIL/MSL/MetalLib).
    pub const PASSTHROUGH_SHADERS: u64 = 1 << 0;
    /// MetalLib passthrough source accepted by this build (Apple platforms).
    pub const METALLIB: u64 = 1 << 1;
    /// MULTI_DRAW_INDIRECT native extension available.
    pub const MULTI_DRAW_INDIRECT: u64 = 1 << 2;
    /// TIMESTAMP_QUERY_INSIDE_PASSES native feature available.
    pub const TIMESTAMP_INSIDE_PASSES: u64 = 1 << 3;
}

/// `AlcoLogLevel` values for the log callback — same numeric values as
/// wgpu-native's `WGPULogLevel` and the C# `AlcoGpuAbi.LogLevel` constants.
pub mod log_level {
    /// Disables forwarding entirely (argument to `alco_set_log_level`).
    pub const OFF: u32 = 0;
    pub const ERROR: u32 = 1;
    pub const WARN: u32 = 2;
    pub const INFO: u32 = 3;
    pub const DEBUG: u32 = 4;
    pub const TRACE: u32 = 5;
}

/// Device creation descriptor. The C# side performs all feature gating (same
/// logic as the old WebGPUDevice) and passes the final required feature set.
#[repr(C)]
pub struct AlcoDeviceDesc {
    /// One of the `backend::*` request values (AUTO/VULKAN/DX12/METAL).
    pub backend: u32,
    /// ALCO_TRUE to enable wgpu validation layers/logging.
    pub debug: u32,
    /// Required Alco feature bits (mirrors C# `GPUFeatures`).
    pub required_features: u64,
    /// Immediate buffer (push constants) size in bytes.
    pub push_constants_size: u32,
    /// NUL-terminated UTF-8 debug label (may be null).
    pub name: *const std::ffi::c_char,
}

/// Static device information returned at creation time. All borrowed strings
/// are owned by the device and remain valid until the device is destroyed.
#[repr(C)]
pub struct AlcoDeviceInfo {
    /// Resolved backend (`backend::RESOLVED_*`).
    pub backend: u32,
    /// NUL-terminated adapter name (borrowed from the device).
    pub adapter_name: *const std::ffi::c_char,
    pub vendor: u32,
    pub device: u32,
    /// Supported Alco feature bits (subset of `AlcoDeviceDesc::required_features` space).
    pub supported_features: u64,
    /// Capability bits (`caps::*`).
    pub caps: u64,
    pub max_bind_groups: u32,
    pub max_immediate_size: u32,
    /// Queue timestamp period in nanoseconds (1.0 when unsupported).
    pub timestamp_period_ns: f32,
}

/// One queued device message (validation error, device lost, native warning).
/// Severity mirrors the old wgpu-native log/error callback levels.
#[repr(C)]
pub struct AlcoDeviceMessage {
    /// 0 = error, 1 = warning, 2 = info.
    pub severity: u32,
    /// 0 = generic, 1 = device lost.
    pub kind: u32,
    /// NUL-terminated message (borrowed until the next `alco_device_pop_message`).
    pub message: *const std::ffi::c_char,
}

/// Build/runtime information for `alco_build_info`.
#[repr(C)]
pub struct AlcoBuildInfo {
    /// `(major << 16) | minor` of the wgpu-types crate linked into this binary.
    pub wgpu_version: u32,
    /// NUL-terminated alco-gpu build identifier (git hash + profile).
    pub alco_build: *const std::ffi::c_char,
}

/// Last-error record for `alco_get_last_error`.
#[repr(C)]
pub struct AlcoErrorInfo {
    /// Matching `AlcoStatus` for the failed call.
    pub status: u32,
    /// NUL-terminated message (borrowed until the next failure on this thread).
    pub message: *const std::ffi::c_char,
}
