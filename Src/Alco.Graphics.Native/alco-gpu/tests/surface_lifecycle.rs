//! Windows ABI regressions for surface acquisition, presentation, and release.
//! Uses hidden Win32 windows and a real Vulkan adapter without extra dependencies.

#![cfg(windows)]

use alco_gpu::abi::*;
use std::ffi::{c_char, c_void, CStr};
use std::ptr::{null, null_mut};

const COLOR_ATTACHMENT: u32 = 1 << 4;
const SUCCESS_OPTIMAL: u32 = 0;
const SUCCESS_SUBOPTIMAL: u32 = 1;

// These private layouts mirror the exported C ABI, whose implementation modules
// need not be public merely to exercise their entry points from an integration test.
#[repr(C)]
struct SurfaceDesc {
    tag: u32,
    handle: u64,
    display: u64,
    name: *const c_char,
}

#[repr(C)]
struct SurfaceCaps {
    formats: [u32; 64],
    format_count: u32,
    present_modes: [u32; 8],
    present_mode_count: u32,
}

#[repr(C)]
struct SurfaceConfig {
    usage: u32,
    format: u32,
    width: u32,
    height: u32,
    present_mode: u32,
    alpha_mode: u32,
    desired_frame_latency: u32,
}

#[repr(C)]
struct TextureDesc {
    dimension: u32,
    format: u32,
    usage: u32,
    width: u32,
    height: u32,
    depth_or_array_layers: u32,
    mip_level_count: u32,
    sample_count: u32,
    name: *const c_char,
}

#[repr(C)]
#[derive(Default)]
struct TextureInfo {
    width: u32,
    height: u32,
    depth_or_array_layers: u32,
    mip_level_count: u32,
    format: u32,
}

extern "C" {
    fn alco_device_create(desc: *const AlcoDeviceDesc, out: *mut AlcoHandle) -> AlcoStatus;
    fn alco_device_destroy(device: AlcoHandle) -> AlcoStatus;
    fn alco_device_poll(device: AlcoHandle, wait: u32, index: u64, empty: *mut u32) -> AlcoStatus;
    fn alco_surface_create(
        device: AlcoHandle,
        desc: *const SurfaceDesc,
        out: *mut AlcoHandle,
    ) -> AlcoStatus;
    fn alco_surface_get_capabilities(
        device: AlcoHandle,
        surface: AlcoHandle,
        out: *mut SurfaceCaps,
    ) -> AlcoStatus;
    fn alco_surface_configure(
        device: AlcoHandle,
        surface: AlcoHandle,
        config: *const SurfaceConfig,
    ) -> AlcoStatus;
    fn alco_surface_get_current_texture(
        device: AlcoHandle,
        surface: AlcoHandle,
        texture: *mut AlcoHandle,
        status: *mut u32,
    ) -> AlcoStatus;
    fn alco_surface_present(
        device: AlcoHandle,
        surface: AlcoHandle,
        status: *mut u32,
    ) -> AlcoStatus;
    fn alco_surface_destroy(device: AlcoHandle, surface: AlcoHandle) -> AlcoStatus;
    fn alco_texture_create(
        device: AlcoHandle,
        desc: *const TextureDesc,
        out: *mut AlcoHandle,
    ) -> AlcoStatus;
    fn alco_texture_get_info(
        device: AlcoHandle,
        texture: AlcoHandle,
        out: *mut TextureInfo,
    ) -> AlcoStatus;
    fn alco_texture_create_view(
        device: AlcoHandle,
        texture: AlcoHandle,
        desc: *const c_void,
        out: *mut AlcoHandle,
    ) -> AlcoStatus;
    fn alco_texture_view_destroy(device: AlcoHandle, view: AlcoHandle) -> AlcoStatus;
    fn alco_texture_release(device: AlcoHandle, texture: AlcoHandle) -> AlcoStatus;
    fn alco_texture_destroy(device: AlcoHandle, texture: AlcoHandle) -> AlcoStatus;
    fn alco_get_last_error(out: *mut AlcoErrorInfo);
}

#[link(name = "user32")]
extern "system" {
    fn CreateWindowExW(
        ex_style: u32,
        class: *const u16,
        title: *const u16,
        style: u32,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
        parent: *mut c_void,
        menu: *mut c_void,
        instance: *mut c_void,
        param: *mut c_void,
    ) -> *mut c_void;
    fn DestroyWindow(window: *mut c_void) -> i32;
    fn SetWindowPos(
        window: *mut c_void,
        after: *mut c_void,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
        flags: u32,
    ) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn GetModuleHandleW(name: *const u16) -> *mut c_void;
    fn GetLastError() -> u32;
}

