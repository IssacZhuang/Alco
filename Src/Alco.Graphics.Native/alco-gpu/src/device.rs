//! Device creation, polling and staged teardown. Children retain the cleanup
//! context independently of the public device owner; ordinary operations borrow it.

use crate::abi::*;
use crate::entry::{set_error, set_error_from};
use std::ffi::{c_char, CStr, CString};
use std::sync::{Arc, Mutex};
use wgpu_core as wgc;
use wgpu_core::global::Global;
use wgpu_types as wgt;

/// Core registrations retained until the last independently owned child is released.
pub struct DeviceCtx {
    /// Core instance and resource registries.
    pub global: Global,
    /// Adapter registration released during final context cleanup.
    pub adapter_id: wgc::id::AdapterId,
    /// Device registration retained for late resource cleanup.
    pub device_id: wgc::id::DeviceId,
    /// The device's single queue registration.
    pub queue_id: wgc::id::QueueId,
    /// Resolved backend (`backend::RESOLVED_*`).
    pub backend: u32,
    /// Adapter name backing borrowed device information.
    pub adapter_name: CString,
    /// Adapter vendor identifier.
    pub vendor: u32,
    /// Adapter device identifier.
    pub device: u32,
    /// Alco feature bits supported by this device.
    pub supported_features: u64,
    /// Native capability bits.
    pub capabilities: u64,
    /// Maximum number of bind groups.
    pub max_bind_groups: u32,
    /// Maximum immediate data size in bytes.
    pub max_immediate_size: u32,
    /// Timestamp tick duration in nanoseconds.
    pub timestamp_period_ns: f32,
    /// Queued asynchronous messages drained by the host.
    pub messages: Mutex<Vec<QueuedDeviceMessage>>,
    /// Storage backing the borrowed message pointer of the last pop.
    pub message_scratch: Mutex<CString>,
}

/// Unique public device owner; children hold references only to its cleanup context.
pub struct DeviceOwner {
    /// Cleanup context shared with independently owned child objects.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for DeviceOwner {
    fn drop(&mut self) {
        self.ctx.global.device_destroy(self.ctx.device_id);
        if let Err(error) = self.ctx.global.device_poll(
            self.ctx.device_id,
            wgt::PollType::Wait {
                submission_index: None,
                timeout: None,
            },
        ) {
            log::warn!("device teardown maintenance: {error}");
        }
    }
}

impl Drop for DeviceCtx {
    fn drop(&mut self) {
        if let Err(error) = self.global.device_poll(
            self.device_id,
            wgt::PollType::Wait {
                submission_index: None,
                timeout: None,
            },
        ) {
            log::warn!("final device maintenance: {error}");
        }
        self.global.queue_drop(self.queue_id);
        self.global.device_drop(self.device_id);
        self.global.adapter_drop(self.adapter_id);
    }
}

/// One asynchronously queued device diagnostic.
pub struct QueuedDeviceMessage {
    /// Severity: zero error, one warning, two information.
    pub severity: u32,
    /// Kind: zero generic, one device lost.
    pub kind: u32,
    /// Owned message text.
    pub message: CString,
}

impl DeviceCtx {
    /// Queues an async message. Reserved for device-loss plumbing; wgpu-core
    /// 30 surfaces sync errors through return values, so nothing calls this
    /// from regular entry points yet.
    #[allow(dead_code)]
    pub fn push_message(&self, severity: u32, kind: u32, message: impl Into<String>) {
        let text = CString::new(message.into().replace('\0', "\\0"))
            .unwrap_or_else(|_| CString::new("invalid message").unwrap());
        self.messages.lock().unwrap().push(QueuedDeviceMessage {
            severity,
            kind,
            message: text,
        });
    }
}

/// Alco feature bits — numeric values mirror C# `GPUFeatures` exactly.
pub(crate) mod gpu_features {
    pub const TEXTURE_COMPRESSION_BC: u64 = 1 << 0;
    pub const TIMESTAMP_QUERY: u64 = 1 << 1;
    pub const TIMESTAMP_QUERY_INSIDE_PASSES: u64 = 1 << 2;
    pub const METALLIB_PASSTHROUGH: u64 = 1 << 3;
    pub const INDIRECT_FIRST_INSTANCE: u64 = 1 << 4;
    pub const MULTI_DRAW_INDIRECT: u64 = 1 << 5;
    pub const CLEAR_TEXTURE: u64 = 1 << 6;
}

