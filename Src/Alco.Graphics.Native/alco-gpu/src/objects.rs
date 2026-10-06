//! Box-owned resource objects addressed through typed ABI pointers.
//! Each object retains its device context and unregisters its core identity
//! before releasing that context. Callers order access against destruction.

use crate::abi::*;
use crate::convert::*;
use crate::device::DeviceCtx;
use crate::entry::{set_error, set_error_from};
use crate::handle::Handle;
use std::borrow::Cow;
use std::ffi::{c_char, c_void, CStr, CString};
use std::mem::MaybeUninit;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, Mutex};
use wgpu_core as wgc;
use wgpu_types as wgt;

/// Completion state shared by a buffer and its asynchronous map callback.
/// The callback remains safe even when the buffer is destroyed first.
pub struct MapCompletion {
    /// Published state: zero pending, one mapped, two failed.
    pub state: AtomicU32,
    /// Lazily allocated failure text, published before the failed state.
    pub error: Mutex<Option<CString>>,
}

/// Owned core buffer registration and its optional map-completion cell.
pub struct BufferObj {
    /// Core buffer identity.
    pub id: wgc::id::BufferId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
    /// Completion holder changed only through caller-exclusive mapping access.
    pub map_completion: Option<Arc<MapCompletion>>,
}

impl Drop for BufferObj {
    fn drop(&mut self) {
        self.ctx.global.buffer_drop(self.id);
    }
}

/// Core texture registration and the metadata reported through the ABI.
pub struct TextureObj {
    /// Core texture identity.
    pub id: wgc::id::TextureId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
    /// Texture width in texels.
    pub width: u32,
    /// Texture height in texels.
    pub height: u32,
    /// Texture depth or array-layer count.
    pub depth_or_array_layers: u32,
    /// Number of mip levels.
    pub mip_level_count: u32,
    /// C# pixel-format value.
    pub format: u32,
    /// Whether this acquired texture must be released instead of destroyed.
    pub is_surface_texture: bool,
    /// Shared parent surface state retained by an acquired texture.
    pub surface: Option<Arc<crate::surface::SurfaceObj>>,
}

impl Drop for TextureObj {
    fn drop(&mut self) {
        if self.is_surface_texture {
            let _ = crate::surface::cleanup_texture(self);
        } else {
            self.ctx.global.texture_drop(self.id);
        }
    }
}

/// Owned core texture-view registration.
pub struct TextureViewObj {
    /// Core texture-view identity.
    pub id: wgc::id::TextureViewId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
    /// Parent surface retained until the view core identity is unregistered.
    pub surface: Option<Arc<crate::surface::SurfaceObj>>,
}

impl Drop for TextureViewObj {
    fn drop(&mut self) {
        self.ctx.global.texture_view_drop(self.id);
    }
}

/// Owned core sampler registration.
pub struct SamplerObj {
    /// Core sampler identity.
    pub id: wgc::id::SamplerId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for SamplerObj {
    fn drop(&mut self) {
        self.ctx.global.sampler_drop(self.id);
    }
}

/// Owned core shader-module registration.
pub struct ShaderModuleObj {
    /// Core shader-module identity.
    pub id: wgc::id::ShaderModuleId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for ShaderModuleObj {
    fn drop(&mut self) {
        self.ctx.global.shader_module_drop(self.id);
    }
}

/// Owned core bind-group-layout registration.
pub struct BindGroupLayoutObj {
    /// Core bind-group-layout identity.
    pub id: wgc::id::BindGroupLayoutId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for BindGroupLayoutObj {
    fn drop(&mut self) {
        self.ctx.global.bind_group_layout_drop(self.id);
    }
}

/// Owned core bind-group registration.
pub struct BindGroupObj {
    /// Core bind-group identity.
    pub id: wgc::id::BindGroupId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for BindGroupObj {
    fn drop(&mut self) {
        self.ctx.global.bind_group_drop(self.id);
    }
}

/// Owned core query-set registration.
pub struct QuerySetObj {
    /// Core query-set identity.
    pub id: wgc::id::QuerySetId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for QuerySetObj {
    fn drop(&mut self) {
        self.ctx.global.query_set_drop(self.id);
    }
}

/// Borrows a call-scoped UTF-8 name, replacing malformed UTF-8 only when needed.
///
/// # Safety
/// A non-null pointer must reference a NUL-terminated string valid for `'a`.
pub(crate) unsafe fn borrow_label<'a>(ptr: *const c_char) -> Cow<'a, str> {
    if ptr.is_null() {
        Cow::Borrowed("")
    } else {
        CStr::from_ptr(ptr).to_string_lossy()
    }
}

/// Converts an ABI name pointer into a call-scoped wgpu label.
///
/// # Safety
/// A non-null pointer must reference a NUL-terminated string valid for `'a`.
pub(crate) unsafe fn label<'a>(ptr: *const c_char) -> Option<Cow<'a, str>> {
    if ptr.is_null() {
        None
    } else {
        Some(borrow_label(ptr))
    }
}

/// Call-scoped descriptor storage using an inline array for common counts.
pub(crate) struct DescriptorStorage<T, const N: usize> {
    inline: [MaybeUninit<T>; N],
    len: usize,
    overflow: Option<Vec<T>>,
}

impl<T, const N: usize> DescriptorStorage<T, N> {
    /// Reserves heap storage only when the requested count exceeds the inline capacity.
    pub(crate) fn new(count: usize) -> Self {
        Self {
            inline: std::array::from_fn(|_| MaybeUninit::uninit()),
            len: 0,
            overflow: (count > N).then(|| Vec::with_capacity(count)),
        }
    }

    /// Appends one converted descriptor entry.
    pub(crate) fn push(&mut self, value: T) {
        if let Some(values) = self.overflow.as_mut() {
            values.push(value);
        } else {
            assert!(self.len < N, "descriptor inline capacity exceeded");
            self.inline[self.len].write(value);
            self.len += 1;
        }
    }

