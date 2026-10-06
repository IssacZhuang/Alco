//! Windows pointer-ABI ownership regressions for surface acquisition and release.
//! Uses hidden Win32 windows, real Vulkan resources, Arc ownership observations,
//! core registry reports, and thread-local host-allocation instrumentation.

#![cfg(windows)]

use alco_gpu::abi::*;
use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;
use std::ffi::{c_char, c_void, CStr};
use std::ptr::{null, null_mut};
use std::sync::Arc;

struct AllocationCounter;

thread_local! {
    static COUNT_ALLOCATIONS: Cell<bool> = const { Cell::new(false) };
    static ALLOCATIONS: Cell<usize> = const { Cell::new(0) };
}

unsafe impl GlobalAlloc for AllocationCounter {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        if COUNT_ALLOCATIONS.try_with(Cell::get).unwrap_or(false) {
            let _ = ALLOCATIONS.try_with(|count| count.set(count.get() + 1));
        }
        System.alloc(layout)
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        System.dealloc(pointer, layout);
    }

    unsafe fn realloc(&self, pointer: *mut u8, layout: Layout, size: usize) -> *mut u8 {
        if COUNT_ALLOCATIONS.try_with(Cell::get).unwrap_or(false) {
            let _ = ALLOCATIONS.try_with(|count| count.set(count.get() + 1));
        }
        System.realloc(pointer, layout, size)
    }
}

#[global_allocator]
static ALLOCATOR: AllocationCounter = AllocationCounter;

fn allocation_count(operation: impl FnOnce()) -> usize {
    struct Reset;
    impl Drop for Reset {
        fn drop(&mut self) {
            COUNT_ALLOCATIONS.with(|enabled| enabled.set(false));
        }
    }
    ALLOCATIONS.with(|count| count.set(0));
    COUNT_ALLOCATIONS.with(|enabled| enabled.set(true));
    let reset = Reset;
    operation();
    drop(reset);
    ALLOCATIONS.with(Cell::get)
}

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
struct SurfaceCapabilities {
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

// Handle<T> is a transparent pointer; opaque Rust pointees never cross by value.
#[allow(improper_ctypes)]
extern "C-unwind" {
    fn device_create(desc: *const DeviceDesc, out: *mut DeviceHandle) -> Status;
    fn device_destroy(device: DeviceHandle) -> Status;
    fn device_poll(device: DeviceHandle, wait: u32, index: u64, empty: *mut u32) -> Status;
    fn device_create_surface(
        device: DeviceHandle,
        desc: *const SurfaceDesc,
        out: *mut SurfaceHandle,
    ) -> Status;
    fn surface_get_capabilities(surface: SurfaceHandle, out: *mut SurfaceCapabilities) -> Status;
    fn surface_configure(surface: SurfaceHandle, config: *const SurfaceConfig) -> Status;
    fn surface_get_current_texture(
        surface: SurfaceHandle,
        texture: *mut TextureHandle,
        status: *mut u32,
    ) -> Status;
    fn surface_present(surface: SurfaceHandle, status: *mut u32) -> Status;
    fn surface_destroy(surface: SurfaceHandle) -> Status;
    fn device_create_texture(
        device: DeviceHandle,
        desc: *const TextureDesc,
        out: *mut TextureHandle,
    ) -> Status;
    fn texture_get_info(texture: TextureHandle, out: *mut TextureInfo) -> Status;
    fn texture_create_view(
        texture: TextureHandle,
        desc: *const c_void,
        out: *mut TextureViewHandle,
    ) -> Status;
    fn texture_view_destroy(view: TextureViewHandle) -> Status;
    fn texture_release(texture: TextureHandle) -> Status;
    fn texture_destroy(texture: TextureHandle) -> Status;
}

extern "C" {
    fn get_last_error(out: *mut ErrorInfo);
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
    let mut error = ErrorInfo {
        status: 0,
        message: null(),
    };
    unsafe { get_last_error(&mut error) };
    if error.message.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(error.message) }
            .to_string_lossy()
            .into_owned()
    }
}

fn expect_status(status: Status, expected: Status, operation: &str) {
    assert_eq!(status, expected, "{operation}: {}", last_error());
}

fn expect_ok(status: Status, operation: &str) {
    expect_status(status, Status::OK, operation);
}

fn cleanup(status: Status, operation: &str) {
    // Cleanup must not panic, particularly when an assertion is already unwinding.
    if !status.is_ok() {
        eprintln!("cleanup {operation}: {status:?}: {}", last_error());
    }
}

struct Device(DeviceHandle);