fn last_error() -> String {
    let mut error = AlcoErrorInfo {
        status: 0,
        message: null(),
    };
    unsafe { alco_get_last_error(&mut error) };
    if error.message.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(error.message) }
            .to_string_lossy()
            .into_owned()
    }
}

fn expect_status(status: AlcoStatus, expected: AlcoStatus, operation: &str) {
    assert_eq!(status, expected, "{operation}: {}", last_error());
}

fn expect_ok(status: AlcoStatus, operation: &str) {
    expect_status(status, AlcoStatus::OK, operation);
}

fn cleanup(status: AlcoStatus, operation: &str) {
    // Cleanup must not panic, particularly when an assertion is already unwinding.
    if !status.is_ok() {
        eprintln!("cleanup {operation}: {status:?}: {}", last_error());
    }
}

struct Device(AlcoHandle);

impl Device {
    fn new() -> Option<Self> {
        // Referencing the rlib also ensures its exported entry points are linked.
        assert_eq!(alco_gpu::alco_abi_version() >> 16, ABI_MAJOR);
        let desc = AlcoDeviceDesc {
            backend: backend::VULKAN,
            debug: ALCO_FALSE,
            required_features: 0,
            push_constants_size: 4,
            name: null(),
        };
        let mut handle = AlcoHandle::NULL;
        let status = unsafe { alco_device_create(&desc, &mut handle) };
        if status == AlcoStatus::UNSUPPORTED {
            eprintln!(
                "SKIP surface lifecycle: Vulkan device unavailable: {}",
                last_error()
            );
            return None;
        }
        expect_ok(status, "device create");
        Some(Self(handle))
    }
}

impl Drop for Device {
    fn drop(&mut self) {
        unsafe {
            cleanup(
                alco_device_poll(self.0, ALCO_TRUE, u64::MAX, null_mut()),
                "device idle",
            );
            cleanup(alco_device_destroy(self.0), "device destroy");
        }
    }
}

struct HiddenWindow {
    handle: *mut c_void,
    instance: *mut c_void,
}

impl HiddenWindow {
    fn new() -> Self {
        let class: Vec<u16> = "STATIC\0".encode_utf16().collect();
        let title: Vec<u16> = "Alco surface lifecycle regression\0"
            .encode_utf16()
            .collect();
        let instance = unsafe { GetModuleHandleW(null()) };
        // STATIC is a built-in Win32 class. WS_POPUP without WS_VISIBLE creates
        // a hidden top-level window whose client area matches the requested size.
        let handle = unsafe {
            CreateWindowExW(
                0,
                class.as_ptr(),
                title.as_ptr(),
                0x8000_0000,
                0,
                0,
                64,
                64,
                null_mut(),
                null_mut(),
                instance,
                null_mut(),
            )
        };
        assert!(!handle.is_null(), "CreateWindowExW failed: {}", unsafe {
            GetLastError()
        });
        Self { handle, instance }
    }

    fn resize(&self, width: u32, height: u32) {
        // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE; keep the window hidden.
        let result = unsafe {
            SetWindowPos(
                self.handle,
                null_mut(),
                0,
                0,
                width as i32,
                height as i32,
                0x0002 | 0x0004 | 0x0010,
            )
        };
        assert_ne!(result, 0, "SetWindowPos failed: {}", unsafe {
            GetLastError()
        });
    }
}

impl Drop for HiddenWindow {
    fn drop(&mut self) {
        if unsafe { DestroyWindow(self.handle) } == 0 {
            eprintln!("cleanup DestroyWindow failed: {}", unsafe {
                GetLastError()
            });
        }
    }
}

struct Surface<'a> {
    device: &'a Device,
    _window: &'a HiddenWindow,
    handle: AlcoHandle,
}

impl<'a> Surface<'a> {
    fn new(device: &'a Device, window: &'a HiddenWindow) -> (Self, SurfaceCaps, SurfaceConfig) {
        let desc = SurfaceDesc {
            tag: 0, // Win32
            handle: window.handle as usize as u64,
            display: window.instance as usize as u64,
            name: null(),
        };
        let mut handle = AlcoHandle::NULL;
        expect_ok(
            unsafe { alco_surface_create(device.0, &desc, &mut handle) },
            "surface create",
        );
        let surface = Self {
            device,
            _window: window,
            handle,
        };
        let mut caps = SurfaceCaps {
            formats: [0; 64],
            format_count: 0,
            present_modes: [0; 8],
            present_mode_count: 0,
        };
        expect_ok(
            unsafe { alco_surface_get_capabilities(device.0, handle, &mut caps) },
            "surface capabilities",
        );
        assert!(caps.format_count > 0 && caps.format_count <= 64);
        assert!(caps.present_mode_count > 0 && caps.present_mode_count <= 8);
        let config = SurfaceConfig {
            usage: COLOR_ATTACHMENT,
            format: caps.formats[0],
            width: 64,
            height: 64,
            present_mode: 0, // Fifo is universally supported.
            alpha_mode: 0,   // Auto selects a supported mode.
            desired_frame_latency: 2,
        };
        (surface, caps, config)
    }

