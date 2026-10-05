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
//!
//! Error callback: a host may register a callback with
//! [`alco_set_error_callback`]. [`guard`] invokes it synchronously — strictly
//! AFTER `catch_unwind` has returned, on the calling thread — for every
//! failure status (anything but OK/NOT_READY), passing the thread-local
//! message. The C# host's callback throws a managed exception to unwind out
//! of the alco call; firing after `catch_unwind` guarantees the foreign
//! exception is never captured by it (a captured foreign exception would be
//! swallowed into an opaque payload and resume unwinding at an unspecified
//! point). Exports are declared `extern "C-unwind"` so a foreign unwind
//! passing through their frames is defined behavior. The callback must not
//! call back into the library: any alco call on the same thread replaces the
//! thread-local message the callback still holds a pointer to.

use crate::abi::{AlcoErrorInfo, AlcoStatus};
use std::cell::RefCell;
use std::ffi::{c_char, c_void, CString};
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};

thread_local! {
    static LAST_ERROR: RefCell<Option<(AlcoStatus, CString)>> = const { RefCell::new(None) };
}

static HOOK_INSTALLED: AtomicBool = AtomicBool::new(false);

/// Host error callback: `(status, NUL-terminated message, host userdata)`.
/// Declared `C-unwind` because the host throws from it to abort the alco call.
pub type AlcoErrorCallback = extern "C-unwind" fn(u32, *const c_char, *mut c_void);

static ERROR_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());
static ERROR_CALLBACK_USERDATA: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());

/// ABI: registers a process-wide error callback (pass null to unregister).
/// The callback is invoked synchronously on the calling thread when a fallible
/// ABI entry fails; `message` is borrowed until the next alco call on the
/// thread. See the module docs for the throwing-host contract.
///
/// # Safety
/// `callback`, when non-null, must remain valid to call until unregistered.
#[no_mangle]
pub unsafe extern "C" fn alco_set_error_callback(
    callback: Option<AlcoErrorCallback>,
    userdata: *mut c_void,
) {
    ERROR_CALLBACK.store(
        callback.map_or_else(std::ptr::null_mut, |f| f as *mut ()),
        Ordering::Release,
    );
    ERROR_CALLBACK_USERDATA.store(userdata, Ordering::Release);
}

/// Invokes the registered error callback for a failure status. OK and
/// NOT_READY are control-flow values and never fire. The thread-local message
/// borrow is released before the call so a re-entering alco call cannot hit
/// an active `RefCell` borrow.
fn fire_error_callback(status: AlcoStatus) {
    if status.is_ok() || status.0 == AlcoStatus::NOT_READY.0 {
        return;
    }
    let function = ERROR_CALLBACK.load(Ordering::Acquire);
    if function.is_null() {
        return;
    }
    let callback: AlcoErrorCallback = unsafe { std::mem::transmute(function) };
    let userdata = ERROR_CALLBACK_USERDATA.load(Ordering::Acquire);
    let message = LAST_ERROR.with(|slot| {
        slot.borrow()
            .as_ref()
            .map(|(_, message)| message.as_ptr())
            .unwrap_or(std::ptr::null())
    });
    callback(status.0, message, userdata);
}

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

