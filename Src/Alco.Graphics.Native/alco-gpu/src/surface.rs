//! Pointer-owned surfaces and their acquire/present lifecycle. The public
//! surface pointer owns one raw Arc reference; acquired textures and their views
//! retain the parent independently. The platform window must outlive the last
//! child owner, not merely the public surface handle. All acquisition changes
//! are serialized by an inline per-surface mutex, without wrapper registries.

use crate::abi::*;
use crate::convert::*;
use crate::device::DeviceCtx;
use crate::entry::{set_error, set_error_from};
use crate::handle::Handle;
use crate::objects::TextureObj;
use std::ffi::c_char;
use std::sync::{Arc, Mutex};
use wgpu_core as wgc;
use wgpu_types as wgt;

/// <summary>
/// Shared owner of a core surface and its device context. The core surface is
/// dropped only after the public handle and all acquired child owners are gone.
/// </summary>
pub struct SurfaceObj {
    /// <summary>The context that owns the surface's core registration.</summary>
    pub ctx: Arc<DeviceCtx>,
    /// <summary>The core surface identity.</summary>
    pub id: wgc::id::SurfaceId,
    state: Mutex<SurfaceState>,
}

impl Drop for SurfaceObj {
    fn drop(&mut self) {
        self.ctx.global.surface_drop(self.id);
    }
}

fn surface_into_handle<T>(owner: Arc<T>) -> Handle<T> {
    Handle(Arc::into_raw(owner).cast_mut())
}

// Only acquisition creates another texture owner. Ordinary surface operations
// borrow Handle::get without touching the Arc reference count.
unsafe fn clone_surface_owner<T>(handle: Handle<T>) -> Arc<T> {
    Arc::increment_strong_count(handle.0);
    Arc::from_raw(handle.0)
}

unsafe fn take_surface_owner<T>(handle: Handle<T>) -> Result<Arc<T>, Status> {
    if handle.is_null() {
        Err(Status::INVALID_HANDLE)
    } else {
        Ok(Arc::from_raw(handle.0))
    }
}

struct SurfaceState {
    /// Last configured format. wgpu-core builds every acquired surface
    /// texture's descriptor from the configuration, so these values are the
    /// authoritative texture info reported through `texture_get_info`.
    format: u32,
    /// Last configured width in texels.
    width: u32,
    /// Last configured height in texels.
    height: u32,
    /// Current unpresented texture, distinct from older unreleased handles.
    acquired_texture: Option<wgc::id::TextureId>,
}

/// C# `SurfaceSource` discriminant (mirrors `SurfaceHandle.cs`).
pub mod surface_tag {
    /// Win32 HWND (+ optional HINSTANCE in `display`).
    pub const WIN32: u32 = 0;
    /// CAMetalLayer pointer.
    pub const METAL_LAYER: u32 = 1;
    /// wl_display + wl_surface.
    pub const WAYLAND: u32 = 2;
    /// xcb connection + window.
    pub const XCB: u32 = 3;
    /// Xlib Display* + window.
    pub const XLIB: u32 = 4;
    /// ANativeWindow*.
    pub const ANDROID: u32 = 5;
}

/// C# acquire-status codes (wgpu-native compatible).
pub mod acquire_status {
    /// A usable texture with an optimal surface configuration.
    pub const SUCCESS_OPTIMAL: u32 = 0;
    /// A usable texture whose surface configuration may need updating.
    pub const SUCCESS_SUBOPTIMAL: u32 = 1;
    /// Acquisition timed out or the window was occluded.
    pub const TIMEOUT: u32 = 2;
    /// The surface configuration is outdated.
    pub const OUTDATED: u32 = 3;
    /// The surface was lost.
    pub const LOST: u32 = 4;
    /// Acquisition failed validation.
    pub const ERROR: u32 = 5;
}