    /// Borrows all initialized entries for the synchronous core call.
    pub(crate) fn as_slice(&self) -> &[T] {
        match self.overflow.as_ref() {
            Some(values) => values,
            // SAFETY: push initializes exactly the prefix tracked by len;
            // MaybeUninit<T> has T's layout and no mutation occurs during this borrow.
            None => unsafe { std::slice::from_raw_parts(self.inline.as_ptr().cast(), self.len) },
        }
    }
}

impl<T, const N: usize> Drop for DescriptorStorage<T, N> {
    fn drop(&mut self) {
        for index in 0..self.len {
            // SAFETY: only the initialized inline prefix is counted in len.
            unsafe { self.inline[index].assume_init_drop() };
        }
    }
}

macro_rules! object_ref {
    ($handle:expr, $message:expr) => {
        match $handle.get() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, $message);
                return status;
            }
        }
    };
}

// ---------------------------------------------------------------------------
// ABI descriptor structs
// ---------------------------------------------------------------------------

/// C# `BufferDescriptor`.
#[repr(C)]
pub struct BufferDesc {
    /// Buffer allocation size in bytes.
    pub size: u64,
    /// C# `BufferUsage` bits.
    pub usage: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// C# `TextureDescriptor`.
#[repr(C)]
pub struct TextureDesc {
    /// C# texture or view dimension discriminant.
    pub dimension: u32,
    /// C# pixel-format discriminant.
    pub format: u32,
    /// C# `TextureUsage` bits.
    pub usage: u32,
    /// Width in texels.
    pub width: u32,
    /// Height in texels.
    pub height: u32,
    /// Depth in texels or array-layer count.
    pub depth_or_array_layers: u32,
    /// Number of mip levels in the texture.
    pub mip_level_count: u32,
    /// Number of samples per texel.
    pub sample_count: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// Result of `texture_get_info`.
#[repr(C)]
pub struct TextureInfo {
    /// Width in texels.
    pub width: u32,
    /// Height in texels.
    pub height: u32,
    /// Depth in texels or array-layer count.
    pub depth_or_array_layers: u32,
    /// Number of mip levels in the texture.
    pub mip_level_count: u32,
    /// C# pixel-format discriminant.
    pub format: u32,
}

/// C# `TextureViewDescriptor`.
#[repr(C)]
pub struct TextureViewDesc {
    /// C# texture or view dimension discriminant.
    pub dimension: u32,
    /// First mip level included in the view.
    pub base_mip_level: u32,
    /// Number of mip levels; zero selects the remaining view range.
    pub mip_level_count: u32,
    /// First array layer included in the view.
    pub base_array_layer: u32,
    /// Number of array layers; zero selects the remaining range.
    pub array_layer_count: u32,
    /// C# texture-aspect discriminant.
    pub aspect: u32,
    /// C# pixel-format discriminant.
    pub format: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// C# `SamplerDescriptor`.
#[repr(C)]
pub struct SamplerDesc {
    /// C# minification filter discriminant.
    pub min_filter: u32,
    /// C# magnification filter discriminant.
    pub mag_filter: u32,
    /// C# mipmap filter discriminant.
    pub mipmap_filter: u32,
    /// C# address mode for the U coordinate.
    pub address_u: u32,
    /// C# address mode for the V coordinate.
    pub address_v: u32,
    /// C# address mode for the W coordinate.
    pub address_w: u32,
    /// Minimum sampled level of detail.
    pub lod_min_clamp: f32,
    /// Maximum sampled level of detail.
    pub lod_max_clamp: f32,
    /// C# comparison function; zero disables comparison sampling.
    pub compare: u32,
    /// Maximum anisotropy; values below one are clamped to one.
    pub max_anisotropy: u16,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// C# `ShaderLanguage`: 2 SpirV, 3 Wgsl, 4 Dxil, 5 Msl, 6 MetalLib.
pub mod shader_language {
    pub const SPIRV: u32 = 2;
    pub const WGSL: u32 = 3;
    pub const DXIL: u32 = 4;
    pub const MSL: u32 = 5;
    pub const METALLIB: u32 = 6;
}

/// Flag bits of `ShaderModuleDesc::flags`; unknown bits are ignored.
pub mod shader_module_flags {
    /// SPIR-V input already matches Naga's coordinate convention (Slang's
    /// direct emission): skip the GL-style Y adjustment during translation.
    pub const SPIRV_ADJUSTED_COORDINATES: u32 = 1 << 0;
}

/// C# `ShaderModule` (bytes + entry point + workgroup size).
#[repr(C)]
pub struct ShaderModuleDesc {
    /// Shader-language discriminant from shader_language.
    pub language: u32,
    /// Call-scoped pointer to shader source bytes.
    pub data: *const u8,
    /// Byte count. For SPIR-V the native side divides by 4 (dword count).
    pub size: u32,
    /// NUL-terminated UTF-8 entry-point name borrowed for the call.
    pub entry_point: *const c_char,
    /// Declared workgroup width for passthrough shaders.
    pub workgroup_x: u32,
    /// Declared workgroup height for passthrough shaders.
    pub workgroup_y: u32,
    /// Declared workgroup depth for passthrough shaders.
    pub workgroup_z: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
    /// shader_module_flags bit set; unknown bits are ignored.
    pub flags: u32,
}

/// One entry of C# `BindGroupDescriptor.Bindings`.
#[repr(C)]
pub struct BindGroupLayoutEntry {
    /// Shader binding index.
    pub binding: u32,
    /// C# `ShaderStage` bits.
    pub visibility: u32,
    /// C# `BindingType` value.
    pub ty: u32,
    /// For sampler bindings: 0 filtering, 1 non-filtering, 2 comparison.
    pub sampler_kind: u32,
    /// For texture bindings: C# `TextureSampleType`.
    pub texture_sample_type: u32,
    /// For texture/storage bindings: C# `TextureViewDimension`.
    pub view_dimension: u32,
    /// For storage textures: C# `AccessMode` bits (1 read, 2 write).
    pub storage_access: u32,
    /// For storage textures: C# `PixelFormat`.
    pub storage_format: u32,
}

/// Bind-group-layout entries and their optional debug name.
#[repr(C)]
pub struct BindGroupLayoutDesc {
    /// Pointer to entry_count initialized descriptor entries.
    pub entries: *const BindGroupLayoutEntry,
    /// Number of initialized entries; zero permits a null array.
    pub entry_count: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// One entry of C# `ResourceGroupDescriptor.Resources`.
#[repr(C)]
pub struct BindGroupEntry {
    /// Shader binding index.
    pub binding: u32,
    /// Buffer, texture-view or sampler handle.
    pub resource: *mut c_void,
    /// Byte offset within a bound buffer.
    pub offset: u64,
    /// Byte size of the buffer or bound range; zero selects the remaining binding range.
    pub size: u64,
    /// Resource kind: zero buffer, one texture view, two sampler. The pointer
    /// must reference the corresponding live object type.
    pub kind: u32,
}

/// Typed bind-group layout and the resources to bind.
#[repr(C)]
pub struct BindGroupDesc {
    /// Live typed bind-group-layout pointer.
    pub layout: BindGroupLayoutHandle,
    /// Pointer to entry_count initialized descriptor entries.
    pub entries: *const BindGroupEntry,
    /// Number of initialized entries; zero permits a null array.
    pub entry_count: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

// ---------------------------------------------------------------------------
// Buffer
// ---------------------------------------------------------------------------

/// ABI: creates a buffer.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_buffer(
    device: DeviceHandle,
    desc: *const BufferDesc,
    out: *mut BufferHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(Status::INVALID_ARGUMENT, "null descriptor or out pointer");
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let usage = match buffer_usage(desc.usage) {
            Ok(u) => u,
            Err(s) => return s,
        };
        let wdesc = wgt::BufferDescriptor {
            label: label(desc.name),
            size: desc.size,
            usage,
            mapped_at_creation: false,
        };
        let (id, err) = ctx.global.device_create_buffer(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.buffer_drop(id);
            return Status::VALIDATION;
        }
        let handle = BufferHandle::new(BufferObj {
            id,
            ctx: Arc::clone(ctx),
            map_completion: None,
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: consumes a buffer handle, destroying its allocation before unregistering it.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_destroy(buffer: BufferHandle) -> Status {
    crate::entry::guard(|| {
        let obj = match buffer.take() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid buffer handle");
                return status;
            }
        };
        obj.ctx.global.buffer_destroy(obj.id);
        drop(obj);
        Status::OK
    })
}

/// Shared read/write map initiation. Completion is poll-driven via
/// `buffer_map_poll`; the native callback only records the outcome into
/// a shared cell that stays valid regardless of object lifetime.
unsafe fn map_buffer(
    buffer: BufferHandle,
    offset: u64,
    size: u64,
    host: wgc::device::HostMap,
) -> Status {
    let obj = match buffer.get_mut() {
        Ok(obj) => obj,
        Err(status) => {
            set_error(status, "invalid buffer handle");
            return status;
        }
    };
    let completion = Arc::new(MapCompletion {
        state: AtomicU32::new(0),
        error: Mutex::new(None),
    });
    let callback_cell = Arc::clone(&completion);
    let callback = Box::new(
        move |result: wgc::resource::BufferAccessResult| match result {
            Ok(()) => callback_cell.state.store(1, Ordering::Release),
            Err(err) => {
                *callback_cell.error.lock().unwrap() =
                    Some(CString::new(err.to_string().replace('\0', "\\0")).unwrap());
                callback_cell.state.store(2, Ordering::Release);
            }
        },
    ) as wgc::resource::BufferMapCallback;
    let operation = wgc::resource::BufferMapOperation {
        host,
        callback: Some(callback),
    };
    match obj
        .ctx
        .global
        .buffer_map_async(obj.id, offset, Some(size), operation)
    {
        Ok(_) => {
            obj.map_completion = Some(completion);
            Status::OK
        }
        Err(e) => {
            set_error_from(Status::VALIDATION, &e);
            Status::VALIDATION
        }
    }
}

/// ABI: initiates a read map. Completion is poll-driven via
/// `buffer_map_poll`; the native callback only records the outcome into
/// a shared cell that stays valid regardless of object lifetime.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_map_read(
    buffer: BufferHandle,
    offset: u64,
    size: u64,
) -> Status {
    crate::entry::guard(|| map_buffer(buffer, offset, size, wgc::device::HostMap::Read))
}

/// ABI: initiates a write map. The mapped range from
/// `buffer_get_mapped_range` is writable until `buffer_unmap`.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_map_write(
    buffer: BufferHandle,
    offset: u64,
    size: u64,
) -> Status {
    crate::entry::guard(|| map_buffer(buffer, offset, size, wgc::device::HostMap::Write))
}

/// ABI: polls a pending map. `NOT_READY` = still mapping, `OK` = mapped
/// (range available via `buffer_get_mapped_range`), `VALIDATION` = failed.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_map_poll(buffer: BufferHandle) -> Status {
    crate::entry::guard(|| {
        let obj = object_ref!(buffer, "invalid buffer handle");
        let Some(completion) = obj.map_completion.as_ref() else {
            return Status::NOT_READY;
        };
        match completion.state.load(Ordering::Acquire) {
            1 => Status::OK,
            2 => {
                let error = completion.error.lock().unwrap();
                let message = error.as_ref().expect("failed map must publish its error");
                set_error(Status::VALIDATION, message.to_string_lossy());
                Status::VALIDATION
            }
            _ => Status::NOT_READY,
        }
    })
}

/// ABI: returns the mapped range pointer — read-only after a read map,
/// writable after a write map until `buffer_unmap`.
///
/// # Safety
/// All live typed handles must outlive the call; destruction must not overlap access.
/// `out` must be a valid `*const u8` slot; the range is valid until unmap.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_get_mapped_range(
    buffer: BufferHandle,
    offset: u64,
    size: u64,
    out: *mut *const u8,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let obj = object_ref!(buffer, "invalid buffer handle");
        match obj
            .ctx
            .global
            .buffer_get_mapped_range(obj.id, offset, Some(size))
        {
            Ok((ptr, len)) if len >= size => {
                *out = ptr.as_ptr();
                Status::OK
            }
            Ok(_) => {
                set_error(Status::VALIDATION, "mapped range smaller than requested");
                Status::VALIDATION
            }
            Err(e) => {
                set_error_from(Status::VALIDATION, &e);
                Status::VALIDATION
            }
        }
    })
}

/// ABI: unmaps the buffer and resets the map state machine.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn buffer_unmap(buffer: BufferHandle) -> Status {
    crate::entry::guard(|| {
        let obj = match buffer.get_mut() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid buffer handle");
                return status;
            }
        };
        if let Err(e) = obj.ctx.global.buffer_unmap(obj.id) {
            set_error_from(Status::VALIDATION, &e);
            return Status::VALIDATION;
        }
        obj.map_completion = None;
        Status::OK
    })
}