/// Records the complete error source chain, retaining context and root causes.
pub(crate) fn set_error_from(status: AlcoStatus, error: &dyn std::error::Error) {
    let mut message = error.to_string();
    let mut source = error.source();
    while let Some(cause) = source {
        message.push_str("\nCaused by: ");
        message.push_str(&cause.to_string());
        source = cause.source();
    }
    set_error(status, message);
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

    let status = match std::panic::catch_unwind(body) {
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
    };
    // Fired strictly after catch_unwind has returned; nothing on this frame
    // needs cleanup, so a foreign unwind from the callback passes straight
    // through to the host caller. See the module docs.
    fire_error_callback(status);
    status
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::error::Error;
    use std::ffi::CStr;
    use std::fmt;

    #[derive(Debug)]
    struct TestError {
        message: &'static str,
        source: Option<Box<TestError>>,
    }

    impl fmt::Display for TestError {
        fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
            f.write_str(self.message)
        }
    }

    impl Error for TestError {
        fn source(&self) -> Option<&(dyn Error + 'static)> {
            self.source.as_deref().map(|e| e as &dyn Error)
        }
    }

    fn last_error() -> (u32, String) {
        let mut info = AlcoErrorInfo { status: 0, message: std::ptr::null() };
        unsafe {
            alco_get_last_error(&mut info);
            (info.status, CStr::from_ptr(info.message).to_string_lossy().into_owned())
        }
    }

    #[test]
    fn complete_error_chain_is_preserved_through_abi() {
        for root in ["Depth32Float has no stencil aspect", "Rgba8Unorm has no depth aspect"] {
            let error = TestError {
                message: "In a pass parameter",
                source: Some(Box::new(TestError {
                    message: "attachment validation",
                    source: Some(Box::new(TestError { message: root, source: None })),
                })),
            };
            let status = guard(|| {
                set_error_from(AlcoStatus::VALIDATION, &error);
                AlcoStatus::VALIDATION
            });
            assert_eq!(status, AlcoStatus::VALIDATION);
            assert_eq!(last_error(), (
                status.0,
                format!("In a pass parameter\nCaused by: attachment validation\nCaused by: {root}"),
            ));
        }
    }

    #[test]
    fn leaf_errors_and_interior_nuls_retain_their_details() {
        let error = TestError { message: "resource\0name", source: None };
        set_error_from(AlcoStatus::UNSUPPORTED, &error);
        assert_eq!(last_error(), (AlcoStatus::UNSUPPORTED.0, "resource\\0name".into()));
    }

    /// Records callbacks fired on this thread only: guard calls from
    /// concurrently running tests land on their own thread-locals.
    thread_local! {
        static FIRED: RefCell<Option<(u32, String)>> = const { RefCell::new(None) };
    }

    /// Serializes the callback tests: registration is process-wide, so a
    /// concurrently registered callback would fire into this test's
    /// thread-local and flake the "must not fire" assertions.
    static CALLBACK_TEST_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

    extern "C-unwind" fn record_callback(status: u32, message: *const c_char, _userdata: *mut c_void) {
        let text = if message.is_null() {
            String::new()
        } else {
            unsafe { CStr::from_ptr(message) }.to_string_lossy().into_owned()
        };
        FIRED.with(|fired| *fired.borrow_mut() = Some((status, text)));
    }

    /// Unregisters the callback on drop so later failures in concurrently or
    /// subsequently running tests never reach the test recorder.
    struct CallbackRegistration;
    impl CallbackRegistration {
        fn install() -> Self {
            unsafe { alco_set_error_callback(Some(record_callback), std::ptr::null_mut()) };
            CallbackRegistration
        }
    }
    impl Drop for CallbackRegistration {
        fn drop(&mut self) {
            unsafe { alco_set_error_callback(None, std::ptr::null_mut()) };
        }
    }

    #[test]
    fn error_callback_fires_with_status_and_thread_local_message() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();

        let status = guard(|| {
            set_error(AlcoStatus::INVALID_HANDLE, "device handle is invalid");
            AlcoStatus::INVALID_HANDLE
        });
        assert_eq!(status, AlcoStatus::INVALID_HANDLE);
        assert_eq!(
            FIRED.with(|fired| fired.borrow().clone()),
            Some((AlcoStatus::INVALID_HANDLE.0, "device handle is invalid".into()))
        );
    }

    #[test]
    fn error_callback_receives_default_message_when_body_set_none() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();

        let status = guard(|| AlcoStatus::OUT_OF_MEMORY);
        assert_eq!(status, AlcoStatus::OUT_OF_MEMORY);
        assert_eq!(
            FIRED.with(|fired| fired.borrow().clone()),
            Some((AlcoStatus::OUT_OF_MEMORY.0, "out of memory".into()))
        );
    }

    #[test]
    fn error_callback_is_silent_for_ok_and_not_ready() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();
        FIRED.with(|fired| *fired.borrow_mut() = None);

        assert_eq!(guard(|| AlcoStatus::OK), AlcoStatus::OK);
        assert_eq!(guard(|| AlcoStatus::NOT_READY), AlcoStatus::NOT_READY);
        assert_eq!(FIRED.with(|fired| fired.borrow().clone()), None);
    }

    #[test]
    fn error_callback_does_not_fire_once_unregistered() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        FIRED.with(|fired| *fired.borrow_mut() = None);
        assert_eq!(guard(|| AlcoStatus::VALIDATION), AlcoStatus::VALIDATION);
        assert_eq!(FIRED.with(|fired| fired.borrow().clone()), None);
    }
}
