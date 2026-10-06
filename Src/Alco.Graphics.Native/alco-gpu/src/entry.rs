//! Entry-point guard machinery: every exported ABI function runs its body
//! through [`guard`], which wraps it in `catch_unwind` so a Rust panic becomes
//! `Status::PANIC` plus a thread-local message instead of an abort at the
//! FFI boundary (an abort would kill the host process before the C# runtime
//! could react). The crate MUST be built with `panic = "unwind"`.
//!
//! Failure protocol: a body returning a failure must record its own fresh
//! diagnostic through [`set_error`], [`set_error_from`] or [`fail`]. Success and
//! NOT_READY never touch diagnostic storage. `get_last_error` exposes the
//! latest failure on this thread, not the outcome of a later successful call.
//!
//! Error callback: a host may register a callback with
//! [`set_error_callback`]. [`guard`] invokes it synchronously — strictly
//! AFTER `catch_unwind` has returned, on the calling thread — for every
//! failure status (anything but OK/NOT_READY), passing the thread-local
//! message. The C# host's callback throws a managed exception to unwind out
//! of the alco call; firing after `catch_unwind` guarantees the foreign
//! exception is never captured by it (a captured foreign exception would be
//! swallowed into an opaque payload and resume unwinding at an unspecified
//! point). Exports are declared `extern "C-unwind"` so a foreign unwind
//! passing through their frames is defined behavior. The callback must not
//! call back into the library: a failure on the same thread replaces the
//! thread-local message the callback still holds a pointer to.

use crate::abi::{ErrorInfo, Status};
use std::cell::RefCell;
use std::ffi::{c_char, c_void, CString};
use std::sync::atomic::{AtomicPtr, Ordering};
use std::sync::Once;

thread_local! {
    static LAST_ERROR: RefCell<Option<(Status, CString)>> = const { RefCell::new(None) };
}

static HOOK_INSTALLED: Once = Once::new();

/// Host error callback: `(status, NUL-terminated message, host userdata)`.
/// Declared `C-unwind` because the host throws from it to abort the alco call.
pub type ErrorCallback = extern "C-unwind" fn(u32, *const c_char, *mut c_void);

static ERROR_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());
static ERROR_CALLBACK_USERDATA: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());