// ---------------------------------------------------------------------------
// Texture / view
// ---------------------------------------------------------------------------

/// ABI: creates a texture.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_texture(
    device: DeviceHandle,
    desc: *const TextureDesc,
    out: *mut TextureHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(Status::INVALID_ARGUMENT, "null descriptor or out pointer");
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let format = match pixel_format(desc.format) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let dimension = match texture_dimension(desc.dimension) {
            Ok(d) => d,
            Err(s) => return s,
        };
        let wdesc = wgt::TextureDescriptor {
            label: label(desc.name),
            size: wgt::Extent3d {
                width: desc.width,
                height: desc.height,
                depth_or_array_layers: desc.depth_or_array_layers,
            },
            mip_level_count: desc.mip_level_count,
            sample_count: desc.sample_count,
            dimension,
            format,
            usage: texture_usage(desc.usage),
            view_formats: Vec::new(),
        };
        let (id, err) = ctx
            .global
            .device_create_texture(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.texture_drop(id);
            return Status::VALIDATION;
        }
        let handle = TextureHandle::new(TextureObj {
            id,
            ctx: Arc::clone(ctx),
            width: desc.width,
            height: desc.height,
            depth_or_array_layers: desc.depth_or_array_layers,
            mip_level_count: desc.mip_level_count,
            format: desc.format,
            is_surface_texture: false,
            surface: None,
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: destroys a texture; surface textures are rejected (release-only).
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn texture_destroy(texture: TextureHandle) -> Status {
    crate::entry::guard(|| {
        // Reject the wrong lifecycle operation without consuming the handle.
        if object_ref!(texture, "invalid texture handle").is_surface_texture {
            set_error(
                Status::INVALID_ARGUMENT,
                "surface textures must be released, not destroyed",
            );
            return Status::INVALID_ARGUMENT;
        }
        let obj = match texture.take() {
            Ok(obj) => obj,
            Err(status) => {
                set_error(status, "invalid texture handle");
                return status;
            }
        };
        obj.ctx.global.texture_destroy(obj.id);
        drop(obj);
        Status::OK
    })
}

/// ABI: fills texture info (used for acquired surface textures).
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn texture_get_info(
    texture: TextureHandle,
    out: *mut TextureInfo,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let obj = object_ref!(texture, "invalid texture handle");
        *out = TextureInfo {
            width: obj.width,
            height: obj.height,
            depth_or_array_layers: obj.depth_or_array_layers,
            mip_level_count: obj.mip_level_count,
            format: obj.format,
        };
        Status::OK
    })
}

