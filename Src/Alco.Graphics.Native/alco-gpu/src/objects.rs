//! Resource objects: buffers, textures, views, samplers, shader modules,
//! bind-group layouts/instances and query sets. Every object lives in a
//! per-device handle table so stale handles fail with `INVALID_HANDLE`.
//! Object destroy functions take the device handle first for uniform error
//! attribution.

use crate::abi::*;
use crate::convert::*;
use crate::device::{DeviceCtx, DEVICES};
use crate::entry::{set_error, set_error_from};
use crate::handle::HandleTable;
use std::ffi::{c_char, CStr, CString};
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, Mutex};
use wgpu_core as wgc;
use wgpu_types as wgt;

/// Shared map-completion cell owned by both the buffer object and the native
/// map callback, so completion is recorded safely even if the buffer is
/// destroyed before the callback fires.
pub(crate) struct MapCompletion {
    /// 0 = pending, 1 = mapped, 2 = failed.
    pub state: AtomicU32,
    pub error: Mutex<CString>,
}

pub(crate) struct BufferObj {
    pub id: wgc::id::BufferId,
    /// Present while a map is pending/mapped; taken by unmap.
    pub map_completion: Option<Arc<MapCompletion>>,
}

pub(crate) struct TextureObj {
    pub id: wgc::id::TextureId,
    pub width: u32,
    pub height: u32,
    pub depth_or_array_layers: u32,
    pub mip_level_count: u32,
    pub format: u32,
    /// True when this texture is an acquired surface texture: it must be
    /// released (never destroyed) — matching the old backend's contract.
    pub is_surface_texture: bool,
}

pub(crate) struct TextureViewObj {
    pub id: wgc::id::TextureViewId,
}

pub(crate) struct SamplerObj {
    pub id: wgc::id::SamplerId,
}

pub(crate) struct ShaderModuleObj {
    pub id: wgc::id::ShaderModuleId,
}

pub(crate) struct BindGroupLayoutObj {
    pub id: wgc::id::BindGroupLayoutId,
}

pub(crate) struct BindGroupObj {
    pub id: wgc::id::BindGroupId,
}

pub(crate) struct QuerySetObj {
    pub id: wgc::id::QuerySetId,
}

pub(crate) struct ObjectTables {
    pub buffers: HandleTable<BufferObj>,
    pub textures: HandleTable<TextureObj>,
    pub views: HandleTable<TextureViewObj>,
    pub samplers: HandleTable<SamplerObj>,
    pub shader_modules: HandleTable<ShaderModuleObj>,
    pub bind_group_layouts: HandleTable<BindGroupLayoutObj>,
    pub bind_groups: HandleTable<BindGroupObj>,
    pub query_sets: HandleTable<QuerySetObj>,
    pub pipelines: HandleTable<crate::pipeline::PipelineObj>,
}

impl Default for ObjectTables {
    fn default() -> Self {
        Self {
            buffers: HandleTable::new(),
            textures: HandleTable::new(),
            views: HandleTable::new(),
            samplers: HandleTable::new(),
            shader_modules: HandleTable::new(),
            bind_group_layouts: HandleTable::new(),
            bind_groups: HandleTable::new(),
            query_sets: HandleTable::new(),
            pipelines: HandleTable::new(),
        }
    }
}

macro_rules! table_accessors {
    ($($method:ident => $table:ident => $ty:ty),* $(,)?) => {
        impl DeviceCtx {
            $(pub fn $method(&self) -> &HandleTable<$ty> {
                &self.objects.$table
            })*
        }
    };
}

table_accessors! {
    buffers => buffers => BufferObj,
    textures => textures => TextureObj,
    views => views => TextureViewObj,
    samplers => samplers => SamplerObj,
    shader_modules => shader_modules => ShaderModuleObj,
    bind_group_layouts => bind_group_layouts => BindGroupLayoutObj,
    bind_groups => bind_groups => BindGroupObj,
    query_sets => query_sets => QuerySetObj,
    pipelines => pipelines => crate::pipeline::PipelineObj,
}