/// Maps Alco request-backend values to wgpu backend masks.
fn request_backends(requested: u32) -> wgt::Backends {
    match requested {
        backend::VULKAN => wgt::Backends::VULKAN,
        backend::DX12 => wgt::Backends::DX12,
        backend::METAL => wgt::Backends::METAL,
        // AUTO: primary desktop/mobile backends (Vulkan > Metal > Dx12 > GL).
        _ => wgt::Backends::PRIMARY,
    }
}

fn resolved_backend_value(backend: wgt::Backend) -> u32 {
    match backend {
        wgt::Backend::Vulkan => backend::RESOLVED_VULKAN,
        wgt::Backend::Dx12 => backend::RESOLVED_DX12,
        wgt::Backend::Metal => backend::RESOLVED_METAL,
        wgt::Backend::Gl => backend::RESOLVED_GL,
        _ => backend::RESOLVED_NULL,
    }
}

/// Converts Alco feature bits to wgpu `Features`. `METALLIB_PASSTHROUGH` has no
/// wgpu counterpart (it is a build/platform capability reported via capabilities).
fn gpu_features_to_wgpu(bits: u64) -> wgt::Features {
    let mut features = wgt::Features::empty();
    if bits & gpu_features::TEXTURE_COMPRESSION_BC != 0 {
        features |= wgt::Features::TEXTURE_COMPRESSION_BC;
    }
    if bits & gpu_features::TIMESTAMP_QUERY != 0 {
        features |= wgt::Features::TIMESTAMP_QUERY;
    }
    if bits & gpu_features::TIMESTAMP_QUERY_INSIDE_PASSES != 0 {
        features |= wgt::Features::TIMESTAMP_QUERY_INSIDE_PASSES;
    }
    if bits & gpu_features::INDIRECT_FIRST_INSTANCE != 0 {
        features |= wgt::Features::INDIRECT_FIRST_INSTANCE;
    }
    if bits & gpu_features::CLEAR_TEXTURE != 0 {
        features |= wgt::Features::CLEAR_TEXTURE;
    }
    // MULTI_DRAW_INDIRECT needs no wgpu feature in wgpu 30: multi-draw is
    // always available and emulated when the native path is absent.
    features
}

/// Computes the Alco feature bits and capabilities supported by an adapter.
fn supported_gpu_features(adapter_features: wgt::Features) -> (u64, u64) {
    let mut features = 0u64;
    let mut caps = 0u64;
    if adapter_features.contains(wgt::Features::TEXTURE_COMPRESSION_BC) {
        features |= gpu_features::TEXTURE_COMPRESSION_BC;
    }
    if adapter_features.contains(wgt::Features::TIMESTAMP_QUERY) {
        features |= gpu_features::TIMESTAMP_QUERY;
    }
    if adapter_features.contains(wgt::Features::TIMESTAMP_QUERY_INSIDE_PASSES) {
        features |= gpu_features::TIMESTAMP_QUERY_INSIDE_PASSES;
        caps |= capabilities::TIMESTAMP_INSIDE_PASSES;
    }
    if adapter_features.contains(wgt::Features::INDIRECT_FIRST_INSTANCE) {
        features |= gpu_features::INDIRECT_FIRST_INSTANCE;
    }
    if adapter_features.contains(wgt::Features::CLEAR_TEXTURE) {
        features |= gpu_features::CLEAR_TEXTURE;
    }
    // Multi-draw is always claimed: wgpu 30 supports multi_draw_* on all
    // backends, emulating with per-record draws when no native path exists.
    features |= gpu_features::MULTI_DRAW_INDIRECT;
    if adapter_features.contains(wgt::Features::PASSTHROUGH_SHADERS) {
        caps |= capabilities::PASSTHROUGH_SHADERS;
    }
    (features, caps)
}

/// Required engine capabilities, separate from desired-optional Alco features.
fn base_adapter_features(backend: wgt::Backend) -> wgt::Features {
    let mut required = wgt::Features::IMMEDIATES
        | wgt::Features::TEXTURE_ADAPTER_SPECIFIC_FORMAT_FEATURES
        | wgt::Features::VERTEX_WRITABLE_STORAGE;
    // Vulkan and DX12 can compile SPIR-V through Naga; Metal needs passthrough.
    if backend != wgt::Backend::Vulkan && backend != wgt::Backend::Dx12 {
        required |= wgt::Features::PASSTHROUGH_SHADERS;
    }
    required
}