/// ABI: creates a texture view. Pass a null `desc` for the default view
/// (used for per-frame surface textures).
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn texture_create_view(
    texture: TextureHandle,
    desc: *const TextureViewDesc,
    out: *mut TextureViewHandle,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(Status::INVALID_ARGUMENT, "null out pointer");
            return Status::INVALID_ARGUMENT;
        }
        let obj = object_ref!(texture, "invalid texture handle");
        let ctx = &obj.ctx;
        let wdesc = match desc.as_ref() {
            Some(d) => {
                let dimension = match texture_view_dimension(d.dimension) {
                    Ok(v) => v,
                    Err(s) => return s,
                };
                let aspect = match texture_aspect(d.aspect) {
                    Ok(v) => v,
                    Err(s) => return s,
                };
                // Format 0 (C# Undefined) inherits the texture format.
                let format = match d.format {
                    0 => None,
                    v => match pixel_format(v) {
                        Ok(f) => Some(f),
                        Err(s) => return s,
                    },
                };
                wgc::resource::TextureViewDescriptor {
                    label: label(d.name),
                    format,
                    dimension: Some(dimension),
                    usage: None,
                    range: wgt::ImageSubresourceRange {
                        aspect,
                        base_mip_level: d.base_mip_level,
                        mip_level_count: plain_or_none(d.mip_level_count),
                        base_array_layer: d.base_array_layer,
                        array_layer_count: plain_or_none(d.array_layer_count),
                    },
                }
            }
            None => wgc::resource::TextureViewDescriptor {
                label: None,
                format: None,
                dimension: None,
                usage: None,
                range: wgt::ImageSubresourceRange {
                    aspect: wgt::TextureAspect::All,
                    base_mip_level: 0,
                    mip_level_count: None,
                    base_array_layer: 0,
                    array_layer_count: None,
                },
            },
        };
        let (id, err) = ctx.global.texture_create_view(obj.id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.texture_view_drop(id);
            return Status::VALIDATION;
        }
        *out = TextureViewHandle::new(TextureViewObj {
            id,
            ctx: Arc::clone(ctx),
            surface: obj.surface.clone(),
        });
        Status::OK
    })
}

/// ABI: destroys a texture view.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn texture_view_destroy(view: TextureViewHandle) -> Status {
    crate::entry::guard(|| match view.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid texture view handle");
            status
        }
    })
}

pub(crate) fn plain_or_none(v: u32) -> Option<u32> {
    if v == 0 || v == NONE {
        None
    } else {
        Some(v)
    }
}

// ---------------------------------------------------------------------------
// Sampler
// ---------------------------------------------------------------------------

/// ABI: creates a sampler.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_sampler(
    device: DeviceHandle,
    desc: *const SamplerDesc,
    out: *mut SamplerHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() => d,
            _ => {
                set_error(Status::INVALID_ARGUMENT, "null descriptor or out pointer");
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let min_filter = match filter_mode(desc.min_filter) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let mag_filter = match filter_mode(desc.mag_filter) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let mipmap_filter = match mipmap_filter_mode(desc.mipmap_filter) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let address_u = match address_mode(desc.address_u) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let address_v = match address_mode(desc.address_v) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let address_w = match address_mode(desc.address_w) {
            Ok(f) => f,
            Err(s) => return s,
        };
        let compare = match optional_compare_function(desc.compare) {
            Ok(c) => c,
            Err(s) => return s,
        };
        let wdesc = wgc::resource::SamplerDescriptor {
            label: label(desc.name),
            address_modes: [address_u, address_v, address_w],
            mag_filter,
            min_filter,
            mipmap_filter,
            lod_min_clamp: desc.lod_min_clamp,
            lod_max_clamp: desc.lod_max_clamp,
            compare,
            // wgpu expects a clamp >= 1, where 1 means "no anisotropy".
            anisotropy_clamp: desc.max_anisotropy.max(1),
            border_color: None,
        };
        let (id, err) = ctx
            .global
            .device_create_sampler(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.sampler_drop(id);
            return Status::VALIDATION;
        }
        let handle = SamplerHandle::new(SamplerObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: destroys a sampler.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn sampler_destroy(sampler: SamplerHandle) -> Status {
    crate::entry::guard(|| match sampler.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid sampler handle");
            status
        }
    })
}

// ---------------------------------------------------------------------------
// Shader module
// ---------------------------------------------------------------------------

/// <summary>
/// ABI: creates a shader module. DX12 SPIR-V and WGSL go through Naga;
/// other supported native shader formats use passthrough. Source payloads are
/// borrowed only for this synchronous call.
/// </summary>
/// <param name="device">The device handle that will own the shader module.</param>
/// <param name="desc">The shader source and entry-point descriptor.</param>
/// <param name="out">Receives the shader module handle on success.</param>
/// <returns>The creation status.</returns>
///
/// # Safety
/// All live typed handles must outlive the call; destruction must not overlap access.
/// `desc` and its source bytes must be readable for the duration of the call;
/// non-null name and entry-point pointers must reference null-terminated strings.
/// `out` must point to a writable handle slot.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_shader_module(
    device: DeviceHandle,
    desc: *const ShaderModuleDesc,
    out: *mut ShaderModuleHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && !d.data.is_null() && d.size > 0 => d,
            _ => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    "null descriptor, out pointer or empty source",
                );
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let data = std::slice::from_raw_parts(desc.data, desc.size as usize);
        let entry_point = borrow_label(desc.entry_point);
        let module_label = label(desc.name);
        let workgroup = [desc.workgroup_x, desc.workgroup_y, desc.workgroup_z];

        let (id, err): (
            wgc::id::ShaderModuleId,
            Option<Box<dyn std::error::Error + Send + Sync>>,
        ) = match desc.language {
            shader_language::SPIRV => {
                let words = spirv_words(data);
                if ctx.caps & caps::PASSTHROUGH_SHADERS != 0
                    && ctx.backend != backend::RESOLVED_DX12
                {
                    let pdesc =
                        passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                            p.spirv = Some(words);
                        });
                    let (id, err) = ctx.global.device_create_shader_module_passthrough(
                        ctx.device_id,
                        &pdesc,
                        None,
                    );
                    (id, err.map(|e| Box::new(e) as _))
                } else {
                    // The coordinate convention is a property of the submitted
                    // bytes (Slang's direct SPIR-V is already adjusted), not of
                    // this backend; the caller declares it through the flag.
                    let source = wgc::pipeline::ShaderModuleSource::SpirV(
                        words,
                        wgc::naga::front::spv::Options {
                            adjust_coordinate_space: desc.flags
                                & shader_module_flags::SPIRV_ADJUSTED_COORDINATES
                                == 0,
                            ..Default::default()
                        },
                    );
                    let sdesc = wgc::pipeline::ShaderModuleDescriptor {
                        label: module_label.clone(),
                        runtime_checks: wgt::ShaderRuntimeChecks::checked(),
                    };
                    let (id, err) =
                        ctx.global
                            .device_create_shader_module(ctx.device_id, &sdesc, source, None);
                    (id, err.map(|e| Box::new(e) as _))
                }
            }
            shader_language::DXIL => {
                match require_passthrough(ctx) {
                    Ok(()) => {}
                    Err(s) => return s,
                }
                let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                    p.dxil = Some(std::borrow::Cow::Borrowed(data));
                });
                let (id, err) =
                    ctx.global
                        .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                (id, err.map(|e| Box::new(e) as _))
            }
            shader_language::MSL => {
                match require_passthrough(ctx) {
                    Ok(()) => {}
                    Err(s) => return s,
                }
                let source = String::from_utf8_lossy(data);
                let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                    p.msl = Some(source);
                });
                let (id, err) =
                    ctx.global
                        .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                (id, err.map(|e| Box::new(e) as _))
            }
            shader_language::METALLIB => {
                match require_passthrough(ctx) {
                    Ok(()) => {}
                    Err(s) => return s,
                }
                let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                    p.metallib = Some(std::borrow::Cow::Borrowed(data));
                });
                let (id, err) =
                    ctx.global
                        .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                (id, err.map(|e| Box::new(e) as _))
            }
            shader_language::WGSL => {
                let source = String::from_utf8_lossy(data);
                let sdesc = wgc::pipeline::ShaderModuleDescriptor {
                    label: module_label.clone(),
                    runtime_checks: wgt::ShaderRuntimeChecks::checked(),
                };
                let (id, err) = ctx.global.device_create_shader_module(
                    ctx.device_id,
                    &sdesc,
                    wgc::pipeline::ShaderModuleSource::Wgsl(source),
                    None,
                );
                (id, err.map(|e| Box::new(e) as _))
            }
            other => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    format!("unsupported shader language {other}"),
                );
                return Status::INVALID_ARGUMENT;
            }
        };

        if let Some(e) = err {
            set_error_from(Status::VALIDATION, e.as_ref());
            ctx.global.shader_module_drop(id);
            return Status::VALIDATION;
        }
        *out = ShaderModuleHandle::new(ShaderModuleObj {
            id,
            ctx: Arc::clone(ctx),
        });
        Status::OK
    })
}