/// Raw platform handles used to create a surface through the C ABI.
#[repr(C)]
pub struct SurfaceDesc {
    /// Platform discriminant from `surface_tag`.
    pub tag: u32,
    /// hwnd / CAMetalLayer* / wl_surface* / xcb window / Xlib window / ANativeWindow*.
    pub handle: u64,
    /// Win32 HINSTANCE (0 = none) / wl_display* / xcb connection* / Xlib Display*.
    pub display: u64,
    /// Optional NUL-terminated UTF-8 debug label.
    pub name: *const c_char,
}

/// Supported surface formats and present modes returned through the C ABI.
#[repr(C)]
pub struct SurfaceCaps {
    /// Supported C# pixel-format values, limited to `format_count` entries.
    pub formats: [u32; 64],
    /// Number of initialized entries in `formats`.
    pub format_count: u32,
    /// C# present modes: 0 Fifo, 1 Immediate, 2 Mailbox.
    pub present_modes: [u32; 8],
    /// Number of initialized entries in `present_modes`.
    pub present_mode_count: u32,
}

/// Swapchain configuration accepted by the C ABI.
#[repr(C)]
pub struct SurfaceConfig {
    /// C# `TextureUsage` bits (RenderAttachment and optionally TextureBinding).
    pub usage: u32,
    /// C# pixel-format value.
    pub format: u32,
    /// Requested texture width in texels, clamped to at least one.
    pub width: u32,
    /// Requested texture height in texels, clamped to at least one.
    pub height: u32,
    /// C# `PresentMode`: 0 Fifo, 1 Immediate, 2 Mailbox.
    pub present_mode: u32,
    /// C# `CompositeAlphaMode`.
    pub alpha_mode: u32,
    /// Desired maximum number of frames in flight, clamped to at least one.
    pub desired_frame_latency: u32,
}

fn status_to_abi(status: wgt::SurfaceStatus) -> u32 {
    match status {
        wgt::SurfaceStatus::Good => acquire_status::SUCCESS_OPTIMAL,
        wgt::SurfaceStatus::Suboptimal => acquire_status::SUCCESS_SUBOPTIMAL,
        wgt::SurfaceStatus::Timeout | wgt::SurfaceStatus::Occluded => acquire_status::TIMEOUT,
        wgt::SurfaceStatus::Outdated => acquire_status::OUTDATED,
        wgt::SurfaceStatus::Lost => acquire_status::LOST,
        wgt::SurfaceStatus::Validation => acquire_status::ERROR,
    }
}

fn present_mode(v: u32) -> Result<wgt::PresentMode, Status> {
    Ok(match v {
        0 => wgt::PresentMode::Fifo,
        1 => wgt::PresentMode::Immediate,
        2 => wgt::PresentMode::Mailbox,
        other => {
            set_error(
                Status::INVALID_ARGUMENT,
                format!("invalid present mode {other}"),
            );
            return Err(Status::INVALID_ARGUMENT);
        }
    })
}

fn alpha_mode(v: u32) -> Result<wgt::CompositeAlphaMode, Status> {
    Ok(match v {
        0 => wgt::CompositeAlphaMode::Auto,
        1 => wgt::CompositeAlphaMode::Opaque,
        2 => wgt::CompositeAlphaMode::PreMultiplied,
        3 => wgt::CompositeAlphaMode::PostMultiplied,
        4 => wgt::CompositeAlphaMode::Inherit,
        other => {
            set_error(
                Status::INVALID_ARGUMENT,
                format!("invalid alpha mode {other}"),
            );
            return Err(Status::INVALID_ARGUMENT);
        }
    })
}