fn adapter_rejection(
    backend: wgt::Backend,
    features: wgt::Features,
    max_immediate_size: u32,
    requested_immediate_size: u32,
) -> Option<String> {
    let missing = base_adapter_features(backend) - features;
    if !missing.is_empty() {
        return Some(format!("missing required features: {missing:?}"));
    }
    let requested = requested_immediate_size.max(1);
    if max_immediate_size < requested {
        return Some(format!(
            "max_immediate_size {max_immediate_size} is below requested {requested}",
        ));
    }
    None
}

fn high_performance_options() -> wgc::instance::RequestAdapterOptions {
    wgc::instance::RequestAdapterOptions {
        power_preference: wgt::PowerPreference::HighPerformance,
        force_fallback_adapter: false,
        compatible_surface: None,
        apply_limit_buckets: false,
    }
}

// Match wgpu-core 30's high-performance ordering for capability-filtered fallback.
// A stable sort preserves core enumeration/backend order within each device type.
fn high_performance_rank(device_type: wgt::DeviceType) -> u8 {
    match device_type {
        wgt::DeviceType::DiscreteGpu => 1,
        wgt::DeviceType::IntegratedGpu => 2,
        wgt::DeviceType::Other => 3,
        wgt::DeviceType::VirtualGpu => 4,
        wgt::DeviceType::Cpu => 5,
    }
}

fn select_adapter(
    global: &Global,
    backends: wgt::Backends,
    immediate_size: u32,
) -> Result<wgc::id::AdapterId, Status> {
    let preferred = global
        .request_adapter(&high_performance_options(), backends, None)
        .map_err(|e| {
            set_error_from(Status::UNSUPPORTED, &e);
            Status::UNSUPPORTED
        })?;
    let rejection = |id| {
        adapter_rejection(
            global.adapter_get_info(id).backend,
            global.adapter_features(id),
            global.adapter_limits(id).max_immediate_size,
            immediate_size,
        )
    };
    let Some(reason) = rejection(preferred) else {
        return Ok(preferred);
    };
    let mut reasons = vec![format!(
        "{}: {reason}",
        global.adapter_get_info(preferred).name
    )];
    global.adapter_drop(preferred);

    // RequestAdapterOptions has no feature/limit filter in v30. Enumerate only
    // when the preferred GPU cannot satisfy the engine, then apply the same
    // high-performance ranking to all capability-supported candidates.
    let mut candidates = global.enumerate_adapters(backends, false);
    candidates.sort_by_key(|&id| high_performance_rank(global.adapter_get_info(id).device_type));
    let mut selected = None;
    for id in candidates {
        if selected.is_none() {
            if let Some(reason) = rejection(id) {
                reasons.push(format!("{}: {reason}", global.adapter_get_info(id).name));
            } else {
                selected = Some(id);
                continue;
            }
        }
        global.adapter_drop(id);
    }
    selected.ok_or_else(|| {
        set_error(
            Status::UNSUPPORTED,
            format!(
                "no adapter supports the engine requirements: {}",
                reasons.join("; ")
            ),
        );
        Status::UNSUPPORTED
    })
}

unsafe fn borrow_str(ptr: *const c_char) -> String {
    if ptr.is_null() {
        String::new()
    } else {
        CStr::from_ptr(ptr).to_string_lossy().into_owned()
    }
}