/// ABI: registers a process-wide error callback (pass null to unregister).
/// The message is borrowed until the next failure on this thread.
///
/// # Safety
/// Registration changes require quiescent calls. A non-null callback must
/// remain valid until unregistered and must not reenter the library.
#[no_mangle]
pub unsafe extern "C" fn set_error_callback(
    callback: Option<ErrorCallback>,
    userdata: *mut c_void,
) {
    initialize();
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
#[cold]
fn fire_error_callback(status: Status) {
    if status.is_ok() || status.0 == Status::NOT_READY.0 {
        return;
    }
    let function = ERROR_CALLBACK.load(Ordering::Acquire);
    if function.is_null() {
        return;
    }
    let callback: ErrorCallback = unsafe { std::mem::transmute(function) };
    let userdata = ERROR_CALLBACK_USERDATA.load(Ordering::Acquire);
    let message = LAST_ERROR.with(|slot| {
        slot.borrow()
            .as_ref()
            .map(|(_, message)| message.as_ptr())
            .unwrap_or(std::ptr::null())
    });
    callback(status.0, message, userdata);
}

/// Installs the panic hook during device or callback initialization.
pub(crate) fn initialize() {
    HOOK_INSTALLED.call_once(|| {
        std::panic::set_hook(Box::new(|_info| {}));
    });
}

/// Records a failure with a human-readable message on the current thread.
#[cold]
pub(crate) fn set_error(status: Status, message: impl Into<String>) {
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
#[cold]
pub(crate) fn set_error_from(status: Status, error: &dyn std::error::Error) {
    let mut message = error.to_string();
    let mut source = error.source();
    while let Some(cause) = source {
        message.push_str("\nCaused by: ");
        message.push_str(&cause.to_string());
        source = cause.source();
    }
    set_error(status, message);
}

/// ABI export: copies the latest thread-local failure into `out`.
/// The message is borrowed until the next failure on this thread.
///
/// # Safety
/// `out` must be a valid pointer to an `ErrorInfo`.
#[no_mangle]
pub unsafe extern "C" fn get_last_error(out: *mut ErrorInfo) {
    if out.is_null() {
        return;
    }
    LAST_ERROR.with(|slot| match slot.borrow().as_ref() {
        Some((status, message)) => {
            (*out).status = status.0;
            (*out).message = message.as_ptr();
        }
        None => {
            (*out).status = Status::OK.0;
            (*out).message = std::ptr::null();
        }
    });
}

/// Runs an ABI entry body under panic protection. See the module docs for the
/// failure protocol.
pub(crate) fn guard(body: impl FnOnce() -> Status) -> Status {
    // Panic containment does not promise that interrupted recording remains reusable.
    let status = match std::panic::catch_unwind(std::panic::AssertUnwindSafe(body)) {
        Ok(status) => status,
        Err(payload) => {
            let message = payload
                .downcast_ref::<String>()
                .cloned()
                .or_else(|| payload.downcast_ref::<&str>().map(|s| s.to_string()))
                .unwrap_or_else(|| "panic with non-string payload".to_string());
            set_error(Status::PANIC, message);
            Status::PANIC
        }
    };
    // Fired strictly after catch_unwind has returned; nothing on this frame
    // needs cleanup, so a foreign unwind from the callback passes straight
    // through to the host caller. See the module docs.
    if !status.is_ok() && status != Status::NOT_READY {
        fire_error_callback(status);
    }
    status
}

/// Runs an infallible body (`()` return) under panic protection.
#[allow(dead_code)]
pub(crate) fn guard_unit(body: impl FnOnce() + std::panic::UnwindSafe) -> Status {
    guard(|| {
        body();
        Status::OK
    })
}

/// Records a fresh default diagnostic and returns the supplied failure status.
#[cold]
pub(crate) fn fail(status: Status) -> Status {
    set_error(status, default_message(status));
    status
}

fn default_message(status: Status) -> &'static str {
    match status {
        Status::INVALID_HANDLE => "null object handle",
        Status::INVALID_ARGUMENT => "invalid argument",
        Status::VALIDATION => "operation failed validation",
        Status::OUT_OF_MEMORY => "out of memory",
        Status::DEVICE_LOST => "device lost",
        Status::UNSUPPORTED => "operation unsupported on this device",
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
        let mut info = ErrorInfo {
            status: 0,
            message: std::ptr::null(),
        };
        unsafe {
            get_last_error(&mut info);
            (
                info.status,
                CStr::from_ptr(info.message).to_string_lossy().into_owned(),
            )
        }
    }

    #[test]
    fn complete_error_chain_is_preserved_through_abi() {
        for root in [
            "Depth32Float has no stencil aspect",
            "Rgba8Unorm has no depth aspect",
        ] {
            let error = TestError {
                message: "In a pass parameter",
                source: Some(Box::new(TestError {
                    message: "attachment validation",
                    source: Some(Box::new(TestError {
                        message: root,
                        source: None,
                    })),
                })),
            };
            let status = guard(|| {
                set_error_from(Status::VALIDATION, &error);
                Status::VALIDATION
            });
            assert_eq!(status, Status::VALIDATION);
            assert_eq!(
                last_error(),
                (
                    status.0,
                    format!(
                        "In a pass parameter\nCaused by: attachment validation\nCaused by: {root}"
                    ),
                )
            );
        }
    }

    #[test]
    fn leaf_errors_and_interior_nuls_retain_their_details() {
        let error = TestError {
            message: "resource\0name",
            source: None,
        };
        set_error_from(Status::UNSUPPORTED, &error);
        assert_eq!(
            last_error(),
            (Status::UNSUPPORTED.0, "resource\\0name".into())
        );
    }

    // Records callbacks fired on this thread only: guard calls from
    // concurrently running tests land on their own thread-locals.
    thread_local! {
        static FIRED: RefCell<Option<(u32, String)>> = const { RefCell::new(None) };
        static UNWIND_CALLBACK: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
    }

    /// Serializes the callback tests: registration is process-wide, so a
    /// concurrently registered callback would fire into this test's
    /// thread-local and flake the "must not fire" assertions.
    static CALLBACK_TEST_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

    extern "C-unwind" fn record_callback(
        status: u32,
        message: *const c_char,
        _userdata: *mut c_void,
    ) {
        let text = if message.is_null() {
            String::new()
        } else {
            unsafe { CStr::from_ptr(message) }
                .to_string_lossy()
                .into_owned()
        };
        FIRED.with(|fired| *fired.borrow_mut() = Some((status, text)));
    }

    /// Unregisters the callback on drop so later failures in concurrently or
    /// subsequently running tests never reach the test recorder.
    struct CallbackRegistration;
    impl CallbackRegistration {
        fn install() -> Self {
            Self::install_with(record_callback)
        }

        fn install_with(callback: ErrorCallback) -> Self {
            unsafe { set_error_callback(Some(callback), std::ptr::null_mut()) };
            CallbackRegistration
        }
    }
    impl Drop for CallbackRegistration {
        fn drop(&mut self) {
            unsafe { set_error_callback(None, std::ptr::null_mut()) };
            UNWIND_CALLBACK.with(|unwind| unwind.set(false));
        }
    }

    #[test]
    fn error_callback_fires_with_status_and_thread_local_message() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();

        let status = guard(|| {
            set_error(Status::INVALID_HANDLE, "device handle is invalid");
            Status::INVALID_HANDLE
        });
        assert_eq!(status, Status::INVALID_HANDLE);
        assert_eq!(
            FIRED.with(|fired| fired.borrow().clone()),
            Some((Status::INVALID_HANDLE.0, "device handle is invalid".into()))
        );
    }

    #[test]
    fn error_callback_receives_fresh_default_failure_message() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();

        let status = guard(|| fail(Status::OUT_OF_MEMORY));
        assert_eq!(status, Status::OUT_OF_MEMORY);
        assert_eq!(
            FIRED.with(|fired| fired.borrow().clone()),
            Some((Status::OUT_OF_MEMORY.0, "out of memory".into()))
        );
    }

    #[test]
    fn error_callback_is_silent_for_ok_and_not_ready() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();
        FIRED.with(|fired| *fired.borrow_mut() = None);

        assert_eq!(guard(|| Status::OK), Status::OK);
        assert_eq!(guard(|| Status::NOT_READY), Status::NOT_READY);
        assert_eq!(FIRED.with(|fired| fired.borrow().clone()), None);
    }

    #[test]
    fn success_and_not_ready_preserve_latest_failure_without_notifying() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();
        FIRED.with(|fired| *fired.borrow_mut() = None);
        set_error(Status::VALIDATION, "latest validation failure");
        let mut initial = ErrorInfo {
            status: 0,
            message: std::ptr::null(),
        };
        unsafe { get_last_error(&mut initial) };
        for _ in 0..32 {
            assert_eq!(guard(|| Status::OK), Status::OK);
            assert_eq!(guard(|| Status::NOT_READY), Status::NOT_READY);
            let mut info = ErrorInfo {
                status: 0,
                message: std::ptr::null(),
            };
            unsafe { get_last_error(&mut info) };
            assert_eq!(info.status, Status::VALIDATION.0);
            assert_eq!(info.message, initial.message);
            assert_eq!(FIRED.with(|fired| fired.borrow().clone()), None);
        }
        assert_eq!(guard(|| fail(Status::OUT_OF_MEMORY)), Status::OUT_OF_MEMORY);
        assert_eq!(
            last_error(),
            (Status::OUT_OF_MEMORY.0, "out of memory".into())
        );
    }

    #[test]
    fn panic_payloads_are_preserved_in_error_records_and_callbacks() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install();

        for owned in [false, true] {
            FIRED.with(|fired| *fired.borrow_mut() = None);
            let status = guard(|| {
                if owned {
                    std::panic::panic_any(String::from("panic context\nCaused by: root\0cause"));
                } else {
                    std::panic::panic_any("panic context\nCaused by: root\0cause");
                }
            });
            assert_eq!(status, Status::PANIC);
            let expected = (
                Status::PANIC.0,
                "panic context\nCaused by: root\\0cause".into(),
            );
            assert_eq!(last_error(), expected);
            assert_eq!(FIRED.with(|fired| fired.borrow().clone()), Some(expected));
        }
    }

    extern "C-unwind" fn unwinding_callback(
        status: u32,
        message: *const c_char,
        userdata: *mut c_void,
    ) {
        record_callback(status, message, userdata);
        // Concurrent tests share registration, but must not inherit this unwind.
        if UNWIND_CALLBACK.with(|unwind| unwind.get()) {
            panic!("callback unwind must reach the caller");
        }
    }

    #[test]
    fn callback_unwind_escapes_the_guard_panic_boundary() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        let _registration = CallbackRegistration::install_with(unwinding_callback);
        FIRED.with(|fired| *fired.borrow_mut() = None);
        UNWIND_CALLBACK.with(|unwind| unwind.set(true));

        let payload = std::panic::catch_unwind(|| guard(|| fail(Status::INVALID_ARGUMENT)))
            .expect_err("callback unwind must not become Status::PANIC");
        assert_eq!(
            payload.downcast_ref::<&str>(),
            Some(&"callback unwind must reach the caller")
        );
        let expected = (Status::INVALID_ARGUMENT.0, "invalid argument".into());
        assert_eq!(last_error(), expected);
        assert_eq!(FIRED.with(|fired| fired.borrow().clone()), Some(expected));
    }

    #[test]
    fn panic_hook_is_installed_once_across_threads() {
        // Hooks and Once state are process-wide: isolate both from parallel tests.
        const CHILD: &str = "ALCO_GPU_ENTRY_HOOK_TEST_CHILD";
        if std::env::var_os(CHILD).is_none() {
            let output = std::process::Command::new(std::env::current_exe().unwrap())
                .args([
                    "--exact",
                    "entry::tests::panic_hook_is_installed_once_across_threads",
                    "--test-threads=1",
                ])
                .env(CHILD, "1")
                .output()
                .unwrap();
            assert!(
                output.status.success(),
                "hook test subprocess failed: {output:?}"
            );
            assert!(
                String::from_utf8_lossy(&output.stdout).contains("1 passed"),
                "hook test subprocess did not run its test: {output:?}"
            );
            return;
        }

        use std::sync::atomic::AtomicUsize;
        use std::sync::{Arc, Barrier};

        assert!(!HOOK_INSTALLED.is_completed());
        let calls = Arc::new(AtomicUsize::new(0));
        let hook_calls = Arc::clone(&calls);
        std::panic::set_hook(Box::new(move |_| {
            hook_calls.fetch_add(1, Ordering::Relaxed);
        }));
        let barrier = Arc::new(Barrier::new(8));
        let mut threads = Vec::new();
        for _ in 0..8 {
            let barrier = Arc::clone(&barrier);
            threads.push(std::thread::spawn(move || {
                barrier.wait();
                initialize();
                assert_eq!(guard(|| panic!("initial guarded panic")), Status::PANIC);
                assert!(HOOK_INSTALLED.is_completed());
                assert_eq!(
                    last_error(),
                    (Status::PANIC.0, "initial guarded panic".into())
                );
            }));
        }
        for thread in threads {
            thread.join().unwrap();
        }
        assert_eq!(calls.load(Ordering::Relaxed), 0);

        // A host replacement after initialization must survive later guard calls.
        let hook_calls = Arc::clone(&calls);
        std::panic::set_hook(Box::new(move |_| {
            hook_calls.fetch_add(1, Ordering::Relaxed);
        }));
        for _ in 0..8 {
            assert_eq!(guard(|| panic!("subsequent guarded panic")), Status::PANIC);
        }
        assert_eq!(calls.load(Ordering::Relaxed), 8);
    }

    #[test]
    fn error_callback_does_not_fire_once_unregistered() {
        let _lock = CALLBACK_TEST_LOCK.lock().unwrap();
        FIRED.with(|fired| *fired.borrow_mut() = None);
        assert_eq!(guard(|| fail(Status::VALIDATION)), Status::VALIDATION);
        assert_eq!(FIRED.with(|fired| fired.borrow().clone()), None);
    }
}