/// <summary>Creates a surface from raw platform handles.</summary>
/// <param name="device">The device whose context owns the surface.</param>
/// <param name="desc">The platform handles and optional label.</param>
/// <param name="out">Receives one Arc-backed surface owner.</param>
/// <returns>The creation status.</returns>
///
/// # Safety
/// `device` must be null or a live device pointer. `desc` and `out` must be valid.
/// The platform handles must remain alive until all acquired textures and views
/// have been released, even if the public surface owner is destroyed first.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_surface(
    device: DeviceHandle,
    desc: *const SurfaceDesc,
    out: *mut SurfaceHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(Status::INVALID_ARGUMENT, "null descriptor or out pointer");
                return Status::INVALID_ARGUMENT;
            }
        };
        *out = SurfaceHandle::NULL;
        let ctx = match device.get() {
            Ok(owner) => &owner.ctx,
            Err(status) => {
                set_error(status, "invalid device handle");
                return status;
            }
        };
        {
            use raw_window_handle as rwh;
            use std::num::NonZeroIsize;
            use std::ptr::NonNull;

            let handle_ptr = |v: u64| NonNull::<std::ffi::c_void>::new(v as usize as *mut _);
            let nonzero_isize = |v: u64| NonZeroIsize::new(v as isize);

            let result: Result<wgc::id::SurfaceId, Status> = (|| match desc.tag {
                surface_tag::WIN32 => {
                    let hwnd = nonzero_isize(desc.handle).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null hwnd");
                        Status::INVALID_ARGUMENT
                    })?;
                    let mut window = rwh::Win32WindowHandle::new(hwnd);
                    window.hinstance = nonzero_isize(desc.display);
                    ctx.global
                        .instance_create_surface(
                            Some(rwh::RawDisplayHandle::Windows(
                                rwh::WindowsDisplayHandle::new(),
                            )),
                            rwh::RawWindowHandle::Win32(window),
                            None,
                        )
                        .map_err(|e| {
                            set_error_from(Status::UNSUPPORTED, &e);
                            Status::UNSUPPORTED
                        })
                }
                surface_tag::WAYLAND => {
                    let surface = handle_ptr(desc.handle).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null wl_surface");
                        Status::INVALID_ARGUMENT
                    })?;
                    let display = handle_ptr(desc.display).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null wl_display");
                        Status::INVALID_ARGUMENT
                    })?;
                    ctx.global
                        .instance_create_surface(
                            Some(rwh::RawDisplayHandle::Wayland(
                                rwh::WaylandDisplayHandle::new(display),
                            )),
                            rwh::RawWindowHandle::Wayland(rwh::WaylandWindowHandle::new(surface)),
                            None,
                        )
                        .map_err(|e| {
                            set_error_from(Status::UNSUPPORTED, &e);
                            Status::UNSUPPORTED
                        })
                }
                surface_tag::XCB => {
                    let window =
                        std::num::NonZeroU32::new(desc.handle as u32).ok_or_else(|| {
                            set_error(Status::INVALID_ARGUMENT, "zero xcb window");
                            Status::INVALID_ARGUMENT
                        })?;
                    let connection = handle_ptr(desc.display).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null xcb connection");
                        Status::INVALID_ARGUMENT
                    })?;
                    ctx.global
                        .instance_create_surface(
                            Some(rwh::RawDisplayHandle::Xcb(rwh::XcbDisplayHandle::new(
                                Some(connection),
                                0,
                            ))),
                            rwh::RawWindowHandle::Xcb(rwh::XcbWindowHandle::new(window)),
                            None,
                        )
                        .map_err(|e| {
                            set_error_from(Status::UNSUPPORTED, &e);
                            Status::UNSUPPORTED
                        })
                }
                surface_tag::XLIB => {
                    let display = handle_ptr(desc.display).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null Xlib display");
                        Status::INVALID_ARGUMENT
                    })?;
                    ctx.global
                        .instance_create_surface(
                            Some(rwh::RawDisplayHandle::Xlib(rwh::XlibDisplayHandle::new(
                                Some(display),
                                0,
                            ))),
                            rwh::RawWindowHandle::Xlib(rwh::XlibWindowHandle::new(
                                desc.handle as std::ffi::c_ulong,
                            )),
                            None,
                        )
                        .map_err(|e| {
                            set_error_from(Status::UNSUPPORTED, &e);
                            Status::UNSUPPORTED
                        })
                }
                surface_tag::ANDROID => {
                    let window = handle_ptr(desc.handle).ok_or_else(|| {
                        set_error(Status::INVALID_ARGUMENT, "null ANativeWindow");
                        Status::INVALID_ARGUMENT
                    })?;
                    ctx.global
                        .instance_create_surface(
                            Some(rwh::RawDisplayHandle::Android(
                                rwh::AndroidDisplayHandle::new(),
                            )),
                            rwh::RawWindowHandle::AndroidNdk(rwh::AndroidNdkWindowHandle::new(
                                window,
                            )),
                            None,
                        )
                        .map_err(|e| {
                            set_error_from(Status::UNSUPPORTED, &e);
                            Status::UNSUPPORTED
                        })
                }
                surface_tag::METAL_LAYER => {
                    #[cfg(target_os = "macos")]
                    {
                        let layer = desc.handle as *mut std::ffi::c_void;
                        if layer.is_null() {
                            set_error(Status::INVALID_ARGUMENT, "null CAMetalLayer");
                            return Err(Status::INVALID_ARGUMENT);
                        }
                        ctx.global
                            .instance_create_surface_metal(layer, None)
                            .map_err(|e| {
                                set_error_from(Status::UNSUPPORTED, &e);
                                Status::UNSUPPORTED
                            })
                    }
                    #[cfg(not(target_os = "macos"))]
                    {
                        let _ = desc.handle;
                        set_error(
                            Status::UNSUPPORTED,
                            "metal layer surfaces require a macOS build",
                        );
                        Err(Status::UNSUPPORTED)
                    }
                }
                other => {
                    set_error(
                        Status::INVALID_ARGUMENT,
                        format!("unsupported surface tag {other}"),
                    );
                    Err(Status::INVALID_ARGUMENT)
                }
            })();

            match result {
                Ok(id) => {
                    let surface = Arc::new(SurfaceObj {
                        ctx: Arc::clone(ctx),
                        id,
                        state: Mutex::new(SurfaceState {
                            format: 0,
                            width: 0,
                            height: 0,
                            acquired_texture: None,
                        }),
                    });
                    *out = surface_into_handle(surface);
                    Status::OK
                }
                Err(status) => status,
            }
        }
    })
}