/// ABI: creates a device (instance → adapter → device) synchronously.
///
/// # Safety
/// `desc` must be valid; `out` must be a valid `DeviceHandle` slot.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create(
    desc: *const DeviceDesc,
    out: *mut DeviceHandle,
) -> Status {
    crate::entry::guard(|| {
        if desc.is_null() || out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null descriptor or out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let desc = &*desc;
        *out = DeviceHandle::NULL;
        crate::entry::initialize();

        let backends = request_backends(desc.backend);
        let debug = desc.debug == TRUE;

        let instance_desc = wgt::InstanceDescriptor {
            backends,
            flags: if debug {
                wgt::InstanceFlags::debugging()
            } else {
                wgt::InstanceFlags::default()
            },
            memory_budget_thresholds: wgt::MemoryBudgetThresholds::default(),
            backend_options: wgt::BackendOptions::default(),
            display: None,
        };
        let global = Global::new("alco-gpu", instance_desc, None);

        let adapter_id = match select_adapter(&global, backends, desc.push_constants_size) {
            Ok(id) => id,
            Err(status) => return status,
        };

        let adapter_info = global.adapter_get_info(adapter_id);
        let adapter_features = global.adapter_features(adapter_id);
        let adapter_limits = global.adapter_limits(adapter_id);
        let resolved = resolved_backend_value(adapter_info.backend);

        // Base feature policy of the Alco engine contract (mirrors the old
        // WebGPUDevice gating): immediates, adapter-specific format features
        // and vertex writable storage are always required. Vulkan and DX12
        // support Naga SPIR-V; Metal requires native shader passthrough.
        let (supported, mut device_caps) = supported_gpu_features(adapter_features);

        // Requested Alco features are desired-optional: intersect with adapter
        // support instead of failing, so the C# side can blanket-request the
        // optional set without a probe round trip (mirrors the old WebGPUDevice
        // behavior of only requesting probed features).
        let mut required = base_adapter_features(adapter_info.backend)
            | gpu_features_to_wgpu(desc.required_features & supported);
        if adapter_info.backend == wgt::Backend::Dx12 {
            required |= adapter_features & wgt::Features::SHADER_F16;
        }
        let passthrough_available = adapter_features.contains(wgt::Features::PASSTHROUGH_SHADERS);
        if passthrough_available {
            required |= wgt::Features::PASSTHROUGH_SHADERS;
            device_caps |= capabilities::PASSTHROUGH_SHADERS;
        }
        // MetalLib passthrough is an Apple-platform source kind.
        if resolved == backend::RESOLVED_METAL && passthrough_available {
            device_caps |= capabilities::METALLIB;
            // Surface it as a supported Alco feature: the C# shader system
            // keys its Metal code target on this bit.
            // (folded into `supported` below via `device_caps` re-check)
        }

        let missing = required - adapter_features;
        if !missing.is_empty() {
            set_error(
                Status::UNSUPPORTED,
                format!("adapter does not support required features: {missing:?}"),
            );
            return Status::UNSUPPORTED;
        }

        // Required limits = adapter limits with the immediate (push constants)
        // size clamped to the engine's request (same policy as WebGPUDevice).
        let mut required_limits = adapter_limits.clone();
        required_limits.max_immediate_size = desc.push_constants_size.max(1);

        let device_desc = wgt::DeviceDescriptor {
            label: Some(std::borrow::Cow::Owned(borrow_str(desc.name))),
            required_features: required,
            required_limits,
            experimental_features: Default::default(),
            memory_hints: wgt::MemoryHints::default(),
            trace: wgt::Trace::default(),
        };
        let (device_id, queue_id) =
            match global.adapter_request_device(adapter_id, &device_desc, None, None) {
                Ok(ids) => ids,
                Err(e) => {
                    set_error_from(Status::UNSUPPORTED, &e);
                    return Status::UNSUPPORTED;
                }
            };

        let timestamp_period_ns = if adapter_features.contains(wgt::Features::TIMESTAMP_QUERY) {
            global.queue_get_timestamp_period(queue_id)
        } else {
            1.0
        };

        let mut supported = supported;
        if device_caps & capabilities::METALLIB != 0 {
            supported |= gpu_features::METALLIB_PASSTHROUGH;
        }

        let ctx = DeviceCtx {
            global,
            adapter_id,
            device_id,
            queue_id,
            backend: resolved,
            adapter_name: CString::new(adapter_info.name.replace('\0', "\\0"))
                .unwrap_or_else(|_| CString::new("adapter").unwrap()),
            vendor: adapter_info.vendor,
            device: adapter_info.device,
            supported_features: supported,
            capabilities: device_caps,
            max_bind_groups: adapter_limits.max_bind_groups,
            max_immediate_size: desc.push_constants_size,
            timestamp_period_ns,
            messages: Mutex::new(Vec::new()),
            message_scratch: Mutex::new(CString::new("").unwrap()),
        };

        *out = DeviceHandle::new(DeviceOwner { ctx: Arc::new(ctx) });
        Status::OK
    })
}

