//! alco-gpu: Alco-owned C ABI over wgpu-core, replacing wgpu-native for the
//! Alco engine. See `Docs/AlcoGpuAbi.md` and `abi.rs` for the ABI contract.
//!
//! Fallible entry points contain Rust panics (see `entry.rs`). Typed opaque
//! pointers address independently owned objects (see `handle.rs`); callers
//! enforce pointer validity, same-context use and exclusive mutable access.

pub mod abi;
pub(crate) mod commands;
pub(crate) mod convert;
pub(crate) mod device;
pub(crate) mod entry;
pub(crate) mod handle;
pub(crate) mod logging;
pub(crate) mod objects;
pub(crate) mod pipeline;
pub(crate) mod surface;

use std::ffi::CString;
use std::sync::OnceLock;

/// Alco ABI version implemented by this library: `(major << 16) | minor`.
/// The C# side rejects a major mismatch at load time.
#[no_mangle]
pub extern "C" fn abi_version() -> u32 {
    (abi::ABI_MAJOR << 16) | abi::ABI_MINOR
}

/// Build identifier embedding the pinned wgpu-core version (kept in lockstep
/// with the `=30.0.1` requirement in Cargo.toml).
fn build_id() -> &'static str {
    static BUILD_ID: OnceLock<String> = OnceLock::new();
    BUILD_ID.get_or_init(|| {
        let profile = if cfg!(debug_assertions) {
            "debug"
        } else {
            "release"
        };
        let hash = option_env!("ALCO_GIT_HASH").unwrap_or("dev");
        format!("alco-gpu {profile} ({hash}) wgpu-core 30.0.1")
    })
}

/// ABI: build/runtime information. The build string is process-lifetime
/// borrowed; `wgpu_version` is the numeric `(major << 16) | minor` of the
/// pinned wgpu-core (30.0 → 0x001E_0000).
///
/// # Safety
/// `out` must be a valid `abi::BuildInfo` slot.
#[no_mangle]
pub unsafe extern "C" fn build_info(out: *mut abi::BuildInfo) {
    if out.is_null() {
        return;
    }
    static BUILD: OnceLock<CString> = OnceLock::new();
    let build = BUILD.get_or_init(|| CString::new(build_id()).unwrap());
    (*out).build_id = build.as_ptr();
    (*out).wgpu_version = 30u32 << 16;
}

#[cfg(test)]
pub(crate) mod test_support {
    use crate::abi::*;
    use crate::device::{device_create, device_destroy};
    use std::ffi::CStr;
    use wgpu_core::global::Global;
    use wgpu_types as wgt;

    /// Vulkan ABI device with deterministic teardown for native regression tests.
    pub(crate) struct TestDevice {
        /// Owned device handle, valid until the test device is dropped.
        pub handle: DeviceHandle,
    }

    impl TestDevice {
        /// Creates a real Vulkan device, skipping only when Vulkan is unavailable.
        pub fn new() -> Option<Self> {
            Self::with_features(0)
        }

        /// Creates a Vulkan device additionally desiring the given Alco feature
        /// bits (`gpu_features` values, e.g. `CLEAR_TEXTURE`).
        pub fn with_features(required_features: u64) -> Option<Self> {
            // Probe availability separately: a failure in Alco's creation policy
            // on an available Vulkan adapter must fail the test, not silently skip.
            let probe = Global::new(
                "alco-test-probe",
                wgt::InstanceDescriptor {
                    backends: wgt::Backends::VULKAN,
                    flags: wgt::InstanceFlags::empty(),
                    memory_budget_thresholds: wgt::MemoryBudgetThresholds::default(),
                    backend_options: wgt::BackendOptions::default(),
                    display: None,
                },
                None,
            );
            if probe
                .request_adapter(&Default::default(), wgt::Backends::VULKAN, None)
                .is_err()
            {
                eprintln!("Skipping Vulkan regression: no Vulkan adapter available");
                return None;
            }
            drop(probe);
            let desc = DeviceDesc {
                backend: backend::VULKAN,
                debug: FALSE,
                required_features,
                push_constants_size: 16,
                name: c"alco-regression".as_ptr(),
            };
            let mut handle = DeviceHandle::NULL;
            let status = unsafe { device_create(&desc, &mut handle) };
            assert_eq!(status, Status::OK, "{}", last_error());
            assert!(!handle.is_null());
            Some(Self { handle })
        }
    }

    impl Drop for TestDevice {
        fn drop(&mut self) {
            let status = unsafe { device_destroy(self.handle) };
            assert_eq!(status, Status::OK, "{}", last_error());
        }
    }

    /// Copies the latest thread-local ABI failure before another failure replaces it.
    pub fn last_error() -> String {
        let mut info = ErrorInfo {
            status: 0,
            message: std::ptr::null(),
        };
        unsafe {
            crate::entry::get_last_error(&mut info);
            if info.message.is_null() {
                String::new()
            } else {
                CStr::from_ptr(info.message).to_string_lossy().into_owned()
            }
        }
    }
}
