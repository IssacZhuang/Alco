//! Native log forwarding. wgpu-core reports internal diagnostics — root
//! causes that never surface through return values, such as the
//! indirect-validation initialization failure that surfaces as
//! `DeviceError::Lost` — exclusively through the `log` crate. Without a
//! `log::Log` implementation installed those records are dropped silently.
//!
//! This module installs a process-wide forwarder that delivers every record
//! to a host callback, mirroring wgpu-native's `wgpuSetLogCallback` /
//! `wgpuSetLogLevel`: the logger is installed lazily on the first callback
//! registration, the level filter defaults to Warn when nothing configured
//! one, and `set_log_level` adjusts the filter later.
//!
//! Unlike the error callback (see `entry.rs`) the log callback is plain
//! `extern "C"` and MUST NOT throw: records fire synchronously from deep
//! inside wgpu-core stack frames that are not `C-unwind`, so a foreign unwind
//! started there is undefined behavior. The message pointer is borrowed for
//! the duration of the call only; the host must copy what it keeps.

use crate::abi::{log_level, Status};
use crate::entry::{guard, set_error};
use log::{Level, LevelFilter, Log, Metadata, Record};
use std::ffi::{c_char, c_void, CString};
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};
use std::sync::Mutex;

/// Host log callback: `(level, NUL-terminated message, host userdata)`.
/// Plain `extern "C"`: it must never unwind (see the module docs).
pub type LogCallback = extern "C" fn(u32, *const c_char, *mut c_void);

static LOG_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());
static LOG_CALLBACK_USERDATA: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());
/// Set once this library owns the process logger; see `ensure_logger_installed`.
static LOGGER_OWNED: AtomicBool = AtomicBool::new(false);
/// Serializes the one-time `log::set_logger` call across threads.
static INSTALL_LOCK: Mutex<()> = Mutex::new(());

struct LogForwarder;

impl Log for LogForwarder {
    fn enabled(&self, _metadata: &Metadata) -> bool {
        // The global max-level filter governs verbosity; nothing else to hide.
        true
    }

    fn log(&self, record: &Record) {
        let function = LOG_CALLBACK.load(Ordering::Acquire);
        if function.is_null() {
            return;
        }
        let callback: LogCallback = unsafe { std::mem::transmute(function) };
        let level = match record.level() {
            Level::Error => log_level::ERROR,
            Level::Warn => log_level::WARN,
            Level::Info => log_level::INFO,
            Level::Debug => log_level::DEBUG,
            Level::Trace => log_level::TRACE,
        };
        let message = CString::new(record.args().to_string().replace('\0', "\\0"))
            .unwrap_or_else(|_| CString::new("invalid log message").unwrap());
        let userdata = LOG_CALLBACK_USERDATA.load(Ordering::Acquire);
        callback(level, message.as_ptr(), userdata);
    }

    fn flush(&self) {}
}

/// Installs the forwarder as the process logger (once). Mirrors wgpu-native:
/// when no level was configured, the first registration also enables Warn so
/// installing the logger never silences error/warn diagnostics.
fn ensure_logger_installed() -> Result<(), Status> {
    if LOGGER_OWNED.load(Ordering::Acquire) {
        return Ok(());
    }
    let _lock = INSTALL_LOCK.lock().unwrap();
    if LOGGER_OWNED.load(Ordering::Acquire) {
        return Ok(());
    }
    static FORWARDER: LogForwarder = LogForwarder;
    match log::set_logger(&FORWARDER) {
        Ok(()) => {
            if log::max_level() == LevelFilter::Off {
                log::set_max_level(LevelFilter::Warn);
            }
            LOGGER_OWNED.store(true, Ordering::Release);
            Ok(())
        }
        Err(_) => {
            // Another library already owns the process logger; records will
            // never reach this forwarder, so report it instead of pretending.
            set_error(
                Status::UNSUPPORTED,
                "a process-wide `log` logger is already installed; native log forwarding is unavailable",
            );
            Err(Status::UNSUPPORTED)
        }
    }
}

/// ABI: registers a process-wide log callback (pass null to unregister) and
/// installs the forwarder on first use. The callback receives every wgpu-core
/// `log` record at or below the configured level, synchronously on the
/// thread that emitted it; the message is borrowed for the duration of the
/// call. The callback must not throw or call back into the library.
///
/// # Safety
/// `callback`, when non-null, must remain valid to call until unregistered.
#[no_mangle]
pub unsafe extern "C-unwind" fn set_log_callback(
    callback: Option<LogCallback>,
    userdata: *mut c_void,
) -> Status {
    guard(|| {
        // Store before installing so records start flowing immediately; if
        // installation fails the stored callback simply never fires.
        LOG_CALLBACK.store(
            callback.map_or_else(std::ptr::null_mut, |f| f as *mut ()),
            Ordering::Release,
        );
        LOG_CALLBACK_USERDATA.store(userdata, Ordering::Release);
        match ensure_logger_installed() {
            Ok(()) => Status::OK,
            Err(status) => status,
        }
    })
}