/// <summary>Returns capabilities for the surface owner's adapter.</summary>
/// <param name="surface">The live surface pointer.</param>
/// <param name="out">Receives supported formats and present modes.</param>
/// <returns>The query status.</returns>
///
/// # Safety
/// `surface` must be null or live for this call. `out` must be writable.
#[no_mangle]
pub unsafe extern "C-unwind" fn surface_get_capabilities(
    surface: SurfaceHandle,
    out: *mut SurfaceCaps,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let surface = match surface.get() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid surface handle");
                return status;
            }
        };
        let ctx = &surface.ctx;
        match ctx
            .global
            .surface_get_capabilities(surface.id, ctx.adapter_id)
        {
            Ok(caps) => {
                (*out).format_count = caps.formats.len().min(64) as u32;
                for index in 0..(*out).format_count as usize {
                    (*out).formats[index] = pixel_format_to_abi(caps.formats[index]);
                }
                (*out).present_mode_count = caps.present_modes.len().min(8) as u32;
                for index in 0..(*out).present_mode_count as usize {
                    (*out).present_modes[index] = present_mode_to_abi(caps.present_modes[index]);
                }
                Status::OK
            }
            Err(error) => {
                set_error_from(Status::UNSUPPORTED, &error);
                Status::UNSUPPORTED
            }
        }
    })
}