    fn configure(&self, config: &SurfaceConfig) {
        expect_ok(
            unsafe { alco_surface_configure(self.device.0, self.handle, config) },
            "surface configure",
        );
    }

    fn acquire(&self) -> Acquired<'_, 'a> {
        let mut texture = AlcoHandle::NULL;
        let mut status = u32::MAX;
        let result = unsafe {
            alco_surface_get_current_texture(self.device.0, self.handle, &mut texture, &mut status)
        };
        // Own the handle immediately so assertions and view creation can unwind safely.
        let mut acquired = Acquired {
            surface: self,
            texture,
            view: AlcoHandle::NULL,
        };
        expect_ok(result, "surface acquire");
        assert!(
            matches!(status, SUCCESS_OPTIMAL | SUCCESS_SUBOPTIMAL),
            "hidden Win32/Vulkan surface acquire returned status {status}"
        );
        assert!(!texture.is_null());
        expect_ok(
            unsafe { alco_texture_create_view(self.device.0, texture, null(), &mut acquired.view) },
            "surface view create",
        );
        acquired
    }

    fn present(&self) {
        let mut status = u32::MAX;
        expect_ok(
            unsafe { alco_surface_present(self.device.0, self.handle, &mut status) },
            "surface present",
        );
        assert!(
            matches!(status, SUCCESS_OPTIMAL | SUCCESS_SUBOPTIMAL),
            "hidden Win32/Vulkan surface present returned status {status}"
        );
    }
}

impl Drop for Surface<'_> {
    fn drop(&mut self) {
        cleanup(
            unsafe { alco_surface_destroy(self.device.0, self.handle) },
            "surface destroy",
        );
    }
}

struct Acquired<'s, 'd> {
    surface: &'s Surface<'d>,
    texture: AlcoHandle,
    view: AlcoHandle,
}

impl Acquired<'_, '_> {
    fn info(&self) -> TextureInfo {
        let mut info = TextureInfo::default();
        expect_ok(
            unsafe { alco_texture_get_info(self.surface.device.0, self.texture, &mut info) },
            "texture info",
        );
        info
    }

    fn release(mut self) -> AlcoHandle {
        let handle = self.texture;
        // Match C# Drop: release the texture while its default view is still alive.
        expect_ok(
            unsafe { alco_texture_release(self.surface.device.0, handle) },
            "surface texture release",
        );
        self.texture = AlcoHandle::NULL;
        expect_ok(
            unsafe { alco_texture_view_destroy(self.surface.device.0, self.view) },
            "surface view destroy",
        );
        self.view = AlcoHandle::NULL;
        handle
    }
}

impl Drop for Acquired<'_, '_> {
    fn drop(&mut self) {
        unsafe {
            if !self.texture.is_null() {
                cleanup(
                    alco_texture_release(self.surface.device.0, self.texture),
                    "surface texture release",
                );
            }
            if !self.view.is_null() {
                cleanup(
                    alco_texture_view_destroy(self.surface.device.0, self.view),
                    "surface view destroy",
                );
            }
        }
    }
}

struct RegularTexture<'a> {
    device: &'a Device,
    handle: AlcoHandle,
}

impl Drop for RegularTexture<'_> {
    fn drop(&mut self) {
        cleanup(
            unsafe { alco_texture_destroy(self.device.0, self.handle) },
            "regular texture destroy",
        );
    }
}

#[test]
fn present_release_does_not_discard_a_later_acquisition() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);

    let first = surface.acquire();
    surface.present();
    // The first presented handle can outlive the next acquisition. Releasing it
    // must neither discard nor mark the second acquisition as presented.
    let second = surface.acquire();
    let stale = first.release();
    expect_status(
        unsafe { alco_texture_release(device.0, stale) },
        AlcoStatus::INVALID_HANDLE,
        "double release",
    );
    surface.present();
    second.release();
    surface.configure(&config);
    surface.acquire().release();
    surface.acquire().release();
}