pub(crate) unsafe fn borrow_label(ptr: *const c_char) -> String {
    if ptr.is_null() {
        String::new()
    } else {
        CStr::from_ptr(ptr).to_string_lossy().into_owned()
    }
}

/// Converts an ABI name pointer into a wgpu `Label` (`Option<Cow<str>>`).
pub(crate) unsafe fn label(ptr: *const c_char) -> Option<std::borrow::Cow<'static, str>> {
    if ptr.is_null() {
        None
    } else {
        Some(std::borrow::Cow::Owned(
            CStr::from_ptr(ptr).to_string_lossy().into_owned(),
        ))
    }
}

// ---------------------------------------------------------------------------
// ABI descriptor structs
// ---------------------------------------------------------------------------

/// C# `BufferDescriptor`.
#[repr(C)]
pub struct AlcoBufferDesc {
    pub size: u64,
    /// C# `BufferUsage` bits.
    pub usage: u32,
    pub name: *const c_char,
}

/// C# `TextureDescriptor`.
#[repr(C)]
pub struct AlcoTextureDesc {
    pub dimension: u32,
    pub format: u32,
    /// C# `TextureUsage` bits.
    pub usage: u32,
    pub width: u32,
    pub height: u32,
    pub depth_or_array_layers: u32,
    pub mip_level_count: u32,
    pub sample_count: u32,
    pub name: *const c_char,
}

/// Result of `alco_texture_get_info`.
#[repr(C)]
pub struct AlcoTextureInfo {
    pub width: u32,
    pub height: u32,
    pub depth_or_array_layers: u32,
    pub mip_level_count: u32,
    pub format: u32,
}

/// C# `TextureViewDescriptor`.
#[repr(C)]
pub struct AlcoTextureViewDesc {
    pub dimension: u32,
    pub base_mip_level: u32,
    pub mip_level_count: u32,
    pub base_array_layer: u32,
    pub array_layer_count: u32,
    pub aspect: u32,
    pub format: u32,
    pub name: *const c_char,
}

