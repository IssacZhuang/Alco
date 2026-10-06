//! Surfaces: raw platform handles (matching C# `SurfaceSource`) through
//! raw-window-handle into wgpu-core, capabilities, configuration and the
//! acquire/present cycle. Acquired surface textures enter the device texture
//! table flagged `is_surface_texture` so the C# side must release (never
//! destroy) them. Configuration and acquisition lifecycle operations hold only
//! their surface's state lock, never the device or generational registry lock.

use crate::abi::*;
use crate::convert::*;
use crate::device::{DeviceCtx, DEVICES};
use crate::entry::{set_error, set_error_from};
use crate::handle::HandleTable;
use crate::objects::TextureObj;
use std::ffi::c_char;
use std::sync::{Arc, Mutex};
use wgpu_core as wgc;
use wgpu_types as wgt;

/// Cloneable core surface registration with independently synchronized state.
#[derive(Clone)]
pub(crate) struct SurfaceObj {
    /// Core surface identity.
    pub id: wgc::id::SurfaceId,
    // Only operations on this surface share the lock; registry lookups never
    // retain their lock while configuring, acquiring, presenting, or discarding.
    state: Arc<Mutex<SurfaceState>>,
}

struct SurfaceState {
    /// Last configured format. wgpu-core builds every acquired surface
    /// texture's descriptor from the configuration, so these values are the
    /// authoritative texture info reported through `alco_texture_get_info`.
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
pub struct AlcoSurfaceDesc {
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
pub struct AlcoSurfaceCaps {
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
pub struct AlcoSurfaceConfig {
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

fn status_to_alco(status: wgt::SurfaceStatus) -> u32 {
    match status {
        wgt::SurfaceStatus::Good => acquire_status::SUCCESS_OPTIMAL,
        wgt::SurfaceStatus::Suboptimal => acquire_status::SUCCESS_SUBOPTIMAL,
        wgt::SurfaceStatus::Timeout | wgt::SurfaceStatus::Occluded => acquire_status::TIMEOUT,
        wgt::SurfaceStatus::Outdated => acquire_status::OUTDATED,
        wgt::SurfaceStatus::Lost => acquire_status::LOST,
        wgt::SurfaceStatus::Validation => acquire_status::ERROR,
    }
}

fn alco_present_mode(v: u32) -> Result<wgt::PresentMode, AlcoStatus> {
    Ok(match v {
        0 => wgt::PresentMode::Fifo,
        1 => wgt::PresentMode::Immediate,
        2 => wgt::PresentMode::Mailbox,
        other => {
            set_error(
                AlcoStatus::INVALID_ARGUMENT,
                format!("invalid present mode {other}"),
            );
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
    })
}

fn alco_alpha_mode(v: u32) -> Result<wgt::CompositeAlphaMode, AlcoStatus> {
    Ok(match v {
        0 => wgt::CompositeAlphaMode::Auto,
        1 => wgt::CompositeAlphaMode::Opaque,
        2 => wgt::CompositeAlphaMode::PreMultiplied,
        3 => wgt::CompositeAlphaMode::PostMultiplied,
        4 => wgt::CompositeAlphaMode::Inherit,
        other => {
            set_error(
                AlcoStatus::INVALID_ARGUMENT,
                format!("invalid alpha mode {other}"),
            );
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
    })
}

/// ABI: creates a surface from raw platform handles.
///
/// # Safety
/// `desc` and `out` must be valid pointers. Platform handles must remain valid
/// until the surface is destroyed.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_create(
    device: AlcoHandle,
    desc: *const AlcoSurfaceDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(
                    AlcoStatus::INVALID_ARGUMENT,
                    "null descriptor or out pointer",
                );
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        DEVICES
            .with(device, |ctx| {
                use raw_window_handle as rwh;
                use std::num::NonZeroIsize;
                use std::ptr::NonNull;

                let handle_ptr = |v: u64| NonNull::<std::ffi::c_void>::new(v as usize as *mut _);
                let nonzero_isize = |v: u64| NonZeroIsize::new(v as isize);

                let result: Result<wgc::id::SurfaceId, AlcoStatus> = (|| match desc.tag {
                    surface_tag::WIN32 => {
                        let hwnd = nonzero_isize(desc.handle).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null hwnd");
                            AlcoStatus::INVALID_ARGUMENT
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
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::WAYLAND => {
                        let surface = handle_ptr(desc.handle).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null wl_surface");
                            AlcoStatus::INVALID_ARGUMENT
                        })?;
                        let display = handle_ptr(desc.display).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null wl_display");
                            AlcoStatus::INVALID_ARGUMENT
                        })?;
                        ctx.global
                            .instance_create_surface(
                                Some(rwh::RawDisplayHandle::Wayland(
                                    rwh::WaylandDisplayHandle::new(display),
                                )),
                                rwh::RawWindowHandle::Wayland(rwh::WaylandWindowHandle::new(
                                    surface,
                                )),
                                None,
                            )
                            .map_err(|e| {
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::XCB => {
                        let window =
                            std::num::NonZeroU32::new(desc.handle as u32).ok_or_else(|| {
                                set_error(AlcoStatus::INVALID_ARGUMENT, "zero xcb window");
                                AlcoStatus::INVALID_ARGUMENT
                            })?;
                        let connection = handle_ptr(desc.display).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null xcb connection");
                            AlcoStatus::INVALID_ARGUMENT
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
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::XLIB => {
                        let display = handle_ptr(desc.display).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null Xlib display");
                            AlcoStatus::INVALID_ARGUMENT
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
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::ANDROID => {
                        let window = handle_ptr(desc.handle).ok_or_else(|| {
                            set_error(AlcoStatus::INVALID_ARGUMENT, "null ANativeWindow");
                            AlcoStatus::INVALID_ARGUMENT
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
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::METAL_LAYER => {
                        #[cfg(target_os = "macos")]
                        {
                            let layer = desc.handle as *mut std::ffi::c_void;
                            if layer.is_null() {
                                set_error(AlcoStatus::INVALID_ARGUMENT, "null CAMetalLayer");
                                return Err(AlcoStatus::INVALID_ARGUMENT);
                            }
                            ctx.global
                                .instance_create_surface_metal(layer, None)
                                .map_err(|e| {
                                    set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                    AlcoStatus::UNSUPPORTED
                                })
                        }
                        #[cfg(not(target_os = "macos"))]
                        {
                            let _ = desc.handle;
                            set_error(
                                AlcoStatus::UNSUPPORTED,
                                "metal layer surfaces require a macOS build",
                            );
                            Err(AlcoStatus::UNSUPPORTED)
                        }
                    }
                    other => {
                        set_error(
                            AlcoStatus::INVALID_ARGUMENT,
                            format!("unsupported surface tag {other}"),
                        );
                        Err(AlcoStatus::INVALID_ARGUMENT)
                    }
                })();

                match result {
                    Ok(id) => {
                        let handle = ctx.surfaces().insert(SurfaceObj {
                            id,
                            state: Arc::new(Mutex::new(SurfaceState {
                                format: 0,
                                width: 0,
                                height: 0,
                                acquired_texture: None,
                            })),
                        });
                        (AlcoStatus::OK, Some(handle))
                    }
                    Err(status) => (status, None),
                }
            })
            .map(|(status, handle)| {
                if let Some(handle) = handle {
                    if status.is_ok() {
                        *out = handle;
                    }
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: fills supported formats and present modes for the device's adapter.
///
/// # Safety
/// `out` must point to writable storage for an `AlcoSurfaceCaps` value.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_get_capabilities(
    device: AlcoHandle,
    surface: AlcoHandle,
    out: *mut AlcoSurfaceCaps,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let surface_id = match ctx.surfaces().with(surface, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                match ctx
                    .global
                    .surface_get_capabilities(surface_id, ctx.adapter_id)
                {
                    Ok(caps) => {
                        (*out).format_count = 0;
                        for format in caps.formats.iter().take(64) {
                            (*out).formats[(*out).format_count as usize] =
                                pixel_format_to_alco(*format);
                            (*out).format_count += 1;
                        }
                        (*out).present_mode_count = 0;
                        for mode in caps.present_modes.iter().take(8) {
                            (*out).present_modes[(*out).present_mode_count as usize] =
                                present_mode_to_alco(*mode);
                            (*out).present_mode_count += 1;
                        }
                        AlcoStatus::OK
                    }
                    Err(e) => {
                        set_error_from(AlcoStatus::UNSUPPORTED, &e);
                        AlcoStatus::UNSUPPORTED
                    }
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: (re)configures the surface's swapchain.
///
/// # Safety
/// `config` must point to an initialized `AlcoSurfaceConfig` value.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_configure(
    device: AlcoHandle,
    surface: AlcoHandle,
    config: *const AlcoSurfaceConfig,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let config = match config.as_ref() {
            Some(c) => c,
            None => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null config pointer");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        DEVICES
            .with(device, |ctx| {
                let surface_obj = match ctx.surfaces().get(surface) {
                    Ok(obj) => obj,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                let mut state = surface_obj.state.lock().unwrap();
                let format = match pixel_format(config.format) {
                    Ok(f) => f,
                    Err(s) => return s,
                };
                let present_mode = match alco_present_mode(config.present_mode) {
                    Ok(m) => m,
                    Err(s) => return s,
                };
                let alpha_mode = match alco_alpha_mode(config.alpha_mode) {
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
                        AlcoStatus::OK
                    }
                    Some(e) => {
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        AlcoStatus::VALIDATION
                    }
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: acquires the next surface texture. `out_texture` receives a texture
/// handle flagged as a surface texture (release-only); `out_status` receives
/// an `acquire_status` code. On a non-success status the texture handle is
/// null and no error is recorded — the caller decides whether to skip or
/// reconfigure, matching the old backend.
///
/// # Safety
/// `out_texture` and `out_status` must point to writable output storage.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_get_current_texture(
    device: AlcoHandle,
    surface: AlcoHandle,
    out_texture: *mut AlcoHandle,
    out_status: *mut u32,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out_texture.is_null() || out_status.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let surface_obj = match ctx.surfaces().get(surface) {
                    Ok(obj) => obj,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                let mut state = surface_obj.state.lock().unwrap();
                let (format, width, height) = (state.format, state.width, state.height);
                match ctx.global.surface_get_current_texture(surface_obj.id, None) {
                    Ok(output) => {
                        *out_status = status_to_alco(output.status);
                        match output.texture {
                            Some(texture_id) => {
                                // wgpu-core derives the acquired texture's descriptor
                                // from the surface configuration, so the recorded
                                // format/size are what get_current_texture produced.
                                let handle = ctx.textures().insert(TextureObj {
                                    id: texture_id,
                                    width,
                                    height,
                                    depth_or_array_layers: 1,
                                    mip_level_count: 1,
                                    format,
                                    is_surface_texture: true,
                                    surface: Some(surface),
                                });
                                state.acquired_texture = Some(texture_id);
                                *out_texture = handle;
                                AlcoStatus::OK
                            }
                            None => {
                                *out_texture = AlcoHandle::NULL;
                                AlcoStatus::OK
                            }
                        }
                    }
                    Err(e) => {
                        *out_texture = AlcoHandle::NULL;
                        *out_status = acquire_status::ERROR;
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        AlcoStatus::VALIDATION
                    }
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: presents the current surface texture.
///
/// # Safety
/// `out_status`, when non-null, must point to writable storage for a status code.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_present(
    device: AlcoHandle,
    surface: AlcoHandle,
    out_status: *mut u32,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let surface_obj = match ctx.surfaces().get(surface) {
                    Ok(obj) => obj,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                let mut state = surface_obj.state.lock().unwrap();
                match ctx.global.surface_present(surface_obj.id) {
                    Ok(status) => {
                        // Core consumes the acquisition even for a non-Good status.
                        // Older texture handles may remain alive until their release.
                        state.acquired_texture = None;
                        if !out_status.is_null() {
                            *out_status = status_to_alco(status);
                        }
                        AlcoStatus::OK
                    }
                    Err(e) => {
                        // Some core errors occur before taking the acquisition;
                        // leave it available for the release path to clean up.
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        AlcoStatus::VALIDATION
                    }
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: releases an acquired surface texture, discarding it first if it has
/// not been presented. Surface textures must never go through
/// `alco_texture_destroy`. Rejected regular textures keep their original handle.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_texture_release(
    device: AlcoHandle,
    texture: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                // Validate before removing: rejection must not bump the generation
                // or replace a regular texture's caller-visible handle.
                let (texture_id, surface) = match ctx
                    .textures()
                    .with(texture, |obj| (obj.id, obj.is_surface_texture, obj.surface))
                {
                    Ok((id, true, surface)) => (id, surface),
                    Ok((_, false, _)) => {
                        set_error(
                            AlcoStatus::INVALID_ARGUMENT,
                            "texture is not a surface texture; use alco_texture_destroy",
                        );
                        return AlcoStatus::INVALID_ARGUMENT;
                    }
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid texture handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                // A stale parent generation must never resolve to a replacement
                // surface. Keep the state claim through texture removal/drop so a
                // reconfigure cannot overtake cleanup of the discarded acquisition.
                let surface_obj = surface.and_then(|handle| ctx.surfaces().get(handle).ok());
                let mut surface_state = surface_obj
                    .as_ref()
                    .map(|obj| obj.state.lock().unwrap());
                if let (Some(obj), Some(state)) = (surface_obj.as_ref(), surface_state.as_mut()) {
                    if state.acquired_texture == Some(texture_id) {
                        match ctx.global.surface_texture_discard(obj.id) {
                            Ok(())
                            | Err(wgc::present::SurfaceError::NothingToPresent)
                            | Err(wgc::present::SurfaceError::NotConfigured) => {
                                // Present can fail after taking the texture, and a
                                // rejected core reconfigure can remove presentation.
                                // Either case has already ended the acquisition.
                                state.acquired_texture = None;
                            }
                            Err(e) => {
                                // Keep the handle available when discard fails.
                                set_error_from(AlcoStatus::VALIDATION, &e);
                                return AlcoStatus::VALIDATION;
                            }
                        }
                    }
                    // Presented older handles do not discard a later acquisition.
                }
                match ctx.textures().remove(texture) {
                    Ok(obj) => {
                        ctx.global.texture_drop(obj.id);
                        AlcoStatus::OK
                    }
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid texture handle");
                        AlcoStatus::INVALID_HANDLE
                    }
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a surface. The surface must not have an acquired texture.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_surface_destroy(
    device: AlcoHandle,
    surface: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.surfaces().remove(surface) {
                Ok(obj) => {
                    let _state = obj.state.lock().unwrap();
                    ctx.global.surface_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// Surface handle table accessor wired onto `DeviceCtx`.
impl DeviceCtx {
    /// Returns the registry of cloneable surface registrations.
    pub fn surfaces(&self) -> &HandleTable<SurfaceObj> {
        &self.surfaces
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn surface(index: u32) -> SurfaceObj {
        SurfaceObj {
            id: wgc::id::SurfaceId::zip(index, 1),
            state: Arc::new(Mutex::new(SurfaceState {
                format: 0,
                width: 0,
                height: 0,
                acquired_texture: None,
            })),
        }
    }

    #[test]
    fn cloned_surface_snapshots_share_configuration_and_acquisition_state() {
        let table = HandleTable::new();
        let handle = table.insert(surface(0));
        let first = table.get(handle).unwrap();
        let second = table.get(handle).unwrap();
        assert!(Arc::ptr_eq(&first.state, &second.state));
        let texture = wgc::id::TextureId::zip(0, 1);
        {
            let mut state = first.state.lock().unwrap();
            state.format = 18;
            state.width = 96;
            state.height = 80;
            state.acquired_texture = Some(texture);
        }
        let state = second.state.lock().unwrap();
        assert_eq!((state.format, state.width, state.height), (18, 96, 80));
        assert_eq!(state.acquired_texture, Some(texture));
    }

    #[test]
    fn stale_surface_generation_does_not_resolve_replacement_state() {
        let table = HandleTable::new();
        let original_handle = table.insert(surface(0));
        let original = table.get(original_handle).unwrap();
        table.remove(original_handle).unwrap();
        let replacement_handle = table.insert(surface(1));
        let replacement = table.get(replacement_handle).unwrap();
        assert_eq!(original_handle.index(), replacement_handle.index());
        assert_ne!(original_handle.generation(), replacement_handle.generation());
        assert!(matches!(table.get(original_handle), Err(AlcoStatus::INVALID_HANDLE)));
        assert!(!Arc::ptr_eq(&original.state, &replacement.state));
    }

    #[test]
    fn surface_state_lock_does_not_lock_registry_or_other_surfaces() {
        let table = HandleTable::new();
        let first_handle = table.insert(surface(0));
        let second_handle = table.insert(surface(1));
        let first = table.get(first_handle).unwrap();
        let _state = first.state.lock().unwrap();
        std::thread::scope(|scope| {
            scope.spawn(|| {
                let snapshot = table.get(first_handle).unwrap();
                assert!(matches!(snapshot.state.try_lock(), Err(std::sync::TryLockError::WouldBlock)));
                table.with(second_handle, |obj| {
                    obj.state.try_lock().unwrap().width = 128;
                }).unwrap();
                let temporary = table.insert(surface(2));
                assert_eq!(table.remove(temporary).unwrap().id, wgc::id::SurfaceId::zip(2, 1));
            }).join().unwrap();
        });
        let second = table.get(second_handle).unwrap();
        assert_eq!(second.state.lock().unwrap().width, 128);
    }
}
