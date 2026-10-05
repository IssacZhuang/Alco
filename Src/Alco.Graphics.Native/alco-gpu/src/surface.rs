//! Surfaces: raw platform handles (matching C# `SurfaceSource`) through
//! raw-window-handle into wgpu-core, capabilities, configuration and the
//! acquire/present cycle. Acquired surface textures enter the device texture
//! table flagged `is_surface_texture` so the C# side must release (never
//! destroy) them.

use crate::abi::*;
use crate::convert::*;
use crate::device::{DeviceCtx, DEVICES};
use crate::entry::{set_error, set_error_from};
use crate::handle::HandleTable;
use crate::objects::TextureObj;
use std::ffi::c_char;
use wgpu_core as wgc;
use wgpu_types as wgt;

pub(crate) struct SurfaceObj {
    pub id: wgc::id::SurfaceId,
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
    pub const SUCCESS_OPTIMAL: u32 = 0;
    pub const SUCCESS_SUBOPTIMAL: u32 = 1;
    pub const TIMEOUT: u32 = 2;
    pub const OUTDATED: u32 = 3;
    pub const LOST: u32 = 4;
    pub const ERROR: u32 = 5;
}

#[repr(C)]
pub struct AlcoSurfaceDesc {
    pub tag: u32,
    /// hwnd / CAMetalLayer* / wl_surface* / xcb window / Xlib window / ANativeWindow*.
    pub handle: u64,
    /// Win32 HINSTANCE (0 = none) / wl_display* / xcb connection* / Xlib Display*.
    pub display: u64,
    pub name: *const c_char,
}

#[repr(C)]
pub struct AlcoSurfaceCaps {
    pub formats: [u32; 64],
    pub format_count: u32,
    /// C# present modes: 0 Fifo, 1 Immediate, 2 Mailbox.
    pub present_modes: [u32; 8],
    pub present_mode_count: u32,
}

#[repr(C)]
pub struct AlcoSurfaceConfig {
    /// C# `TextureUsage` bits (RenderAttachment and optionally TextureBinding).
    pub usage: u32,
    pub format: u32,
    pub width: u32,
    pub height: u32,
    /// C# `PresentMode`: 0 Fifo, 1 Immediate, 2 Mailbox.
    pub present_mode: u32,
    /// C# `CompositeAlphaMode`.
    pub alpha_mode: u32,
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
            set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid present mode {other}"));
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
            set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid alpha mode {other}"));
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
    })
}

/// ABI: creates a surface from raw platform handles.
#[no_mangle]
pub unsafe extern "C" fn alco_surface_create(
    device: AlcoHandle,
    desc: *const AlcoSurfaceDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null descriptor or out pointer");
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
                                rwh::RawWindowHandle::Wayland(
                                    rwh::WaylandWindowHandle::new(surface),
                                ),
                                None,
                            )
                            .map_err(|e| {
                                set_error_from(AlcoStatus::UNSUPPORTED, &e);
                                AlcoStatus::UNSUPPORTED
                            })
                    }
                    surface_tag::XCB => {
                        let window = std::num::NonZeroU32::new(desc.handle as u32).ok_or_else(|| {
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
                                rwh::RawWindowHandle::AndroidNdk(
                                    rwh::AndroidNdkWindowHandle::new(window),
                                ),
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
                        set_error(AlcoStatus::INVALID_ARGUMENT, format!("unsupported surface tag {other}"));
                        Err(AlcoStatus::INVALID_ARGUMENT)
                    }
                })();

                match result {
                    Ok(id) => {
                        let handle = ctx.surfaces().insert(SurfaceObj { id });
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
#[no_mangle]
pub unsafe extern "C" fn alco_surface_get_capabilities(
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
#[no_mangle]
pub unsafe extern "C" fn alco_surface_configure(
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
                let surface_id = match ctx.surfaces().with(surface, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
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
                    .surface_configure(surface_id, ctx.device_id, &wconfig)
                {
                    None => AlcoStatus::OK,
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
#[no_mangle]
pub unsafe extern "C" fn alco_surface_get_current_texture(
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
                let surface_id = match ctx.surfaces().with(surface, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                match ctx.global.surface_get_current_texture(surface_id, None) {
                    Ok(output) => {
                        *out_status = status_to_alco(output.status);
                        match output.texture {
                            Some(texture_id) => {
                                // Surface textures have a single mip, one layer; the
                                // configured size is authoritative and read back by
                                // C# via texture_get_info after configure.
                                let handle = ctx.textures().insert(TextureObj {
                                    id: texture_id,
                                    width: 0,
                                    height: 0,
                                    depth_or_array_layers: 1,
                                    mip_level_count: 1,
                                    format: 0,
                                    is_surface_texture: true,
                                });
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
#[no_mangle]
pub unsafe extern "C" fn alco_surface_present(
    device: AlcoHandle,
    surface: AlcoHandle,
    out_status: *mut u32,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let surface_id = match ctx.surfaces().with(surface, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid surface handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                match ctx.global.surface_present(surface_id) {
                    Ok(status) => {
                        if !out_status.is_null() {
                            *out_status = status_to_alco(status);
                        }
                        AlcoStatus::OK
                    }
                    Err(e) => {
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

/// ABI: releases (drops) the acquired surface texture handle. Surface
/// textures must never go through `alco_texture_destroy`.
#[no_mangle]
pub unsafe extern "C" fn alco_texture_release(
    device: AlcoHandle,
    texture: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.textures().remove(texture) {
                Ok(obj) if obj.is_surface_texture => {
                    ctx.global.texture_drop(obj.id);
                    AlcoStatus::OK
                }
                Ok(obj) => {
                    // Regular textures go through the full destroy path.
                    let handle = ctx.textures().insert(obj);
                    let _ = handle;
                    set_error(
                        AlcoStatus::INVALID_ARGUMENT,
                        "texture is not a surface texture; use alco_texture_destroy",
                    );
                    AlcoStatus::INVALID_ARGUMENT
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid texture handle");
                    AlcoStatus::INVALID_HANDLE
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
pub unsafe extern "C" fn alco_surface_destroy(device: AlcoHandle, surface: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.surfaces().remove(surface) {
                Ok(obj) => {
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
    pub fn surfaces(&self) -> &HandleTable<SurfaceObj> {
        &self.surfaces
    }
}