/// ABI: sets the maximum level forwarded to the log callback. Valid values
/// are the `log_level` constants; anything else fails with
/// `INVALID_ARGUMENT`.
#[no_mangle]
pub unsafe extern "C-unwind" fn set_log_level(level: u32) -> Status {
    guard(|| {
        let filter = match level {
            log_level::OFF => LevelFilter::Off,
            log_level::ERROR => LevelFilter::Error,
            log_level::WARN => LevelFilter::Warn,
            log_level::INFO => LevelFilter::Info,
            log_level::DEBUG => LevelFilter::Debug,
            log_level::TRACE => LevelFilter::Trace,
            _ => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    format!("unknown log level {level}"),
                );
                return Status::INVALID_ARGUMENT;
            }
        };
        log::set_max_level(filter);
        Status::OK
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;

    thread_local! {
        static RECORDED: RefCell<Vec<(u32, String)>> = const { RefCell::new(Vec::new()) };
    }

    extern "C" fn record_callback(level: u32, message: *const c_char, _userdata: *mut c_void) {
        let text = if message.is_null() {
            String::new()
        } else {
            unsafe { std::ffi::CStr::from_ptr(message) }
                .to_string_lossy()
                .into_owned()
        };
        RECORDED.with(|records| records.borrow_mut().push((level, text)));
    }

    /// Serializes the log tests: the logger, callback registration and level
    /// filter are process-wide state shared with concurrently running tests.
    static LOG_TEST_LOCK: Mutex<()> = Mutex::new(());

    /// Restores process-global state (callback unregistered, level reset) so
    /// later tests never receive this test's records.
    struct LogState {
        previous_level: LevelFilter,
    }

    impl LogState {
        fn install() -> Self {
            let previous_level = log::max_level();
            unsafe { set_log_callback(Some(record_callback), std::ptr::null_mut()) };
            LogState { previous_level }
        }
    }

    impl Drop for LogState {
        fn drop(&mut self) {
            unsafe { set_log_callback(None, std::ptr::null_mut()) };
            log::set_max_level(self.previous_level);
        }
    }

    #[test]
    fn records_reach_the_callback_with_level_and_message() {
        let _lock = LOG_TEST_LOCK.lock().unwrap();
        let _state = LogState::install();
        assert_eq!(unsafe { set_log_level(log_level::TRACE) }, Status::OK);
        RECORDED.with(|records| records.borrow_mut().clear());

        log::error!("emitted {}", "error");
        log::warn!("emitted warn");
        log::info!("emitted info");
        log::debug!("emitted debug");
        log::trace!("emitted trace");

        RECORDED.with(|records| {
            assert_eq!(
                records.borrow().as_slice(),
                &[
                    (log_level::ERROR, "emitted error".to_string()),
                    (log_level::WARN, "emitted warn".to_string()),
                    (log_level::INFO, "emitted info".to_string()),
                    (log_level::DEBUG, "emitted debug".to_string()),
                    (log_level::TRACE, "emitted trace".to_string()),
                ]
            );
        });
    }

    #[test]
    fn level_filter_drops_records_above_the_configured_level() {
        let _lock = LOG_TEST_LOCK.lock().unwrap();
        let _state = LogState::install();
        assert_eq!(unsafe { set_log_level(log_level::WARN) }, Status::OK);
        RECORDED.with(|records| records.borrow_mut().clear());

        log::error!("kept");
        log::warn!("kept too");
        log::info!("dropped");
        log::debug!("dropped");

        RECORDED.with(|records| {
            assert_eq!(
                records.borrow().as_slice(),
                &[
                    (log_level::ERROR, "kept".to_string()),
                    (log_level::WARN, "kept too".to_string()),
                ]
            );
        });
    }

    #[test]
    fn callback_stops_receiving_records_once_unregistered() {
        let _lock = LOG_TEST_LOCK.lock().unwrap();
        let state = LogState::install();
        assert_eq!(unsafe { set_log_level(log_level::ERROR) }, Status::OK);
        drop(state);
        RECORDED.with(|records| records.borrow_mut().clear());

        log::error!("must not be delivered");

        RECORDED.with(|records| assert!(records.borrow().is_empty()));
    }

    #[test]
    fn second_registration_keeps_the_logger_installed() {
        let _lock = LOG_TEST_LOCK.lock().unwrap();
        let _state = LogState::install();
        // The first install happened in whichever test ran first; a repeat
        // registration must stay OK and keep forwarding working.
        assert_eq!(
            unsafe { set_log_callback(Some(record_callback), std::ptr::null_mut()) },
            Status::OK
        );
    }

    #[test]
    fn unknown_log_levels_are_rejected() {
        let _lock = LOG_TEST_LOCK.lock().unwrap();
        assert_eq!(unsafe { set_log_level(99) }, Status::INVALID_ARGUMENT);
        assert_eq!(unsafe { set_log_level(u32::MAX) }, Status::INVALID_ARGUMENT);
        for level in [
            log_level::OFF,
            log_level::ERROR,
            log_level::WARN,
            log_level::INFO,
            log_level::DEBUG,
            log_level::TRACE,
        ] {
            assert_eq!(unsafe { set_log_level(level) }, Status::OK);
        }
    }
}