/// <summary>Configures the surface's swapchain using its owning device.</summary>
/// <param name="surface">The live surface pointer.</param>
/// <param name="config">The requested swapchain configuration.</param>
/// <returns>The configuration status.</returns>
///
/// # Safety
/// `surface` must be null or live for this call. `config` must be readable.
#[no_mangle]
pub unsafe extern "C-unwind" fn surface_configure(
    surface: SurfaceHandle,
    config: *const SurfaceConfig,
) -> Status {
    crate::entry::guard(|| {
        let config = match config.as_ref() {
            Some(c) => c,
            None => {
                set_error(Status::INVALID_ARGUMENT, "null config pointer");
                return Status::INVALID_ARGUMENT;
            }
        };
        let surface_obj = match surface.get() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid surface handle");
                return status;
            }
        };
        let ctx = &surface_obj.ctx;
        {
            let mut state = surface_obj.state.lock().unwrap();
            let format = match pixel_format(config.format) {
                Ok(f) => f,
                Err(s) => return s,
            };
            let present_mode = match present_mode(config.present_mode) {
                Ok(m) => m,
                Err(s) => return s,
            };
            let alpha_mode = match alpha_mode(config.alpha_mode) {
                Ok(m) => m,
                Err(s) => return s,
            };
            let wconfig = wgt::SurfaceConfiguration {
                usage: texture_usage(config.usage),
                format,
                color_space: wgt::SurfaceColorSpace::Auto,
                width: config.width.max(1),
                height: config.height.max(1),
                present_mode,
                desired_maximum_frame_latency: config.desired_frame_latency.max(1),
                alpha_mode,
                view_formats: Vec::new(),
            };
            match ctx
                .global
                .surface_configure(surface_obj.id, ctx.device_id, &wconfig)
            {
                None => {
                    state.format = config.format;
                    state.width = wconfig.width;
                    state.height = wconfig.height;
                    Status::OK
                }
                Some(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Status::VALIDATION
                }
            }
        }
    })
}

/// <summary>
/// Acquires a release-only texture that retains the surface. A non-success
/// acquisition status returns a null texture without recording an ABI error.
/// </summary>
/// <param name="surface">The live surface pointer.</param>
/// <param name="out_texture">Receives the acquired texture owner, or null.</param>
/// <param name="out_status">Receives an acquire_status code.</param>
/// <returns>The acquisition call status.</returns>
///
/// # Safety
/// `surface` must be null or live for this call. Both outputs must be writable.
#[no_mangle]
pub unsafe extern "C-unwind" fn surface_get_current_texture(
    surface: SurfaceHandle,
    out_texture: *mut TextureHandle,
    out_status: *mut u32,
) -> Status {
    crate::entry::guard(|| {
        if out_texture.is_null() || out_status.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        *out_texture = TextureHandle::NULL;
        *out_status = acquire_status::ERROR;
        let surface_obj = match surface.get() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid surface handle");
                return status;
            }
        };
        let ctx = &surface_obj.ctx;
        {
            let mut state = surface_obj.state.lock().unwrap();
            let (format, width, height) = (state.format, state.width, state.height);
            match ctx.global.surface_get_current_texture(surface_obj.id, None) {
                Ok(output) => {
                    *out_status = status_to_abi(output.status);
                    match output.texture {
                        Some(texture_id) => {
                            // wgpu-core derives the acquired texture's descriptor
                            // from the surface configuration, so the recorded
                            // format/size are what get_current_texture produced.
                            let handle = TextureHandle::new(TextureObj {
                                ctx: Arc::clone(ctx),
                                id: texture_id,
                                width,
                                height,
                                depth_or_array_layers: 1,
                                mip_level_count: 1,
                                format,
                                is_surface_texture: true,
                                surface: Some(clone_surface_owner(surface)),
                            });
                            state.acquired_texture = Some(texture_id);
                            *out_texture = handle;
                            Status::OK
                        }
                        None => {
                            *out_texture = TextureHandle::NULL;
                            Status::OK
                        }
                    }
                }
                Err(e) => {
                    *out_texture = TextureHandle::NULL;
                    *out_status = acquire_status::ERROR;
                    set_error_from(Status::VALIDATION, &e);
                    Status::VALIDATION
                }
            }
        }
    })
}

