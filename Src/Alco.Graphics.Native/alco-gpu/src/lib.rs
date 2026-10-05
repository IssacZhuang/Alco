//! alco-gpu: Alco-owned C ABI over wgpu-core, replacing wgpu-native for the
//! Alco engine. See `Docs/AlcoGpuAbi.md` and `abi.rs` for the ABI contract.
//!
//! Every exported entry point is panic-guarded (see `entry.rs`) and objects are
//! addressed through generational handles (see `handle.rs`) so misuse from C#
//! surfaces as `AlcoStatus` + message rather than a process abort.

pub mod abi;
pub(crate) mod commands;
pub(crate) mod convert;
pub(crate) mod device;
pub(crate) mod objects;
pub(crate) mod pipeline;
pub(crate) mod surface;
pub(crate) mod entry;
pub(crate) mod handle;

use std::ffi::CString;
use std::sync::OnceLock;

/// Alco ABI version implemented by this library: `(major << 16) | minor`.
/// The C# side rejects a major mismatch at load time.
#[no_mangle]
pub extern "C" fn alco_abi_version() -> u32 {
    (abi::ABI_MAJOR << 16) | abi::ABI_MINOR
}

/// Build identifier embedding the pinned wgpu-core version (kept in lockstep
/// with the `=30.0.1` requirement in Cargo.toml).
fn alco_build_id() -> &'static str {
    static BUILD_ID: OnceLock<String> = OnceLock::new();
    BUILD_ID.get_or_init(|| {
        let profile = if cfg!(debug_assertions) { "debug" } else { "release" };
        let hash = option_env!("ALCO_GIT_HASH").unwrap_or("dev");
        format!("alco-gpu {profile} ({hash}) wgpu-core 30.0.1")
    })
}

/// ABI: build/runtime information. The build string is process-lifetime
/// borrowed; `wgpu_version` is the numeric `(major << 16) | minor` of the
/// pinned wgpu-core (30.0 → 0x001E_0000).
///
/// # Safety
/// `out` must be a valid `abi::AlcoBuildInfo` slot.
#[no_mangle]
pub unsafe extern "C" fn alco_build_info(out: *mut abi::AlcoBuildInfo) {
    if out.is_null() {
        return;
    }
    static ALCO_BUILD: OnceLock<CString> = OnceLock::new();
    let build = ALCO_BUILD.get_or_init(|| CString::new(alco_build_id()).unwrap());
    (*out).alco_build = build.as_ptr();
    (*out).wgpu_version = (30u32 << 16) | 0;
}