/// ABI: consumes the public owner, invalidates the device, and waits for teardown maintenance.
///
/// Children retain only a cleanup context and may be released after this call.
///
/// # Safety
/// The pointer must identify a live unique device owner. All ordinary device and
/// child operations must finish before teardown; the consumed pointer cannot be reused.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_destroy(device: DeviceHandle) -> Status {
    crate::entry::guard(|| match device.take() {
        Ok(owner) => {
            drop(owner);
            Status::OK
        }
        Err(status) => status,
    })
}

/// ABI: fills static device information.
///
/// # Safety
/// `out` must be writable. The device owner must remain alive through this call
/// and every use of its borrowed adapter name.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_get_info(
    device: DeviceHandle,
    out: *mut DeviceInfo,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let owner = match device.get() {
            Ok(owner) => owner,
            Err(status) => return status,
        };
        let ctx = &owner.ctx;
        (*out).backend = ctx.backend;
        (*out).adapter_name = ctx.adapter_name.as_ptr();
        (*out).vendor = ctx.vendor;
        (*out).device = ctx.device;
        (*out).supported_features = ctx.supported_features;
        (*out).capabilities = ctx.capabilities;
        (*out).max_bind_groups = ctx.max_bind_groups;
        (*out).max_immediate_size = ctx.max_immediate_size;
        (*out).timestamp_period_ns = ctx.timestamp_period_ns;
        Status::OK
    })
}

/// ABI: polls the device. With `wait = TRUE` blocks until the given
/// submission index (or the latest submission when `u64::MAX`) completes.
/// With `wait = FALSE` performs a single non-blocking pump: map and
/// submitted-work callbacks that are already complete get fired.
///
/// The non-blocking path deliberately uses a zero-timeout `Wait` rather than
/// `PollType::Poll`: on wgpu-core 30.0.1 the `Poll` variant never observes
/// fence completion on the Vulkan backend, so pending buffer maps never
/// resolve. A zero-timeout wait takes the same hal wait + triage path as a
/// blocking poll but returns immediately when the fence is not signaled.
///
/// # Safety
/// `out_queue_empty` if non-null is a valid `u32` slot.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_poll(
    device: DeviceHandle,
    wait: u32,
    submit_index: u64,
    out_queue_empty: *mut u32,
) -> Status {
    crate::entry::guard(|| {
        let owner = match device.get() {
            Ok(owner) => owner,
            Err(status) => return status,
        };
        let ctx = &owner.ctx;
        let poll_type = if wait == TRUE {
            if submit_index == u64::MAX {
                wgt::PollType::Wait {
                    submission_index: None,
                    timeout: None,
                }
            } else {
                wgt::PollType::Wait {
                    submission_index: Some(submit_index),
                    timeout: None,
                }
            }
        } else {
            wgt::PollType::Wait {
                submission_index: None,
                timeout: Some(core::time::Duration::ZERO),
            }
        };
        match ctx.global.device_poll(ctx.device_id, poll_type) {
            Ok(status) => {
                if !out_queue_empty.is_null() {
                    *out_queue_empty = matches!(status, wgt::PollStatus::QueueEmpty) as u32;
                }
                Status::OK
            }
            // A zero-timeout poll with work still in flight reports Timeout;
            // that is the expected "not done yet" outcome, not a device fault.
            Err(wgc::device::WaitIdleError::Timeout) => {
                if !out_queue_empty.is_null() {
                    *out_queue_empty = 0;
                }
                Status::OK
            }
            Err(e) => {
                set_error_from(Status::DEVICE_LOST, &e);
                Status::DEVICE_LOST
            }
        }
    })
}