/// <summary>Presents the current surface texture.</summary>
/// <param name="surface">The live surface pointer.</param>
/// <param name="out_status">Optional output for an acquire_status code.</param>
/// <returns>The presentation call status.</returns>
///
/// # Safety
/// `surface` must be null or live for this call. A non-null output must be writable.
#[no_mangle]
pub unsafe extern "C-unwind" fn surface_present(
    surface: SurfaceHandle,
    out_status: *mut u32,
) -> Status {
    crate::entry::guard(|| {
        let surface_obj = match surface.get() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid surface handle");
                return status;
            }
        };
        let ctx = &surface_obj.ctx;
        {
            let mut state = surface_obj.state.lock().unwrap();
            match ctx.global.surface_present(surface_obj.id) {
                Ok(status) => {
                    // Core consumes the acquisition even for a non-Good status.
                    // Older texture handles may remain alive until their release.
                    state.acquired_texture = None;
                    if !out_status.is_null() {
                        *out_status = status_to_abi(status);
                    }
                    Status::OK
                }
                Err(e) => {
                    // Some core errors occur before taking the acquisition;
                    // leave it available for the release path to clean up.
                    set_error_from(Status::VALIDATION, &e);
                    Status::VALIDATION
                }
            }
        }
    })
}

/// <summary>
/// Retires an acquired texture exactly once without ABI diagnostics or callbacks.
/// Taking its parent first disarms TextureObj::Drop after an explicit release.
/// </summary>
/// <param name="texture">The acquired texture wrapper to clean up.</param>
/// <returns>The discard error, if cleanup required a fallback.</returns>
pub(crate) fn cleanup_texture(texture: &mut TextureObj) -> Result<(), wgc::present::SurfaceError> {
    let Some(surface) = texture.surface.take() else {
        return Ok(());
    };
    let global = &surface.ctx.global;
    let mut state = surface
        .state
        .lock()
        .unwrap_or_else(|poison| poison.into_inner());
    let mut result = Ok(());
    if state.acquired_texture == Some(texture.id) {
        match global.surface_texture_discard(surface.id) {
            Ok(()) => {}
            Err(error) => {
                // Device validity is checked before core takes its acquired Arc.
                // Release bypasses that check; destroy then snatches HAL metadata
                // even when a retained view still owns the core texture.
                let _ = global.surface_texture_release(surface.id);
                global.texture_destroy(texture.id);
                if !matches!(
                    error,
                    wgc::present::SurfaceError::NothingToPresent
                        | wgc::present::SurfaceError::NotConfigured
                ) {
                    result = Err(error);
                }
            }
        }
        state.acquired_texture = None;
    } else {
        // Present may fail after taking the core acquisition but before snatching
        // its HAL metadata. Only retire this texture, never a newer acquisition.
        global.texture_destroy(texture.id);
    }
    global.texture_drop(texture.id);
    // The last Arc may execute SurfaceObj::Drop. Never run it under this mutex.
    drop(state);
    drop(surface);
    result
}

/// <summary>
/// Consumes an acquired surface texture, discarding it if unpresented. A valid
/// surface-texture owner is consumed even if cleanup reports an error. Rejected
/// regular textures retain their owner and must use texture_destroy.
/// </summary>
/// <param name="texture">The texture owner to consume.</param>
/// <returns>The release status, including any cleanup failure.</returns>
///
/// # Safety
/// `texture` must be null or a live texture pointer. All uses of a non-null
/// texture must have ended, and the caller must never use or release it again.
#[no_mangle]
pub unsafe extern "C-unwind" fn texture_release(texture: TextureHandle) -> Status {
    crate::entry::guard(|| {
        match texture.get() {
            Ok(obj) if obj.is_surface_texture => {}
            Ok(_) => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    "texture is not a surface texture; use texture_destroy",
                );
                return Status::INVALID_ARGUMENT;
            }
            Err(status) => {
                set_error(status, "invalid texture handle");
                return status;
            }
        }
        let mut owner = texture.take().expect("validated texture owner");
        let result = cleanup_texture(&mut owner);
        drop(owner);
        match result {
            Ok(()) => Status::OK,
            Err(error) => {
                set_error_from(Status::VALIDATION, &error);
                Status::VALIDATION
            }
        }
    })
}