impl Device {
    fn new() -> Option<Self> {
        // Referencing the rlib also ensures its exported entry points are linked.
        assert_eq!(alco_gpu::abi_version() >> 16, ABI_MAJOR);
        let desc = DeviceDesc {
            backend: backend::VULKAN,
            debug: FALSE,
            required_features: 0,
            push_constants_size: 4,
            name: null(),
        };
        let mut handle = DeviceHandle::NULL;
        let status = unsafe { device_create(&desc, &mut handle) };
        if status == Status::UNSUPPORTED {
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
                device_poll(self.0, TRUE, u64::MAX, null_mut()),
                "device idle",
            );
            cleanup(device_destroy(self.0), "device destroy");
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

struct Surface<'w> {
    window: &'w HiddenWindow,
    handle: SurfaceHandle,
}

impl<'w> Surface<'w> {
    fn new(device: &Device, window: &'w HiddenWindow) -> (Self, SurfaceCapabilities, SurfaceConfig) {
        let desc = SurfaceDesc {
            tag: 0, // Win32
            handle: window.handle as usize as u64,
            display: window.instance as usize as u64,
            name: null(),
        };
        let mut handle = SurfaceHandle::NULL;
        expect_ok(
            unsafe { device_create_surface(device.0, &desc, &mut handle) },
            "surface create",
        );
        let surface = Self { window, handle };
        let mut caps = SurfaceCapabilities {
            formats: [0; 64],
            format_count: 0,
            present_modes: [0; 8],
            present_mode_count: 0,
        };
        expect_ok(
            unsafe { surface_get_capabilities(handle, &mut caps) },
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
            unsafe { surface_configure(self.handle, config) },
            "surface configure",
        );
    }

    fn acquire(&self) -> Acquired<'w> {
        let mut texture = TextureHandle::NULL;
        let mut status = u32::MAX;
        let result = unsafe { surface_get_current_texture(self.handle, &mut texture, &mut status) };
        // Own the handle immediately so assertions and view creation can unwind safely.
        let mut acquired = Acquired {
            _window: self.window,
            texture,
            view: TextureViewHandle::NULL,
        };
        expect_ok(result, "surface acquire");
        assert!(
            matches!(status, SUCCESS_OPTIMAL | SUCCESS_SUBOPTIMAL),
            "hidden Win32/Vulkan surface acquire returned status {status}"
        );
        assert!(!texture.is_null());
        expect_ok(
            unsafe { texture_create_view(texture, null(), &mut acquired.view) },
            "surface view create",
        );
        acquired
    }

    fn present(&self) {
        let mut status = u32::MAX;
        expect_ok(
            unsafe { surface_present(self.handle, &mut status) },
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
        cleanup(unsafe { surface_destroy(self.handle) }, "surface destroy");
    }
}

struct Acquired<'w> {
    // Child wrappers keep the physical window alive independently of Surface.
    _window: &'w HiddenWindow,
    texture: TextureHandle,
    view: TextureViewHandle,
}

impl Acquired<'_> {
    fn info(&self) -> TextureInfo {
        let mut info = TextureInfo::default();
        expect_ok(
            unsafe { texture_get_info(self.texture, &mut info) },
            "texture info",
        );
        info
    }

    fn release_texture(&mut self) -> Status {
        // Valid acquired releases consume the owner even on cleanup failure.
        // Disarm before the call so an assertion or host unwind never retries it.
        let texture = std::mem::replace(&mut self.texture, TextureHandle::NULL);
        unsafe { texture_release(texture) }
    }

    fn release_view(&mut self) {
        let view = std::mem::replace(&mut self.view, TextureViewHandle::NULL);
        expect_ok(
            unsafe { texture_view_destroy(view) },
            "surface view destroy",
        );
    }

    fn release(mut self) {
        // Match C# Drop: release the texture while its default view is still alive.
        expect_ok(self.release_texture(), "surface texture release");
        self.release_view();
    }
}

impl Drop for Acquired<'_> {
    fn drop(&mut self) {
        unsafe {
            if !self.texture.is_null() {
                cleanup(texture_release(self.texture), "surface texture release");
            }
            if !self.view.is_null() {
                cleanup(texture_view_destroy(self.view), "surface view destroy");
            }
        }
    }
}

struct RegularTexture {
    handle: TextureHandle,
}