fn spirv_words(data: &[u8]) -> std::borrow::Cow<'_, [u32]> {
    // Preserve the existing chunks_exact(4) behavior for incomplete trailing words.
    let data = &data[..data.len() / 4 * 4];
    #[cfg(target_endian = "little")]
    {
        // SAFETY: Every u32 bit pattern is valid. align_to checks alignment, and
        // borrowing is allowed only when the entire complete-word slice is aligned.
        // Native words match the existing little-endian decode on this target.
        let (prefix, words, suffix) = unsafe { data.align_to::<u32>() };
        if prefix.is_empty() && suffix.is_empty() {
            return std::borrow::Cow::Borrowed(words);
        }
    }
    let mut words = Vec::with_capacity(data.len() / 4);
    for chunk in data.chunks_exact(4) {
        words.push(u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]));
    }
    std::borrow::Cow::Owned(words)
}

fn require_passthrough(ctx: &DeviceCtx) -> Result<(), Status> {
    if ctx.caps & caps::PASSTHROUGH_SHADERS == 0 {
        set_error(
            Status::UNSUPPORTED,
            "this device does not support passthrough shaders",
        );
        return Err(Status::UNSUPPORTED);
    }
    Ok(())
}

fn passthrough_desc<'a>(
    module_label: Option<std::borrow::Cow<'a, str>>,
    entry_point: &'a str,
    workgroup: [u32; 3],
    fill: impl FnOnce(&mut wgc::pipeline::ShaderModuleDescriptorPassthrough<'a>),
) -> wgc::pipeline::ShaderModuleDescriptorPassthrough<'a> {
    let mut passthrough = wgt::CreateShaderModuleDescriptorPassthrough {
        label: module_label,
        entry_points: std::borrow::Cow::Owned(vec![wgt::PassthroughShaderEntryPoint {
            name: std::borrow::Cow::Borrowed(entry_point),
            workgroup_size: (workgroup[0], workgroup[1], workgroup[2]),
        }]),
        spirv: None,
        dxil: None,
        hlsl: None,
        metallib: None,
        msl: None,
        glsl: None,
        wgsl: None,
    };
    fill(&mut passthrough);
    passthrough
}

/// ABI: destroys a shader module.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn shader_module_destroy(module: ShaderModuleHandle) -> Status {
    crate::entry::guard(|| match module.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid shader module handle");
            status
        }
    })
}

// ---------------------------------------------------------------------------
// Bind group layout (C# GPUBindGroup) and bind group (C# GPUResourceGroup)
// ---------------------------------------------------------------------------

fn bind_group_layout_entry(
    entry: &BindGroupLayoutEntry,
) -> Result<wgt::BindGroupLayoutEntry, Status> {
    let ty = match entry.ty {
        // UniformBuffer
        1 => wgt::BindingType::Buffer {
            ty: wgt::BufferBindingType::Uniform,
            has_dynamic_offset: false,
            min_binding_size: None,
        },
        // StorageBuffer
        2 => wgt::BindingType::Buffer {
            ty: wgt::BufferBindingType::Storage { read_only: false },
            has_dynamic_offset: false,
            min_binding_size: None,
        },
        // Sampler
        3 => wgt::BindingType::Sampler(match entry.sampler_kind {
            1 => wgt::SamplerBindingType::NonFiltering,
            2 => wgt::SamplerBindingType::Comparison,
            _ => wgt::SamplerBindingType::Filtering,
        }),
        // Texture
        4 => wgt::BindingType::Texture {
            sample_type: texture_sample_type(entry.texture_sample_type)?,
            view_dimension: texture_view_dimension(entry.view_dimension)?,
            multisampled: false,
        },
        // StorageTexture
        5 => wgt::BindingType::StorageTexture {
            access: storage_texture_access(entry.storage_access)?,
            format: pixel_format(entry.storage_format)?,
            view_dimension: texture_view_dimension(entry.view_dimension)?,
        },
        // SamplerComparison
        6 => wgt::BindingType::Sampler(wgt::SamplerBindingType::Comparison),
        other => {
            set_error(
                Status::INVALID_ARGUMENT,
                format!("invalid binding type {other}"),
            );
            return Err(Status::INVALID_ARGUMENT);
        }
    };
    Ok(wgt::BindGroupLayoutEntry {
        binding: entry.binding,
        visibility: shader_stages(entry.visibility),
        ty,
        count: None,
    })
}

/// ABI: creates a bind group layout (the C# GPUBindGroup object), including
/// a valid empty layout when `entry_count` is zero.
///
/// # Safety
/// All live typed handles must outlive the call; destruction must not overlap access.
/// `desc` and `out` must be valid pointers. For a positive entry count,
/// `entries` must point to that many initialized layout entries.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_bind_group_layout(
    device: DeviceHandle,
    desc: *const BindGroupLayoutDesc,
    out: *mut BindGroupLayoutHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && (d.entry_count == 0 || !d.entries.is_null()) => d,
            _ => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    "null descriptor, out pointer or nonempty entry array",
                );
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let entries = if desc.entry_count == 0 {
            &[][..]
        } else {
            std::slice::from_raw_parts(desc.entries, desc.entry_count as usize)
        };
        let mut wentries = DescriptorStorage::<_, 16>::new(entries.len());
        for entry in entries {
            match bind_group_layout_entry(entry) {
                Ok(e) => wentries.push(e),
                Err(s) => return s,
            }
        }
        let wdesc = wgc::binding_model::BindGroupLayoutDescriptor {
            label: label(desc.name),
            entries: Cow::Borrowed(wentries.as_slice()),
        };
        let (id, err) = ctx
            .global
            .device_create_bind_group_layout(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.bind_group_layout_drop(id);
            return Status::VALIDATION;
        }
        let handle = BindGroupLayoutHandle::new(BindGroupLayoutObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: destroys a bind group layout.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn bind_group_layout_destroy(layout: BindGroupLayoutHandle) -> Status {
    crate::entry::guard(|| match layout.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid bind group layout handle");
            status
        }
    })
}