/// <summary>
/// Consumes the public surface's Arc owner. Acquired textures and views keep
/// the core surface alive until their last owner is released.
/// </summary>
/// <param name="surface">The public surface owner to consume.</param>
/// <returns>The destruction status.</returns>
///
/// # Safety
/// `surface` must be null or the live Arc-backed pointer returned by creation.
/// All public-handle borrows must have ended. Never use this pointer again, and
/// keep the platform window alive until all acquired texture/view owners end.
#[no_mangle]
pub unsafe extern "C-unwind" fn surface_destroy(surface: SurfaceHandle) -> Status {
    crate::entry::guard(|| match take_surface_owner(surface) {
        Ok(owner) => {
            drop(owner);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid surface handle");
            status
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    struct Tracked(Arc<AtomicUsize>);

    impl Drop for Tracked {
        fn drop(&mut self) {
            self.0.fetch_add(1, Ordering::Relaxed);
        }
    }

    #[test]
    fn public_surface_pointer_is_the_arc_payload_without_an_extra_box() {
        let owner = Arc::new(7u32);
        let address = Arc::as_ptr(&owner);
        let weak = Arc::downgrade(&owner);
        let handle = surface_into_handle(owner);
        assert_eq!(handle.0.cast_const(), address);
        assert_eq!(
            std::mem::size_of_val(&handle),
            std::mem::size_of::<*mut u32>()
        );
        assert_eq!(weak.strong_count(), 1);
        for _ in 0..32 {
            assert_eq!(unsafe { *handle.get().unwrap() }, 7);
            assert_eq!(weak.strong_count(), 1);
        }
        drop(unsafe { take_surface_owner(handle) }.unwrap());
        assert!(weak.upgrade().is_none());
    }

    #[test]
    fn acquired_child_owns_original_parent_after_public_owner_is_consumed() {
        let drops = Arc::new(AtomicUsize::new(0));
        let original = surface_into_handle(Arc::new(Tracked(Arc::clone(&drops))));
        let child = unsafe { clone_surface_owner(original) };
        let weak = Arc::downgrade(&child);
        assert_eq!(weak.strong_count(), 2);
        drop(unsafe { take_surface_owner(original) }.unwrap());
        assert_eq!(weak.strong_count(), 1);
        assert_eq!(drops.load(Ordering::Relaxed), 0);
        let replacement = surface_into_handle(Arc::new(Tracked(Arc::clone(&drops))));
        assert_ne!(replacement.0.cast_const(), Arc::as_ptr(&child));
        drop(child);
        assert_eq!(drops.load(Ordering::Relaxed), 1);
        assert!(weak.upgrade().is_none());
        drop(unsafe { take_surface_owner(replacement) }.unwrap());
        assert_eq!(drops.load(Ordering::Relaxed), 2);
    }

    #[test]
    fn null_public_surface_owner_is_rejected() {
        assert!(matches!(
            unsafe { take_surface_owner(Handle::<u32>::NULL) },
            Err(Status::INVALID_HANDLE)
        ));
    }

    fn state() -> Mutex<SurfaceState> {
        Mutex::new(SurfaceState {
            format: 0,
            width: 0,
            height: 0,
            acquired_texture: None,
        })
    }

    #[test]
    fn acquisition_identity_distinguishes_old_presented_textures() {
        let state = state();
        let first = wgc::id::TextureId::zip(0, 1);
        let second = wgc::id::TextureId::zip(1, 1);
        let mut state = state.lock().unwrap();
        state.acquired_texture = Some(first);
        state.acquired_texture = None;
        state.acquired_texture = Some(second);
        assert_ne!(state.acquired_texture, Some(first));
        assert_eq!(state.acquired_texture, Some(second));
    }

    #[test]
    fn surface_state_lock_does_not_lock_other_surfaces() {
        let first = state();
        let second = state();
        let _state = first.lock().unwrap();
        std::thread::scope(|scope| {
            scope
                .spawn(|| {
                    assert!(matches!(
                        first.try_lock(),
                        Err(std::sync::TryLockError::WouldBlock)
                    ));
                    second.try_lock().unwrap().width = 128;
                    let temporary = surface_into_handle(Arc::new(9));
                    assert_eq!(*unsafe { take_surface_owner(temporary) }.unwrap(), 9);
                })
                .join()
                .unwrap();
        });
        assert_eq!(second.lock().unwrap().width, 128);
    }
}