/// ABI: pops one queued device message. Returns `NOT_READY` when the queue is
/// empty. The message string is borrowed until the next pop on this device.
///
/// # Safety
/// `out` must be a valid `DeviceMessage` slot.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_pop_message(
    device: DeviceHandle,
    out: *mut DeviceMessage,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let owner = match device.get() {
            Ok(owner) => owner,
            Err(status) => return status,
        };
        let ctx = &owner.ctx;
        let message = ctx.messages.lock().unwrap().pop();
        match message {
            Some(message) => {
                let mut scratch = ctx.message_scratch.lock().unwrap();
                *scratch = message.message;
                (*out).severity = message.severity;
                (*out).kind = message.kind;
                (*out).message = scratch.as_ptr();
                Status::OK
            }
            None => Status::NOT_READY,
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn vulkan_public_teardown_preserves_cleanup_context_until_last_owner() {
        let Some(device) = crate::test_support::TestDevice::new() else {
            return;
        };
        let ctx = unsafe { device.handle.get().unwrap().ctx.clone() };
        let weak = Arc::downgrade(&ctx);
        let mut info = DeviceInfo {
            backend: 0,
            adapter_name: std::ptr::null(),
            vendor: 0,
            device: 0,
            supported_features: 0,
            capabilities: 0,
            max_bind_groups: 0,
            max_immediate_size: 0,
            timestamp_period_ns: 0.0,
        };
        unsafe {
            assert_eq!(device_get_info(device.handle, &mut info), Status::OK);
            assert_eq!(info.backend, ctx.backend);
            assert_eq!(
                device_poll(device.handle, TRUE, u64::MAX, std::ptr::null_mut()),
                Status::OK
            );
        }
        drop(device);
        assert!(weak.upgrade().is_some());
        drop(ctx);
        assert!(weak.upgrade().is_none());
    }

    #[test]
    fn adapter_request_preserves_high_performance_and_backend_masks() {
        let options = high_performance_options();
        assert_eq!(
            options.power_preference,
            wgt::PowerPreference::HighPerformance
        );
        assert!(!options.force_fallback_adapter);
        assert!(!options.apply_limit_buckets);
        assert!(options.compatible_surface.is_none());
        assert_eq!(request_backends(backend::AUTO), wgt::Backends::PRIMARY);
        assert_eq!(request_backends(backend::VULKAN), wgt::Backends::VULKAN);
        assert_eq!(request_backends(backend::DX12), wgt::Backends::DX12);
        assert_eq!(request_backends(backend::METAL), wgt::Backends::METAL);
    }

    #[test]
    fn fallback_ranking_matches_core_high_performance_order_and_is_stable() {
        use wgt::DeviceType::*;
        let mut candidates = [
            (Cpu, 0),
            (IntegratedGpu, 1),
            (DiscreteGpu, 2),
            (Other, 3),
            (VirtualGpu, 4),
            (DiscreteGpu, 5),
        ];
        candidates.sort_by_key(|&(ty, _)| high_performance_rank(ty));
        assert_eq!(
            candidates,
            [
                (DiscreteGpu, 2),
                (DiscreteGpu, 5),
                (IntegratedGpu, 1),
                (Other, 3),
                (VirtualGpu, 4),
                (Cpu, 0)
            ]
        );
    }

    #[test]
    fn unsupported_preferred_adapter_does_not_hide_supported_candidates() {
        let base = base_adapter_features(wgt::Backend::Vulkan);
        let mut candidates = [
            (
                wgt::DeviceType::IntegratedGpu,
                wgt::Backend::Vulkan,
                base,
                256,
            ),
            (
                wgt::DeviceType::DiscreteGpu,
                wgt::Backend::Vulkan,
                base - wgt::Features::VERTEX_WRITABLE_STORAGE,
                256,
            ),
            (wgt::DeviceType::DiscreteGpu, wgt::Backend::Vulkan, base, 8),
        ];
        candidates.sort_by_key(|&(ty, ..)| high_performance_rank(ty));
        let selected = candidates.iter().find(|&&(_, backend, features, limit)| {
            adapter_rejection(backend, features, limit, 16).is_none()
        });
        assert_eq!(selected.unwrap().0, wgt::DeviceType::IntegratedGpu);
        assert!(adapter_rejection(wgt::Backend::Vulkan, base, 16, 16).is_none());
        assert!(adapter_rejection(wgt::Backend::Dx12, base, 16, 16).is_none());
        assert!(adapter_rejection(wgt::Backend::Metal, base, 16, 16).is_some());
        assert!(adapter_rejection(
            wgt::Backend::Metal,
            base | wgt::Features::PASSTHROUGH_SHADERS,
            16,
            16
        )
        .is_none());
        // Desired-optional features must not become selection requirements.
        assert!(!base.contains(wgt::Features::TIMESTAMP_QUERY));
        assert_eq!(
            gpu_features_to_wgpu(gpu_features::TIMESTAMP_QUERY & supported_gpu_features(base).0),
            wgt::Features::empty()
        );
    }
}