impl Drop for RegularTexture {
    fn drop(&mut self) {
        cleanup(
            unsafe { texture_destroy(self.handle) },
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
    let first_id = unsafe { first.texture.get().unwrap().id };
    let second_id = unsafe { second.texture.get().unwrap().id };
    assert_ne!(first_id, second_id);
    let ctx = unsafe { surface.handle.get().unwrap() };
    assert_eq!(
        ctx.ctx
            .global
            .generate_report()
            .hub
            .textures
            .num_kept_from_user,
        2
    );
    first.release();
    assert_eq!(
        ctx.ctx
            .global
            .generate_report()
            .hub
            .textures
            .num_kept_from_user,
        1
    );
    assert_eq!(unsafe { second.texture.get().unwrap().id }, second_id);
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
fn null_and_wrong_kind_lifecycle_calls_preserve_live_resource_identity() {
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
    let mut handle = TextureHandle::NULL;
    expect_ok(
        unsafe { device_create_texture(device.0, &desc, &mut handle) },
        "regular texture create",
    );
    let regular = RegularTexture { handle };
    let identity = unsafe { regular.handle.get().unwrap().id };
    expect_status(
        unsafe { texture_release(regular.handle) },
        Status::INVALID_ARGUMENT,
        "wrong-kind release",
    );
    assert!(last_error().contains("not a surface texture"));
    let mut info = TextureInfo::default();
    expect_ok(
        unsafe { texture_get_info(regular.handle, &mut info) },
        "regular handle after rejection",
    );
    assert_eq!((info.width, info.height), (16, 16));
    assert_eq!(unsafe { regular.handle.get().unwrap().id }, identity);
    expect_status(
        unsafe { texture_release(TextureHandle::NULL) },
        Status::INVALID_HANDLE,
        "null texture release",
    );
    expect_status(
        unsafe { surface_destroy(SurfaceHandle::NULL) },
        Status::INVALID_HANDLE,
        "null surface destroy",
    );
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let acquired = surface.acquire();
    let identity = unsafe { acquired.texture.get().unwrap().id };
    expect_status(
        unsafe { texture_destroy(acquired.texture) },
        Status::INVALID_ARGUMENT,
        "wrong-kind acquired texture destroy",
    );
    assert_eq!(unsafe { acquired.texture.get().unwrap().id }, identity);
    assert_eq!(acquired.info().width, config.width);
    acquired.release();
}

#[test]
fn surface_validation_errors_still_allow_texture_cleanup_and_recovery() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, mut config) = Surface::new(&device, &window);
    let mut present_status = u32::MAX;
    expect_status(
        unsafe { surface_present(surface.handle, &mut present_status) },
        Status::VALIDATION,
        "present before configure",
    );
    assert_eq!(present_status, u32::MAX);
    surface.configure(&config);
    expect_status(
        unsafe { surface_present(surface.handle, null_mut()) },
        Status::VALIDATION,
        "present without acquisition",
    );
    assert!(last_error().contains("No surface image"));

    let acquired = surface.acquire();
    config.present_mode = u32::MAX;
    expect_status(
        unsafe { surface_configure(surface.handle, &config) },
        Status::INVALID_ARGUMENT,
        "invalid present mode",
    );
    assert_eq!(acquired.info().width, 64);
    config.present_mode = 0;
    expect_status(
        unsafe { surface_configure(surface.handle, &config) },
        Status::VALIDATION,
        "configure while acquired",
    );
    assert!(last_error().contains("must be dropped before re-configuring"));
    // Core 30 removes presentation before returning PreviousOutputExists.
    // Release must still drop the registered texture rather than strand its handle.
    acquired.release();
    surface.configure(&config);
    let recovered = surface.acquire();
    expect_ok(
        unsafe { surface_present(surface.handle, null_mut()) },
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

#[test]
fn borrowed_surface_texture_operations_do_not_allocate_or_acquire_an_owner() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let acquired = surface.acquire();
    let parent = unsafe { acquired.texture.get().unwrap().surface.as_ref().unwrap() };
    assert_eq!(surface.handle.0.cast_const(), Arc::as_ptr(parent));
    assert_eq!(Arc::strong_count(parent), 3); // Public surface, texture, and view.
    let ctx_owners = Arc::strong_count(&parent.ctx);
    let report = parent.ctx.global.generate_report();
    assert_eq!(report.surfaces.num_allocated, 1);
    assert_eq!(report.hub.textures.num_allocated, 1);
    assert_eq!(report.hub.texture_views.num_allocated, 1);
    let mut info = TextureInfo::default();
    expect_ok(
        unsafe { texture_get_info(acquired.texture, &mut info) },
        "warm texture info",
    );
    let allocations = allocation_count(|| {
        for _ in 0..256 {
            assert_eq!(
                unsafe { texture_get_info(acquired.texture, &mut info) },
                Status::OK
            );
            assert_eq!(unsafe { surface.handle.get().unwrap().id }, parent.id);
            assert_eq!(Arc::strong_count(parent), 3);
            assert_eq!(Arc::strong_count(&parent.ctx), ctx_owners);
        }
    });
    assert_eq!(
        allocations, 0,
        "pointer borrows and texture metadata queries allocated"
    );
    assert_eq!(parent.ctx.global.generate_report(), report);
    acquired.release();
}

#[test]
fn public_surface_destroy_waits_for_acquired_texture_and_retained_view() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let mut acquired = surface.acquire();
    let parent = unsafe { acquired.texture.get().unwrap().surface.as_ref().unwrap() };
    let weak_parent = Arc::downgrade(parent);
    let ctx = Arc::clone(&parent.ctx);
    assert_eq!(weak_parent.strong_count(), 3);
    drop(surface);
    assert_eq!(weak_parent.strong_count(), 2);
    assert_eq!(ctx.global.generate_report().surfaces.num_kept_from_user, 1);
    assert_eq!(acquired.info().width, config.width);

    expect_ok(
        acquired.release_texture(),
        "child release after public surface destroy",
    );
    assert_eq!(weak_parent.strong_count(), 1);
    let report = ctx.global.generate_report();
    assert_eq!(report.surfaces.num_kept_from_user, 1);
    assert_eq!(report.hub.textures.num_allocated, 0);
    assert_eq!(report.hub.texture_views.num_kept_from_user, 1);
    acquired.release_view();
    assert!(weak_parent.upgrade().is_none());
    let report = ctx.global.generate_report();
    assert_eq!(report.surfaces.num_allocated, 0);
    assert_eq!(report.hub.texture_views.num_allocated, 0);
    // `window` is still alive here and is only destroyed after final core drop.
}

#[test]
fn device_first_cleanup_consumes_failed_release_with_a_retained_view() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let mut acquired = surface.acquire();
    let parent = unsafe { acquired.texture.get().unwrap().surface.as_ref().unwrap() };
    let weak_parent = Arc::downgrade(parent);
    let ctx = Arc::clone(&parent.ctx);
    let weak_ctx = Arc::downgrade(&ctx);
    let surface_id = parent.id;
    drop(device);
    assert!(matches!(
        ctx.global.surface_texture_discard(surface_id),
        Err(wgpu_core::present::SurfaceError::Device(_))
    ));
    // The failed core discard above returned before taking its acquired Arc.
    drop(surface);
    expect_status(
        acquired.release_texture(),
        Status::VALIDATION,
        "release after device destroy consumes despite discard failure",
    );
    assert!(last_error().contains("lost"));
    assert!(acquired.texture.is_null());
    assert_eq!(weak_parent.strong_count(), 1);
    let report = ctx.global.generate_report();
    assert_eq!(report.hub.textures.num_allocated, 0);
    assert_eq!(report.hub.texture_views.num_allocated, 1);
    assert_eq!(report.surfaces.num_allocated, 1);
    assert!(matches!(
        ctx.global.surface_texture_release(surface_id),
        Err(wgpu_core::present::SurfaceError::NothingToPresent)
    ));
    acquired.release_view();
    assert!(weak_parent.upgrade().is_none());
    assert_eq!(ctx.global.generate_report().surfaces.num_allocated, 0);
    drop(ctx);
    assert!(weak_ctx.upgrade().is_none());
}