/// C# `SamplerDescriptor`.
#[repr(C)]
pub struct AlcoSamplerDesc {
    pub min_filter: u32,
    pub mag_filter: u32,
    pub mipmap_filter: u32,
    pub address_u: u32,
    pub address_v: u32,
    pub address_w: u32,
    pub lod_min_clamp: f32,
    pub lod_max_clamp: f32,
    pub compare: u32,
    pub max_anisotropy: u16,
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

/// C# `ShaderModule` (bytes + entry point + workgroup size).
#[repr(C)]
pub struct AlcoShaderModuleDesc {
    pub language: u32,
    pub data: *const u8,
    /// Byte count. For SPIR-V the native side divides by 4 (dword count).
    pub size: u32,
    pub entry_point: *const c_char,
    pub workgroup_x: u32,
    pub workgroup_y: u32,
    pub workgroup_z: u32,
    pub name: *const c_char,
}

/// One entry of C# `BindGroupDescriptor.Bindings`.
#[repr(C)]
pub struct AlcoBindGroupLayoutEntry {
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

#[repr(C)]
pub struct AlcoBindGroupLayoutDesc {
    pub entries: *const AlcoBindGroupLayoutEntry,
    pub entry_count: u32,
    pub name: *const c_char,
}

/// One entry of C# `ResourceGroupDescriptor.Resources`.
#[repr(C)]
pub struct AlcoBindGroupEntry {
    pub binding: u32,
    /// Buffer, texture-view or sampler handle.
    pub resource: AlcoHandle,
    pub offset: u64,
    pub size: u64,
    /// Resource kind: 0 buffer, 1 texture view, 2 sampler. Required because
    /// handles from different per-type tables may collide numerically, so the
    /// kind cannot be recovered by probing tables.
    pub kind: u32,
}

#[repr(C)]
pub struct AlcoBindGroupDesc {
    pub layout: AlcoHandle,
    pub entries: *const AlcoBindGroupEntry,
    pub entry_count: u32,
    pub name: *const c_char,
}

// ---------------------------------------------------------------------------
// Buffer
// ---------------------------------------------------------------------------

/// ABI: creates a buffer.
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_create(
    device: AlcoHandle,
    desc: *const AlcoBufferDesc,
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
            .with(device, move |ctx| {
                let usage = match buffer_usage(desc.usage) {
                    Ok(u) => u,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let wdesc = wgt::BufferDescriptor {
                    label: label(desc.name),
                    size: desc.size,
                    usage,
                    mapped_at_creation: false,
                };
                let (id, err) = ctx.global.device_create_buffer(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.buffer_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.buffers().insert(BufferObj { id, map_completion: None });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a buffer (generational — double destroy returns INVALID_HANDLE).
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_destroy(device: AlcoHandle, buffer: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.buffers().remove(buffer) {
                Ok(obj) => {
                    ctx.global.buffer_destroy(obj.id);
                    ctx.global.buffer_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: initiates a read map. Completion is poll-driven via
/// `alco_buffer_map_poll`; the native callback only records the outcome into
/// a shared cell that stays valid regardless of object lifetime.
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_map_read(
    device: AlcoHandle,
    buffer: AlcoHandle,
    offset: u64,
    size: u64,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let buffer_id = match ctx.buffers().with(buffer, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                let completion = Arc::new(MapCompletion {
                    state: AtomicU32::new(0),
                    error: Mutex::new(CString::new("").unwrap()),
                });
                let callback_cell = completion.clone();
                let callback = Box::new(move |result: wgc::resource::BufferAccessResult| match result {
                    Ok(()) => callback_cell.state.store(1, Ordering::Release),
                    Err(err) => {
                        callback_cell.state.store(2, Ordering::Release);
                        *callback_cell.error.lock().unwrap() = CString::new(err.to_string()).unwrap();
                    }
                }) as wgc::resource::BufferMapCallback;
                let operation = wgc::resource::BufferMapOperation {
                    host: wgc::device::HostMap::Read,
                    callback: Some(callback),
                };
                match ctx.global.buffer_map_async(buffer_id, offset, Some(size), operation) {
                    Ok(_) => {
                        ctx.buffers()
                            .with(buffer, |obj| obj.map_completion = Some(completion))
                            .ok();
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

/// ABI: polls a pending map. `NOT_READY` = still mapping, `OK` = mapped
/// (range available via `alco_buffer_get_mapped_range`), `VALIDATION` = failed.
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_map_poll(
    device: AlcoHandle,
    buffer: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let state = ctx.buffers().with(buffer, |obj| {
                    obj.map_completion
                        .as_ref()
                        .map(|c| c.state.load(Ordering::Acquire))
                });
                match state {
                    Ok(Some(0)) | Ok(None) | Ok(Some(3..)) => AlcoStatus::NOT_READY,
                    Ok(Some(1)) => AlcoStatus::OK,
                    Ok(Some(2)) => {
                        let message = ctx
                            .buffers()
                            .with(buffer, |obj| {
                                obj.map_completion
                                    .as_ref()
                                    .unwrap()
                                    .error
                                    .lock()
                                    .unwrap()
                                    .clone()
                            })
                            .unwrap_or_default();
                        set_error(AlcoStatus::VALIDATION, message.to_string_lossy().into_owned());
                        AlcoStatus::VALIDATION
                    }
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
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

/// ABI: returns the mapped read range pointer.
///
/// # Safety
/// `out` must be a valid `*const u8` slot; the range is valid until unmap.
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_get_mapped_range(
    device: AlcoHandle,
    buffer: AlcoHandle,
    offset: u64,
    size: u64,
    out: *mut *const u8,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                match ctx.buffers().with(buffer, |obj| obj.id) {
                    Ok(id) => {
                        let range = ctx
                            .global
                            .buffer_get_mapped_range(id, offset, Some(size));
                        match range {
                            Ok((ptr, len)) if len >= size => {
                                *out = ptr.as_ptr();
                                AlcoStatus::OK
                            }
                            Ok(_) => {
                                set_error(AlcoStatus::VALIDATION, "mapped range smaller than requested");
                                AlcoStatus::VALIDATION
                            }
                            Err(e) => {
                                set_error_from(AlcoStatus::VALIDATION, &e);
                                AlcoStatus::VALIDATION
                            }
                        }
                    }
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
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

/// ABI: unmaps the buffer and resets the map state machine.
#[no_mangle]
pub unsafe extern "C" fn alco_buffer_unmap(device: AlcoHandle, buffer: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                match ctx.buffers().with(buffer, |obj| obj.id) {
                    Ok(id) => {
                        if let Err(e) = ctx.global.buffer_unmap(id) {
                            set_error_from(AlcoStatus::VALIDATION, &e);
                            return AlcoStatus::VALIDATION;
                        }
                        ctx.buffers()
                            .with(buffer, |obj| obj.map_completion = None)
                            .ok();
                        AlcoStatus::OK
                    }
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
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

// ---------------------------------------------------------------------------
// Texture / view
// ---------------------------------------------------------------------------

/// ABI: creates a texture.
#[no_mangle]
pub unsafe extern "C" fn alco_texture_create(
    device: AlcoHandle,
    desc: *const AlcoTextureDesc,
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
            .with(device, move |ctx| {
                let format = match pixel_format(desc.format) {
                    Ok(f) => f,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let dimension = match texture_dimension(desc.dimension) {
                    Ok(d) => d,
                    Err(s) => return (s, AlcoHandle::NULL),
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
                let (id, err) = ctx.global.device_create_texture(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.texture_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.textures().insert(TextureObj {
                    id,
                    width: desc.width,
                    height: desc.height,
                    depth_or_array_layers: desc.depth_or_array_layers,
                    mip_level_count: desc.mip_level_count,
                    format: desc.format,
                    is_surface_texture: false,
                });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a texture; surface textures are rejected (release-only).
#[no_mangle]
pub unsafe extern "C" fn alco_texture_destroy(
    device: AlcoHandle,
    texture: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.textures().remove(texture) {
                Ok(obj) if obj.is_surface_texture => {
                    set_error(
                        AlcoStatus::INVALID_ARGUMENT,
                        "surface textures must be released, not destroyed",
                    );
                    // Re-insert so the surface release path can still find it.
                    let handle = ctx.textures().insert(obj);
                    let _ = handle;
                    AlcoStatus::INVALID_ARGUMENT
                }
                Ok(obj) => {
                    ctx.global.texture_destroy(obj.id);
                    ctx.global.texture_drop(obj.id);
                    AlcoStatus::OK
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

/// ABI: fills texture info (used for acquired surface textures).
#[no_mangle]
pub unsafe extern "C" fn alco_texture_get_info(
    device: AlcoHandle,
    texture: AlcoHandle,
    out: *mut AlcoTextureInfo,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                match ctx.textures().with(texture, |obj| {
                    (*out).width = obj.width;
                    (*out).height = obj.height;
                    (*out).depth_or_array_layers = obj.depth_or_array_layers;
                    (*out).mip_level_count = obj.mip_level_count;
                    (*out).format = obj.format;
                }) {
                    Ok(()) => AlcoStatus::OK,
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

/// ABI: creates a texture view. Pass a null `desc` for the default view
/// (used for per-frame surface textures).
#[no_mangle]
pub unsafe extern "C" fn alco_texture_create_view(
    device: AlcoHandle,
    texture: AlcoHandle,
    desc: *const AlcoTextureViewDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let texture_id = match ctx.textures().with(texture, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid texture handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
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
                let (id, err) = ctx
                    .global
                    .texture_create_view(texture_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.texture_view_drop(id);
                    return AlcoStatus::VALIDATION;
                }
                *out = ctx.views().insert(TextureViewObj { id });
                AlcoStatus::OK
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a texture view.
#[no_mangle]
pub unsafe extern "C" fn alco_texture_view_destroy(
    device: AlcoHandle,
    view: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.views().remove(view) {
                Ok(obj) => {
                    ctx.global.texture_view_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid texture view handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

fn plain_or_none(v: u32) -> Option<u32> {
    if v == 0 || v == ALCO_NONE {
        None
    } else {
        Some(v)
    }
}

// ---------------------------------------------------------------------------
// Sampler
// ---------------------------------------------------------------------------

/// ABI: creates a sampler.
#[no_mangle]
pub unsafe extern "C" fn alco_sampler_create(
    device: AlcoHandle,
    desc: *const AlcoSamplerDesc,
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
            .with(device, move |ctx| {
                let min_filter = match filter_mode(desc.min_filter) {
                    Ok(f) => f,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let mag_filter = match filter_mode(desc.mag_filter) {
                    Ok(f) => f,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let mipmap_filter = match mipmap_filter_mode(desc.mipmap_filter) {
                    Ok(f) => f,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let address_mode = |v: u32| address_mode(v).ok();
                let address_u = match address_mode(desc.address_u) {
                    Some(f) => f,
                    None => return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL),
                };
                let address_v = match address_mode(desc.address_v) {
                    Some(f) => f,
                    None => return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL),
                };
                let address_w = match address_mode(desc.address_w) {
                    Some(f) => f,
                    None => return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL),
                };
                let compare = match optional_compare_function(desc.compare) {
                    Ok(c) => c,
                    Err(_) => return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL),
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
                let (id, err) = ctx.global.device_create_sampler(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.sampler_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.samplers().insert(SamplerObj { id });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a sampler.
#[no_mangle]
pub unsafe extern "C" fn alco_sampler_destroy(
    device: AlcoHandle,
    sampler: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.samplers().remove(sampler) {
                Ok(obj) => {
                    ctx.global.sampler_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid sampler handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

// ---------------------------------------------------------------------------
// Shader module
// ---------------------------------------------------------------------------

/// ABI: creates a shader module. DXIL/MSL/MetalLib/SPIR-V go through the
/// passthrough path; WGSL goes through Naga.
#[no_mangle]
pub unsafe extern "C" fn alco_shader_module_create(
    device: AlcoHandle,
    desc: *const AlcoShaderModuleDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && !d.data.is_null() && d.size > 0 => d,
            _ => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null descriptor, out pointer or empty source");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        DEVICES
            .with(device, move |ctx| {
                let data = std::slice::from_raw_parts(desc.data, desc.size as usize);
                let entry_point = borrow_label(desc.entry_point);
                let module_label = label(desc.name);
                let workgroup = [desc.workgroup_x, desc.workgroup_y, desc.workgroup_z];

                let (id, err): (wgc::id::ShaderModuleId, Option<Box<dyn std::error::Error + Send + Sync>>) =
                    match desc.language {
                        shader_language::SPIRV => {
                            let mut words = Vec::with_capacity(data.len() / 4);
                            for chunk in data.chunks_exact(4) {
                                words.push(u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]));
                            }
                            if ctx.caps & caps::PASSTHROUGH_SHADERS != 0 {
                                let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                                    p.spirv = Some(std::borrow::Cow::Owned(words.clone()));
                                });
                                let (id, err) = ctx
                                    .global
                                    .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                                (id, err.map(|e| Box::new(e) as _))
                            } else {
                                let source = wgc::pipeline::ShaderModuleSource::SpirV(
                                    std::borrow::Cow::Owned(words.clone()),
                                    wgc::naga::front::spv::Options::default(),
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
                                p.dxil = Some(std::borrow::Cow::Owned(data.to_vec()));
                            });
                            let (id, err) = ctx
                                .global
                                .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                            (id, err.map(|e| Box::new(e) as _))
                        }
                        shader_language::MSL => {
                            match require_passthrough(ctx) {
                                Ok(()) => {}
                                Err(s) => return s,
                            }
                            let source = String::from_utf8_lossy(data).into_owned();
                            let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                                p.msl = Some(std::borrow::Cow::Owned(source));
                            });
                            let (id, err) = ctx
                                .global
                                .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                            (id, err.map(|e| Box::new(e) as _))
                        }
                        shader_language::METALLIB => {
                            match require_passthrough(ctx) {
                                Ok(()) => {}
                                Err(s) => return s,
                            }
                            let pdesc = passthrough_desc(module_label.clone(), &entry_point, workgroup, |p| {
                                p.metallib = Some(std::borrow::Cow::Owned(data.to_vec()));
                            });
                            let (id, err) = ctx
                                .global
                                .device_create_shader_module_passthrough(ctx.device_id, &pdesc, None);
                            (id, err.map(|e| Box::new(e) as _))
                        }
                        shader_language::WGSL => {
                            let source = String::from_utf8_lossy(data).into_owned();
                            let sdesc = wgc::pipeline::ShaderModuleDescriptor {
                                label: module_label.clone(),
                                runtime_checks: wgt::ShaderRuntimeChecks::checked(),
                            };
                            let (id, err) = ctx.global.device_create_shader_module(
                                ctx.device_id,
                                &sdesc,
                                wgc::pipeline::ShaderModuleSource::Wgsl(std::borrow::Cow::Owned(source)),
                                None,
                            );
                            (id, err.map(|e| Box::new(e) as _))
                        }
                        other => {
                            set_error(AlcoStatus::INVALID_ARGUMENT, format!("unsupported shader language {other}"));
                            return AlcoStatus::INVALID_ARGUMENT;
                        }
                    };

                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.shader_module_drop(id);
                    return AlcoStatus::VALIDATION;
                }
                *out = ctx.shader_modules().insert(ShaderModuleObj { id });
                AlcoStatus::OK
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

fn require_passthrough(ctx: &DeviceCtx) -> Result<(), AlcoStatus> {
    if ctx.caps & caps::PASSTHROUGH_SHADERS == 0 {
        set_error(
            AlcoStatus::UNSUPPORTED,
            "this device does not support passthrough shaders",
        );
        return Err(AlcoStatus::UNSUPPORTED);
    }
    Ok(())
}

fn passthrough_desc(
    module_label: Option<std::borrow::Cow<'static, str>>,
    entry_point: &str,
    workgroup: [u32; 3],
    fill: impl FnOnce(
        &mut wgt::CreateShaderModuleDescriptorPassthrough<'static, Option<std::borrow::Cow<'static, str>>>,
    ),
) -> wgt::CreateShaderModuleDescriptorPassthrough<'static, Option<std::borrow::Cow<'static, str>>> {
    let mut passthrough = wgt::CreateShaderModuleDescriptorPassthrough {
        label: module_label,
        entry_points: std::borrow::Cow::Owned(vec![wgt::PassthroughShaderEntryPoint {
            name: std::borrow::Cow::Owned(entry_point.to_string()),
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
#[no_mangle]
pub unsafe extern "C" fn alco_shader_module_destroy(
    device: AlcoHandle,
    module: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.shader_modules().remove(module) {
                Ok(obj) => {
                    ctx.global.shader_module_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid shader module handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

// ---------------------------------------------------------------------------
// Bind group layout (C# GPUBindGroup) and bind group (C# GPUResourceGroup)
// ---------------------------------------------------------------------------

fn bind_group_layout_entry(
    entry: &AlcoBindGroupLayoutEntry,
) -> Result<wgt::BindGroupLayoutEntry, AlcoStatus> {
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
            set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid binding type {other}"));
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
    };
    Ok(wgt::BindGroupLayoutEntry {
        binding: entry.binding,
        visibility: shader_stages(entry.visibility),
        ty,
        count: None,
    })
}

/// ABI: creates a bind group layout (the C# GPUBindGroup object).
#[no_mangle]
pub unsafe extern "C" fn alco_bind_group_layout_create(
    device: AlcoHandle,
    desc: *const AlcoBindGroupLayoutDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && !d.entries.is_null() && d.entry_count > 0 => d,
            _ => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null descriptor, out pointer or empty entries");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        DEVICES
            .with(device, move |ctx| {
                let entries = std::slice::from_raw_parts(desc.entries, desc.entry_count as usize);
                let mut wentries = Vec::with_capacity(entries.len());
                for entry in entries {
                    match bind_group_layout_entry(entry) {
                        Ok(e) => wentries.push(e),
                        Err(s) => return (s, AlcoHandle::NULL),
                    }
                }
                let wdesc = wgc::binding_model::BindGroupLayoutDescriptor {
                    label: label(desc.name),
                    entries: std::borrow::Cow::Owned(wentries),
                };
                let (id, err) =
                    ctx.global
                        .device_create_bind_group_layout(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.bind_group_layout_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.bind_group_layouts().insert(BindGroupLayoutObj { id });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a bind group layout.
#[no_mangle]
pub unsafe extern "C" fn alco_bind_group_layout_destroy(
    device: AlcoHandle,
    layout: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.bind_group_layouts().remove(layout) {
                Ok(obj) => {
                    ctx.global.bind_group_layout_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group layout handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: creates a bind group instance (the C# GPUResourceGroup object).
#[no_mangle]
pub unsafe extern "C" fn alco_bind_group_create(
    device: AlcoHandle,
    desc: *const AlcoBindGroupDesc,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let desc = match desc.as_ref() {
            Some(d) if !out.is_null() && !d.entries.is_null() && d.entry_count > 0 => d,
            _ => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null descriptor, out pointer or empty entries");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        DEVICES
            .with(device, move |ctx| {
                let layout_id = match ctx.bind_group_layouts().with(desc.layout, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group layout handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };
                let entries = std::slice::from_raw_parts(desc.entries, desc.entry_count as usize);
                let mut wentries = Vec::with_capacity(entries.len());
                for entry in entries {
                    let resource = match entry.kind {
                        0 => {
                            let id = match ctx.buffers().with(entry.resource, |obj| obj.id) {
                                Ok(id) => id,
                                Err(_) => {
                                    set_error(AlcoStatus::INVALID_HANDLE, format!("invalid buffer handle in binding {}", entry.binding));
                                    return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                                }
                            };
                            wgc::binding_model::BindingResource::Buffer(wgc::binding_model::BufferBinding {
                                buffer: id,
                                offset: entry.offset,
                                size: if entry.size == 0 { None } else { Some(entry.size) },
                            })
                        }
                        1 => {
                            let id = match ctx.views().with(entry.resource, |obj| obj.id) {
                                Ok(id) => id,
                                Err(_) => {
                                    set_error(AlcoStatus::INVALID_HANDLE, format!("invalid texture view handle in binding {}", entry.binding));
                                    return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                                }
                            };
                            wgc::binding_model::BindingResource::TextureView(id)
                        }
                        2 => {
                            let id = match ctx.samplers().with(entry.resource, |obj| obj.id) {
                                Ok(id) => id,
                                Err(_) => {
                                    set_error(AlcoStatus::INVALID_HANDLE, format!("invalid sampler handle in binding {}", entry.binding));
                                    return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                                }
                            };
                            wgc::binding_model::BindingResource::Sampler(id)
                        }
                        other => {
                            set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid resource kind {other} in binding {}", entry.binding));
                            return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
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
                    entries: std::borrow::Cow::Owned(wentries),
                };
                let (id, err) = ctx.global.device_create_bind_group(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.bind_group_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.bind_groups().insert(BindGroupObj { id });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a bind group.
#[no_mangle]
pub unsafe extern "C" fn alco_bind_group_destroy(
    device: AlcoHandle,
    group: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.bind_groups().remove(group) {
                Ok(obj) => {
                    ctx.global.bind_group_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

// ---------------------------------------------------------------------------
// Query set
// ---------------------------------------------------------------------------

/// ABI: creates a timestamp query set.
#[no_mangle]
pub unsafe extern "C" fn alco_query_set_create(
    device: AlcoHandle,
    count: u32,
    name: *const c_char,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() || count == 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer or zero count");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, move |ctx| {
                let wdesc = wgt::QuerySetDescriptor {
                    label: label(name),
                    ty: wgt::QueryType::Timestamp,
                    count,
                };
                let (id, err) = ctx.global.device_create_query_set(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.query_set_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.query_sets().insert(QuerySetObj { id });
                (AlcoStatus::OK, handle)
            })
            .map(|(status, handle)| {
                if status.is_ok() {
                    *out = handle;
                }
                status
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a query set (drop only — destroy+drop double-removes in wgpu).
#[no_mangle]
pub unsafe extern "C" fn alco_query_set_destroy(
    device: AlcoHandle,
    query_set: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.query_sets().remove(query_set) {
                Ok(obj) => {
                    ctx.global.query_set_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}
