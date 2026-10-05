//! Entry-point guard machinery: every exported ABI function runs its body
//! through [`guard`], which wraps it in `catch_unwind` so a Rust panic becomes
//! `AlcoStatus::PANIC` plus a thread-local message instead of an abort at the
//! FFI boundary (an abort would kill the host process before the C# runtime
//! could react). The crate MUST be built with `panic = "unwind"`.
//!
//! Failure protocol: a body that returns a non-OK status should first record a
//! message via [`set_error`]; if it does not, [`guard`] stores a generic
//! message for that status. The thread-local slot is cleared at guard entry so
//! every failure carries a fresh message; C# reads it with
//! `alco_get_last_error` (which deliberately bypasses the guard).

use crate::abi::{AlcoErrorInfo, AlcoStatus};
use std::cell::RefCell;
use std::ffi::CString;
use std::sync::atomic::{AtomicBool, Ordering};

thread_local! {
    static LAST_ERROR: RefCell<Option<(AlcoStatus, CString)>> = const { RefCell::new(None) };
}

static HOOK_INSTALLED: AtomicBool = AtomicBool::new(false);

/// Installs a process-wide panic hook (once) that silences the default stderr
/// "thread panicked" report. Panic payloads are recovered from
/// `catch_unwind`'s `Err` value instead.
fn install_silencing_hook() {
    if !HOOK_INSTALLED.swap(true, Ordering::Relaxed) {
        std::panic::set_hook(Box::new(|_info| {}));
    }
}

/// Records a failure with a human-readable message on the current thread.
pub(crate) fn set_error(status: AlcoStatus, message: impl Into<String>) {
    let msg = message.into();
    LAST_ERROR.with(|slot| {
        *slot.borrow_mut() = Some((
            status,
            // Replace interior NULs so the C string stays faithful.
            CString::new(msg.replace('\0', "\\0"))
                .unwrap_or_else(|_| CString::new("invalid error message").unwrap()),
        ));
    });
}

/// Records a failure from any `Display` error (wgpu-core errors).
pub(crate) fn set_error_from(status: AlcoStatus, error: &dyn std::fmt::Display) {
    set_error(status, error.to_string());
}

/// ABI export: copies the thread-local last error into `out`. The message is
/// borrowed until the next alco call on this thread.
///
/// # Safety
/// `out` must be a valid pointer to an `AlcoErrorInfo`.
#[no_mangle]
pub unsafe extern "C" fn alco_get_last_error(out: *mut AlcoErrorInfo) {
    if out.is_null() {
        return;
    }
    LAST_ERROR.with(|slot| {
        match slot.borrow().as_ref() {
            Some((status, message)) => {
                (*out).status = status.0;
                (*out).message = message.as_ptr();
            }
            None => {
                (*out).status = AlcoStatus::OK.0;
                (*out).message = std::ptr::null();
            }
        }
    });
}

/// Runs an ABI entry body under panic protection. See the module docs for the
/// failure protocol.
pub(crate) fn guard(body: impl FnOnce() -> AlcoStatus + std::panic::UnwindSafe) -> AlcoStatus {
    install_silencing_hook();
    LAST_ERROR.with(|slot| *slot.borrow_mut() = None);

    match std::panic::catch_unwind(body) {
        Ok(status) => {
            if !status.is_ok() {
                let clean = LAST_ERROR.with(|slot| slot.borrow().is_none());
                if clean {
                    set_error(status, default_message(status));
                }
            }
            status
        }
        Err(payload) => {
            let message = payload
                .downcast_ref::<String>()
                .cloned()
                .or_else(|| payload.downcast_ref::<&str>().map(|s| s.to_string()))
                .unwrap_or_else(|| "panic with non-string payload".to_string());
            set_error(AlcoStatus::PANIC, message);
            AlcoStatus::PANIC
        }
    }
}

/// Runs an infallible body (`()` return) under panic protection.
#[allow(dead_code)]
pub(crate) fn guard_unit(body: impl FnOnce() + std::panic::UnwindSafe) -> AlcoStatus {
    guard(|| {
        body();
        AlcoStatus::OK
    })
}

fn default_message(status: AlcoStatus) -> &'static str {
    match status {
        AlcoStatus::INVALID_HANDLE => "invalid or destroyed object handle",
        AlcoStatus::INVALID_ARGUMENT => "invalid argument",
        AlcoStatus::VALIDATION => "operation failed validation",
        AlcoStatus::OUT_OF_MEMORY => "out of memory",
        AlcoStatus::DEVICE_LOST => "device lost",
        AlcoStatus::UNSUPPORTED => "operation unsupported on this device",
        AlcoStatus::NOT_READY => "operation still pending",
        _ => "operation failed",
    }
}