#[test]
fn native_acquired_texture_drop_cleans_invalid_device_without_abi_diagnostics() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let mut acquired = surface.acquire();
    let parent = unsafe { acquired.texture.get().unwrap().surface.as_ref().unwrap() };
    let weak_parent = Arc::downgrade(parent);
    let ctx = Arc::clone(&parent.ctx);
    drop(device);
    drop(surface);
    expect_status(
        unsafe { surface_present(SurfaceHandle::NULL, null_mut()) },
        Status::INVALID_HANDLE,
        "diagnostic sentinel",
    );
    let error_before = last_error();
    let texture = std::mem::replace(&mut acquired.texture, TextureHandle::NULL);
    drop(unsafe { texture.take().unwrap() });
    assert_eq!(
        last_error(),
        error_before,
        "native Drop must not record ABI errors"
    );
    assert_eq!(weak_parent.strong_count(), 1);
    assert_eq!(ctx.global.generate_report().hub.textures.num_allocated, 0);
    acquired.release_view();
    assert!(weak_parent.upgrade().is_none());
    assert_eq!(ctx.global.generate_report().surfaces.num_allocated, 0);
}

#[test]
fn releasing_unpresented_texture_retires_metadata_before_its_view_is_released() {
    let Some(device) = Device::new() else { return };
    let window = HiddenWindow::new();
    let (surface, _, config) = Surface::new(&device, &window);
    surface.configure(&config);
    let mut previous = surface.acquire();
    expect_ok(
        previous.release_texture(),
        "unpresented release retaining view",
    );
    surface.configure(&config);
    let current = surface.acquire();
    // Retiring the old view must not consume the current surface acquisition.
    previous.release_view();
    surface.present();
    current.release();
    let report = unsafe { surface.handle.get().unwrap().ctx.global.generate_report() };
    assert_eq!(report.hub.textures.num_allocated, 0);
    assert_eq!(report.hub.texture_views.num_allocated, 0);
}