/// ABI: creates a bind group instance (the C# GPUResourceGroup object),
/// allowing zero entries for an empty layout.
///
/// # Safety
/// All live typed handles must outlive the call; destruction must not overlap access.
/// `desc` and `out` must be valid pointers. For a positive entry count,
/// `entries` must point to that many initialized binding entries.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_bind_group(
    device: DeviceHandle,
    desc: *const BindGroupDesc,
    out: *mut BindGroupHandle,
) -> Status {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && (d.entry_count == 0 || !d.entries.is_null()) => d,
            _ => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    "null descriptor, out pointer or nonempty entry array",
                );
                return Status::INVALID_ARGUMENT;
            }
        };
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let layout = object_ref!(desc.layout, "invalid bind group layout handle");
        debug_assert!(Arc::ptr_eq(ctx, &layout.ctx));
        let layout_id = layout.id;
        let entries = if desc.entry_count == 0 {
            &[][..]
        } else {
            std::slice::from_raw_parts(desc.entries, desc.entry_count as usize)
        };
        let mut wentries = DescriptorStorage::<_, 16>::new(entries.len());
        for entry in entries {
            let resource = match entry.kind {
                0 => {
                    let handle = Handle::<BufferObj>(entry.resource.cast());
                    let obj = object_ref!(
                        handle,
                        format!("invalid buffer handle in binding {}", entry.binding)
                    );
                    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
                    let id = obj.id;
                    wgc::binding_model::BindingResource::Buffer(wgc::binding_model::BufferBinding {
                        buffer: id,
                        offset: entry.offset,
                        size: if entry.size == 0 {
                            None
                        } else {
                            Some(entry.size)
                        },
                    })
                }
                1 => {
                    let handle = Handle::<TextureViewObj>(entry.resource.cast());
                    let obj = object_ref!(
                        handle,
                        format!("invalid texture view handle in binding {}", entry.binding)
                    );
                    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
                    let id = obj.id;
                    wgc::binding_model::BindingResource::TextureView(id)
                }
                2 => {
                    let handle = Handle::<SamplerObj>(entry.resource.cast());
                    let obj = object_ref!(
                        handle,
                        format!("invalid sampler handle in binding {}", entry.binding)
                    );
                    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
                    let id = obj.id;
                    wgc::binding_model::BindingResource::Sampler(id)
                }
                other => {
                    set_error(
                        Status::INVALID_ARGUMENT,
                        format!("invalid resource kind {other} in binding {}", entry.binding),
                    );
                    return Status::INVALID_ARGUMENT;
                }
            };
            wentries.push(wgc::binding_model::BindGroupEntry {
                binding: entry.binding,
                resource,
            });
        }
        let wdesc = wgc::binding_model::BindGroupDescriptor {
            label: label(desc.name),
            layout: layout_id,
            entries: Cow::Borrowed(wentries.as_slice()),
        };
        let (id, err) = ctx
            .global
            .device_create_bind_group(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.bind_group_drop(id);
            return Status::VALIDATION;
        }
        let handle = BindGroupHandle::new(BindGroupObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: destroys a bind group.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn bind_group_destroy(group: BindGroupHandle) -> Status {
    crate::entry::guard(|| match group.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid bind group handle");
            status
        }
    })
}

// ---------------------------------------------------------------------------
// Query set
// ---------------------------------------------------------------------------