#[test]
fn unpresented_release_allows_resize_vsync_reconfigure_and_reacquire() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, caps, mut config) = Surface::new(&device, &window);
    surface.configure(&config);
    surface.acquire().release();

    window.resize(96, 80);
    config.width = 96;
    config.height = 80;
    surface.configure(&config);
    let resized = surface.acquire();
    let info = resized.info();
    assert_eq!(
        (info.width, info.height, info.format),
        (96, 80, config.format)
    );
    resized.release();

    let alternative = caps.present_modes[..caps.present_mode_count as usize]
        .iter()
        .copied()
        .find(|mode| *mode != config.present_mode && *mode <= 2);
    if let Some(mode) = alternative {
        config.present_mode = mode;
        eprintln!("Testing Fifo -> present mode {mode} after unpresented release");
        surface.configure(&config);
        surface.acquire().release();
        config.present_mode = 0;
        surface.configure(&config);
    } else {
        eprintln!("SKIP VSync subcase: surface exposes no alternate Alco present mode");
    }
    for _ in 0..3 {
        surface.acquire().release();
        surface.configure(&config);
    }
    surface.acquire().release();
}

#[test]
fn invalid_and_wrong_kind_releases_preserve_the_original_handle() {
    let Some(device) = Device::new() else { return };
    let desc = TextureDesc {
        dimension: 1,
        format: 18,
        usage: COLOR_ATTACHMENT,
        width: 16,
        height: 16,
        depth_or_array_layers: 1,
        mip_level_count: 1,
        sample_count: 1,
        name: null(),
    };
    let mut handle = AlcoHandle::NULL;
    expect_ok(
        unsafe { alco_texture_create(device.0, &desc, &mut handle) },
        "regular texture create",
    );
    let regular = RegularTexture {
        device: &device,
        handle,
    };
    expect_status(
        unsafe { alco_texture_release(device.0, regular.handle) },
        AlcoStatus::INVALID_ARGUMENT,
        "wrong-kind release",
    );
    assert!(last_error().contains("not a surface texture"));
    let mut info = TextureInfo::default();
    expect_ok(
        unsafe { alco_texture_get_info(device.0, regular.handle, &mut info) },
        "regular handle after rejection",
    );
    assert_eq!((info.width, info.height), (16, 16));
    expect_status(
        unsafe { alco_texture_release(AlcoHandle::NULL, regular.handle) },
        AlcoStatus::INVALID_HANDLE,
        "invalid device release",
    );
    expect_ok(
        unsafe { alco_texture_get_info(device.0, regular.handle, &mut info) },
        "regular handle after invalid device",
    );
    expect_status(
        unsafe { alco_texture_release(device.0, AlcoHandle::NULL) },
        AlcoStatus::INVALID_HANDLE,
        "null texture release",
    );
}

#[test]
fn surface_validation_errors_still_allow_texture_cleanup_and_recovery() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, mut config) = Surface::new(&device, &window);
    let mut present_status = u32::MAX;
    expect_status(
        unsafe { alco_surface_present(device.0, surface.handle, &mut present_status) },
        AlcoStatus::VALIDATION,
        "present before configure",
    );
    assert_eq!(present_status, u32::MAX);
    surface.configure(&config);
    expect_status(
        unsafe { alco_surface_present(device.0, surface.handle, null_mut()) },
        AlcoStatus::VALIDATION,
        "present without acquisition",
    );
    assert!(last_error().contains("No surface image"));

    let acquired = surface.acquire();
    config.present_mode = u32::MAX;
    expect_status(
        unsafe { alco_surface_configure(device.0, surface.handle, &config) },
        AlcoStatus::INVALID_ARGUMENT,
        "invalid present mode",
    );
    assert_eq!(acquired.info().width, 64);
    config.present_mode = 0;
    expect_status(
        unsafe { alco_surface_configure(device.0, surface.handle, &config) },
        AlcoStatus::VALIDATION,
        "configure while acquired",
    );
    assert!(last_error().contains("must be dropped before re-configuring"));
    // Core 30 removes presentation before returning PreviousOutputExists.
    // Release must still drop the registered texture rather than strand its handle.
    acquired.release();
    surface.configure(&config);
    let recovered = surface.acquire();
    expect_ok(
        unsafe { alco_surface_present(device.0, surface.handle, null_mut()) },
        "present with optional null status",
    );
    recovered.release();
}

#[test]
fn releasing_one_surface_does_not_discard_another_surfaces_acquisition() {
    let Some(device) = Device::new() else { return };
    let first_window = HiddenWindow::new();
    let second_window = HiddenWindow::new();
    let (first, _, first_config) = Surface::new(&device, &first_window);
    let (second, _, second_config) = Surface::new(&device, &second_window);
    first.configure(&first_config);
    second.configure(&second_config);
    let first_texture = first.acquire();
    let second_texture = second.acquire();
    first_texture.release();
    first.configure(&first_config);
    second.present();
    second_texture.release();
    first.acquire().release();
    second.acquire().release();
}