/// ABI: creates a timestamp query set.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_query_set(
    device: DeviceHandle,
    count: u32,
    name: *const c_char,
    out: *mut QuerySetHandle,
) -> Status {
    crate::entry::guard(|| {
        if out.is_null() || count == 0 {
            set_error(Status::INVALID_ARGUMENT, "null out pointer or zero count");
            return Status::INVALID_ARGUMENT;
        }
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let wdesc = wgt::QuerySetDescriptor {
            label: label(name),
            ty: wgt::QueryType::Timestamp,
            count,
        };
        let (id, err) = ctx
            .global
            .device_create_query_set(ctx.device_id, &wdesc, None);
        if let Some(e) = err {
            set_error_from(Status::VALIDATION, &e);
            ctx.global.query_set_drop(id);
            return Status::VALIDATION;
        }
        let handle = QuerySetHandle::new(QuerySetObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        Status::OK
    })
}

/// ABI: destroys a query set (drop only — destroy+drop double-removes in wgpu).
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn query_set_destroy(query_set: QuerySetHandle) -> Status {
    crate::entry::guard(|| match query_set.take() {
        Ok(obj) => {
            drop(obj);
            Status::OK
        }
        Err(status) => {
            set_error(status, "invalid query set handle");
            status
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::commands::{device_create_encoder, encoder_copy_buffer_to_buffer, encoder_finish};
    use crate::device::device_poll;
    use crate::test_support::{last_error, TestDevice};
    use std::ptr;

    #[repr(align(4))]
    struct AlignedShaderBytes<const N: usize>([u8; N]);

    #[test]
    fn spirv_aligned_payload_matches_little_endian_words() {
        let data = AlignedShaderBytes([0x03, 0x02, 0x23, 0x07, 0x44, 0x33, 0x22, 0x11]);
        let words = spirv_words(&data.0);
        assert_eq!(words.as_ref(), &[0x0723_0203, 0x1122_3344]);
        #[cfg(target_endian = "little")]
        {
            assert!(matches!(&words, std::borrow::Cow::Borrowed(_)));
            assert_eq!(words.as_ptr().cast::<u8>(), data.0.as_ptr());
        }
        #[cfg(target_endian = "big")]
        assert!(matches!(&words, std::borrow::Cow::Owned(_)));
    }

    #[test]
    fn spirv_unaligned_payload_is_decoded_once_and_moved() {
        let data = AlignedShaderBytes([0xff, 0x03, 0x02, 0x23, 0x07, 0x44, 0x33, 0x22, 0x11]);
        let mut words = spirv_words(&data.0[1..]);
        assert!(matches!(&words, std::borrow::Cow::Owned(_)));
        assert_eq!(words.as_ref(), &[0x0723_0203, 0x1122_3344]);
        let allocation = words.as_ptr();
        assert_eq!(words.to_mut().as_ptr(), allocation);
        let descriptor = passthrough_desc(None, "main", [1, 2, 3], |p| {
            p.spirv = Some(words);
        });
        assert_eq!(descriptor.spirv.as_ref().unwrap().as_ptr(), allocation);
    }

    #[test]
    fn spirv_incomplete_or_malformed_payloads_preserve_word_decoding() {
        // No new length or magic validation: complete words are retained and
        // incomplete trailing bytes are ignored, exactly as before.
        let aligned = AlignedShaderBytes([0, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 0xaa, 0xbb, 0xcc]);
        let unaligned =
            AlignedShaderBytes([0x11, 0, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 0xaa, 0xbb, 0xcc]);
        for trailing in 0..=3 {
            assert_eq!(
                spirv_words(&aligned.0[..8 + trailing]).as_ref(),
                &[0, u32::MAX]
            );
            assert_eq!(
                spirv_words(&unaligned.0[1..9 + trailing]).as_ref(),
                &[0, u32::MAX]
            );
            assert!(spirv_words(&aligned.0[..trailing]).is_empty());
            assert!(spirv_words(&unaligned.0[1..1 + trailing]).is_empty());
        }
    }

    #[test]
    fn spirv_mutation_owns_storage_without_changing_source() {
        let data = AlignedShaderBytes([0x03, 0x02, 0x23, 0x07, 0x44, 0x33, 0x22, 0x11]);
        let mut words = spirv_words(&data.0);
        words.to_mut()[1] = 0x5566_7788;
        assert!(matches!(&words, std::borrow::Cow::Owned(_)));
        assert_eq!(words.as_ref(), &[0x0723_0203, 0x5566_7788]);
        assert_eq!(spirv_words(&data.0).as_ref(), &[0x0723_0203, 0x1122_3344]);
        assert_ne!(words.as_ptr().cast::<u8>(), data.0.as_ptr());
    }

    #[test]
    fn passthrough_descriptor_borrows_call_scoped_binary_payloads() {
        let entry_point = String::from("main");
        let payload = [0x44, 0x58, 0x49, 0x4c];
        let descriptor = passthrough_desc(None, &entry_point, [1, 2, 3], |p| {
            p.dxil = Some(std::borrow::Cow::Borrowed(&payload));
            p.metallib = Some(std::borrow::Cow::Borrowed(&payload));
        });
        for binary in [&descriptor.dxil, &descriptor.metallib] {
            let binary = binary.as_ref().unwrap();
            assert!(matches!(binary, std::borrow::Cow::Borrowed(_)));
            assert_eq!(binary.as_ptr(), payload.as_ptr());
        }
        let entry = &descriptor.entry_points[0];
        assert!(matches!(&entry.name, std::borrow::Cow::Borrowed(_)));
        assert_eq!(entry.name.as_ptr(), entry_point.as_ptr());
        assert_eq!(entry.workgroup_size, (1, 2, 3));
    }

    #[test]
    fn text_shader_payloads_preserve_lossy_utf8_and_borrow_valid_text() {
        for (bytes, expected, borrowed) in [
            (&b"shader source"[..], "shader source", true),
            (&b"shader \xff source"[..], "shader \u{fffd} source", false),
        ] {
            let descriptor = passthrough_desc(None, "main", [1, 1, 1], |p| {
                p.msl = Some(String::from_utf8_lossy(bytes));
            });
            let msl = descriptor.msl.as_ref().unwrap();
            assert_eq!(msl.as_ref(), expected);
            assert_eq!(matches!(msl, std::borrow::Cow::Borrowed(_)), borrowed);
            let wgc::pipeline::ShaderModuleSource::Wgsl(wgsl) =
                wgc::pipeline::ShaderModuleSource::Wgsl(String::from_utf8_lossy(bytes))
            else {
                unreachable!()
            };
            assert_eq!(wgsl.as_ref(), expected);
            assert_eq!(matches!(&wgsl, std::borrow::Cow::Borrowed(_)), borrowed);
            if borrowed {
                assert_eq!(msl.as_ptr(), bytes.as_ptr());
                assert_eq!(wgsl.as_ptr(), bytes.as_ptr());
            }
        }
    }

    #[test]
    fn positive_binding_counts_require_non_null_arrays_before_device_lookup() {
        unsafe {
            let layout = BindGroupLayoutDesc {
                entries: ptr::null(),
                entry_count: 1,
                name: ptr::null(),
            };
            let group = BindGroupDesc {
                layout: BindGroupLayoutHandle::NULL,
                entries: ptr::null(),
                entry_count: 1,
                name: ptr::null(),
            };
            let mut layout_out = BindGroupLayoutHandle::NULL;
            let mut group_out = BindGroupHandle::NULL;
            assert_eq!(
                device_create_bind_group_layout(DeviceHandle::NULL, &layout, &mut layout_out),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(
                device_create_bind_group(DeviceHandle::NULL, &group, &mut group_out),
                Status::INVALID_ARGUMENT
            );
        }
    }

    #[test]
    fn vulkan_empty_bindings_accept_null_and_valid_zero_length_arrays() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        let layout_placeholder = BindGroupLayoutEntry {
            binding: 0,
            visibility: 0,
            ty: 0,
            sampler_kind: 0,
            texture_sample_type: 0,
            view_dimension: 0,
            storage_access: 0,
            storage_format: 0,
        };
        let group_placeholder = BindGroupEntry {
            binding: 0,
            resource: ptr::null_mut(),
            offset: 0,
            size: 0,
            kind: 0,
        };
        unsafe {
            for entries in [ptr::null(), &layout_placeholder] {
                let desc = BindGroupLayoutDesc {
                    entries,
                    entry_count: 0,
                    name: ptr::null(),
                };
                let mut layout = BindGroupLayoutHandle::NULL;
                assert_eq!(
                    device_create_bind_group_layout(device.handle, &desc, &mut layout),
                    Status::OK,
                    "{}",
                    last_error()
                );
                assert!(!layout.is_null());
                for entries in [ptr::null(), &group_placeholder] {
                    let desc = BindGroupDesc {
                        layout,
                        entries,
                        entry_count: 0,
                        name: ptr::null(),
                    };
                    let mut group = BindGroupHandle::NULL;
                    assert_eq!(
                        device_create_bind_group(device.handle, &desc, &mut group),
                        Status::OK,
                        "{}",
                        last_error()
                    );
                    assert!(!group.is_null());
                    let report = device
                        .handle
                        .get()
                        .unwrap()
                        .ctx
                        .global
                        .generate_report()
                        .hub
                        .bind_groups;
                    assert_eq!(report.num_allocated, 1);
                    assert_eq!(report.num_kept_from_user, 1);
                    assert_eq!(bind_group_destroy(group), Status::OK);
                }
                assert_eq!(bind_group_layout_destroy(layout), Status::OK);
            }
            let report = device
                .handle
                .get()
                .unwrap()
                .ctx
                .global
                .generate_report()
                .hub;
            assert_eq!(report.bind_groups.num_allocated, 0);
            assert_eq!(report.bind_group_layouts.num_allocated, 0);
        }
    }

    #[test]
    fn labels_borrow_valid_utf8_and_preserve_lossy_conversion() {
        unsafe {
            assert!(label(ptr::null()).is_none());
            assert!(matches!(borrow_label(ptr::null()), Cow::Borrowed("")));
            let source = c"call-scoped label";
            let converted = label(source.as_ptr()).unwrap();
            assert!(matches!(&converted, Cow::Borrowed(_)));
            assert_eq!(converted.as_ptr(), source.as_ptr().cast());
            let malformed = CString::new(vec![b'a', 255]).unwrap();
            let converted = borrow_label(malformed.as_ptr());
            assert!(matches!(&converted, Cow::Owned(_)));
            assert_eq!(converted, format!("a{}", char::REPLACEMENT_CHARACTER));
        }
    }

    #[test]
    fn descriptor_storage_drops_inline_and_overflow_entries_once() {
        struct Tracked(Arc<AtomicU32>);
        impl Drop for Tracked {
            fn drop(&mut self) {
                self.0.fetch_add(1, Ordering::Relaxed);
            }
        }
        for count in [0, 2, 3] {
            let drops = Arc::new(AtomicU32::new(0));
            let mut values = DescriptorStorage::<_, 2>::new(count);
            assert_eq!(values.overflow.is_some(), count > 2);
            for _ in 0..count {
                values.push(Tracked(Arc::clone(&drops)));
            }
            assert_eq!(values.as_slice().len(), count);
            assert_eq!(drops.load(Ordering::Relaxed), 0);
            drop(values);
            assert_eq!(drops.load(Ordering::Relaxed), count as u32);
        }
    }

    #[test]
    fn resource_null_handles_fail_without_accessing_a_device() {
        unsafe {
            assert_eq!(buffer_destroy(BufferHandle::NULL), Status::INVALID_HANDLE);
            assert_eq!(
                buffer_map_read(BufferHandle::NULL, 0, 4),
                Status::INVALID_HANDLE
            );
            assert_eq!(buffer_map_poll(BufferHandle::NULL), Status::INVALID_HANDLE);
            assert_eq!(buffer_unmap(BufferHandle::NULL), Status::INVALID_HANDLE);
            assert_eq!(texture_destroy(TextureHandle::NULL), Status::INVALID_HANDLE);
            assert_eq!(
                texture_view_destroy(TextureViewHandle::NULL),
                Status::INVALID_HANDLE
            );
            assert_eq!(sampler_destroy(SamplerHandle::NULL), Status::INVALID_HANDLE);
            assert_eq!(
                shader_module_destroy(ShaderModuleHandle::NULL),
                Status::INVALID_HANDLE
            );
            assert_eq!(
                bind_group_layout_destroy(BindGroupLayoutHandle::NULL),
                Status::INVALID_HANDLE
            );
            assert_eq!(
                bind_group_destroy(BindGroupHandle::NULL),
                Status::INVALID_HANDLE
            );
            assert_eq!(
                query_set_destroy(QuerySetHandle::NULL),
                Status::INVALID_HANDLE
            );
        }
    }

    #[test]
    fn vulkan_map_poll_borrows_completion_without_arc_cloning() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let desc = BufferDesc {
                size: 16,
                usage: 1 | 8,
                name: ptr::null(),
            };
            let mut buffer = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &desc, &mut buffer),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(buffer_map_poll(buffer), Status::NOT_READY);
            let completion = Arc::new(MapCompletion {
                state: AtomicU32::new(0),
                error: Mutex::new(None),
            });
            buffer.get_mut().unwrap().map_completion = Some(Arc::clone(&completion));
            let count = Arc::strong_count(&completion);
            for _ in 0..32 {
                assert_eq!(buffer_map_poll(buffer), Status::NOT_READY);
                assert_eq!(Arc::strong_count(&completion), count);
            }
            assert!(completion.error.lock().unwrap().is_none());
            completion.state.store(1, Ordering::Release);
            assert_eq!(buffer_map_poll(buffer), Status::OK);
            *completion.error.lock().unwrap() = Some(CString::new("map failure").unwrap());
            completion.state.store(2, Ordering::Release);
            assert_eq!(buffer_map_poll(buffer), Status::VALIDATION);
            assert_eq!(last_error(), "map failure");
            assert_eq!(Arc::strong_count(&completion), count);
            assert_eq!(buffer_destroy(buffer), Status::OK);
            assert_eq!(Arc::strong_count(&completion), 1);
        }
    }

    #[test]
    fn vulkan_resources_retain_context_after_device_owner_is_destroyed() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let weak = Arc::downgrade(&device.handle.get().unwrap().ctx);
            let buffer_desc = BufferDesc {
                size: 16,
                usage: 8,
                name: ptr::null(),
            };
            let mut buffer = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &buffer_desc, &mut buffer),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(weak.strong_count(), 2);
            let texture_desc = TextureDesc {
                dimension: 1,
                format: 18,
                usage: 4,
                width: 4,
                height: 4,
                depth_or_array_layers: 1,
                mip_level_count: 1,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut texture = TextureHandle::NULL;
            assert_eq!(
                device_create_texture(device.handle, &texture_desc, &mut texture),
                Status::OK,
                "{}",
                last_error()
            );
            let mut view = TextureViewHandle::NULL;
            assert_eq!(
                texture_create_view(texture, ptr::null(), &mut view),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(weak.strong_count(), 4);
            drop(device);
            assert_eq!(weak.strong_count(), 3);
            let mut info = TextureInfo {
                width: 0,
                height: 0,
                depth_or_array_layers: 0,
                mip_level_count: 0,
                format: 0,
            };
            assert_eq!(texture_get_info(texture, &mut info), Status::OK);
            assert_eq!((info.width, info.height, info.format), (4, 4, 18));
            assert_eq!(buffer_destroy(buffer), Status::OK);
            assert_eq!(texture_destroy(texture), Status::OK);
            assert_eq!(weak.strong_count(), 1);
            assert_eq!(texture_view_destroy(view), Status::OK);
            assert!(weak.upgrade().is_none());
        }
    }

    #[test]
    fn vulkan_write_mapping_round_trips_through_read_mapping() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        const SIZE: u64 = 64;
        unsafe {
            let source_desc = BufferDesc {
                size: SIZE,
                usage: (1 << 1) | (1 << 2), // MAP_WRITE | COPY_SRC
                name: ptr::null(),
            };
            let destination_desc = BufferDesc {
                size: SIZE,
                usage: (1 << 0) | (1 << 3), // MAP_READ | COPY_DST
                name: ptr::null(),
            };
            let mut source = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &source_desc, &mut source),
                Status::OK,
                "{}",
                last_error()
            );
            let mut destination = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &destination_desc, &mut destination),
                Status::OK,
                "{}",
                last_error()
            );
            // The write map completes during blocking device maintenance; the
            // mapped range is writable for the requested size.
            assert_eq!(buffer_map_write(source, 0, SIZE), Status::OK);
            assert_eq!(
                device_poll(device.handle, TRUE, u64::MAX, ptr::null_mut()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(buffer_map_poll(source), Status::OK, "{}", last_error());
            let mut range = std::ptr::null();
            assert_eq!(
                buffer_get_mapped_range(source, 0, SIZE, &mut range),
                Status::OK,
                "{}",
                last_error()
            );
            let mapped = range as *mut u8;
            for element in 0..SIZE as usize {
                *mapped.add(element) = (element * 5 % 251) as u8;
            }
            assert_eq!(buffer_unmap(source), Status::OK);
            let mut enc = EncoderHandle::NULL;
            assert_eq!(
                device_create_encoder(device.handle, ptr::null(), &mut enc),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_copy_buffer_to_buffer(enc, source, 0, destination, 0, SIZE),
                Status::OK,
                "{}",
                last_error()
            );
            let mut command = CommandBufferHandle::NULL;
            assert_eq!(
                encoder_finish(enc, &mut command),
                Status::OK,
                "{}",
                last_error()
            );
            let mut index = 0;
            assert_eq!(
                crate::commands::queue_submit(device.handle, command, &mut index),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                device_poll(device.handle, TRUE, index, ptr::null_mut()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                buffer_map_read(destination, 0, SIZE),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                device_poll(device.handle, TRUE, u64::MAX, ptr::null_mut()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(buffer_map_poll(destination), Status::OK, "{}", last_error());
            let mut read = std::ptr::null();
            assert_eq!(
                buffer_get_mapped_range(destination, 0, SIZE, &mut read),
                Status::OK,
                "{}",
                last_error()
            );
            for element in 0..SIZE as usize {
                assert_eq!(
                    *read.add(element),
                    (element * 5 % 251) as u8,
                    "element {element}"
                );
            }
            assert_eq!(buffer_unmap(destination), Status::OK);
            assert_eq!(buffer_destroy(source), Status::OK);
            assert_eq!(buffer_destroy(destination), Status::OK);
        }
    }
}
