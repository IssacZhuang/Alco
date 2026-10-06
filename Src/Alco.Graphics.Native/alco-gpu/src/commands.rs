//! Command encoding: encoders, render/compute passes, copies, query
//! resolution, queue writes/submission and render bundles.
//!
//! ABI2 handles point directly to individually owned wrappers. Open passes use
//! wgpu-core's by-value recording API, not its pass-ID registry. End, Finish and
//! Submit consume their wrappers even when core validation fails; Release drops
//! an unfinished pass without ending it. Each wrapper keeps its context alive.
//!
//! Non-null handles must refer to live objects of their declared type. The caller
//! orders destruction after all access, exclusively owns mutable recording state,
//! and supplies resources from the same context. Freed handles must never be used.
//! Independent objects may be used concurrently on the same device.

use crate::abi::*;
use crate::convert::*;
use crate::device::DeviceCtx;
use crate::entry::{set_error, set_error_from};
use crate::handle::Handle;
use crate::objects::{borrow_label, label, plain_or_none};
use std::borrow::Cow;
use std::ffi::c_char;
use std::sync::Arc;
use wgpu_core as wgc;
use wgpu_types as wgt;

/// Owns a core command encoder and retains its device context through cleanup.
pub struct EncoderObj {
    /// Core command encoder identity.
    pub id: wgc::id::CommandEncoderId,
    /// Context that owns the encoder's core registration.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for EncoderObj {
    fn drop(&mut self) {
        self.ctx.global.command_encoder_drop(self.id);
    }
}

/// Owns a finished command buffer and retains its device context through cleanup.
pub struct CommandBufferObj {
    /// Core command buffer identity.
    pub id: wgc::id::CommandBufferId,
    /// Context that owns the command buffer's core registration.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for CommandBufferObj {
    fn drop(&mut self) {
        self.ctx.global.command_buffer_drop(self.id);
    }
}

/// Owns mutable render-pass recording state until End or Release consumes it.
pub struct RenderPassObj {
    /// Core recording state, taken before invoking End on a consumed wrapper.
    pub pass: Option<wgc::command::RenderPass>,
    /// Context retained until the recording state has been dropped.
    pub ctx: Arc<DeviceCtx>,
}

/// Owns mutable compute-pass recording state until End or Release consumes it.
pub struct ComputePassObj {
    /// Core recording state, taken before invoking End on a consumed wrapper.
    pub pass: Option<wgc::command::ComputePass>,
    /// Context retained until the recording state has been dropped.
    pub ctx: Arc<DeviceCtx>,
}

/// Owns an unfinished render bundle encoder and its device context.
pub struct BundleEncoderObj {
    /// Core recording state, taken before finishing a consumed wrapper.
    pub encoder: Option<Box<wgc::command::RenderBundleEncoder>>,
    /// Context retained until the recording state has been dropped.
    pub ctx: Arc<DeviceCtx>,
}

/// Owns a finished render bundle and retains its context through core cleanup.
pub struct RenderBundleObj {
    /// Core render bundle identity.
    pub id: wgc::id::RenderBundleId,
    /// Context that owns the bundle's core registration.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for RenderBundleObj {
    fn drop(&mut self) {
        self.ctx.global.render_bundle_drop(self.id);
    }
}

fn command_status(body: impl FnOnce() -> Result<(), Status>) -> Status {
    match body() {
        Ok(()) => Status::OK,
        Err(status) => status,
    }
}

unsafe fn borrow<'a, T>(handle: Handle<T>, kind: &str) -> Result<&'a T, Status> {
    handle.get().map_err(|status| recording_error(status, kind))
}

unsafe fn borrow_mut<'a, T>(handle: Handle<T>, kind: &str) -> Result<&'a mut T, Status> {
    handle
        .get_mut()
        .map_err(|status| recording_error(status, kind))
}

unsafe fn consume<T>(handle: Handle<T>, kind: &str) -> Result<Box<T>, Status> {
    handle
        .take()
        .map_err(|status| recording_error(status, kind))
}

// ---------------------------------------------------------------------------
// ABI structs
// ---------------------------------------------------------------------------

/// One color attachment of a render pass.
#[repr(C)]
pub struct ColorAttachment {
    /// Live color attachment view.
    pub view: TextureViewHandle,
    /// Resolve target view, `TextureViewHandle::NULL` when unused.
    pub resolve_view: TextureViewHandle,
    /// C# `AttachmentLoadOp`: 0 Load, 1 Clear.
    pub load_op: u32,
    /// C# `AttachmentStoreOp`: 0 Store, 1 Discard.
    pub store_op: u32,
    /// RGBA clear value used by the Clear load operation.
    pub clear_color: [f32; 4],
}

/// Depth-stencil attachment of a render pass. A load/store op value of
/// `NONE` marks the channel read-only (wgpu `PassChannel` with no ops).
#[repr(C)]
pub struct DepthStencilAttachment {
    /// Live depth-stencil attachment view.
    pub view: TextureViewHandle,
    /// Depth load operation, or NONE for a read-only channel.
    pub depth_load_op: u32,
    /// Depth store operation, or NONE for a read-only channel.
    pub depth_store_op: u32,
    /// Depth clear value used by the Clear load operation.
    pub depth_clear: f32,
    /// Stencil load operation, or NONE for a read-only channel.
    pub stencil_load_op: u32,
    /// Stencil store operation, or NONE for a read-only channel.
    pub stencil_store_op: u32,
    /// Stencil clear value used by the Clear load operation.
    pub stencil_clear: u32,
}

/// Timestamp query writes performed at pass boundaries.
#[repr(C)]
pub struct TimestampWrites {
    /// Live timestamp query set.
    pub query_set: QuerySetHandle,
    /// `NONE` when not written.
    pub beginning_index: u32,
    /// End-of-pass query index, or NONE when not written.
    pub end_index: u32,
}

/// Attachment and timestamp descriptors for beginning a render pass.
#[repr(C)]
pub struct RenderPassDesc {
    /// Readable color attachment array, nullable when its count is zero.
    pub color_attachments: *const ColorAttachment,
    /// Number of color attachment descriptors.
    pub color_attachment_count: u32,
    /// Null when the pass has no depth-stencil attachment.
    pub depth_stencil: *const DepthStencilAttachment,
    /// Null when the pass writes no timestamps.
    pub timestamp_writes: *const TimestampWrites,
}

/// Full-mip source layout for texture copies/writes (computed C#-side, which
/// owns the row-pitch rules per format).
#[repr(C)]
pub struct CopyLayout {
    /// Byte offset of the first copied texel in the buffer.
    pub offset: u64,
    /// `NONE` when not required (single row).
    pub bytes_per_row: u32,
    /// Rows per depth slice or array layer, or NONE when not required.
    pub rows_per_image: u32,
}

/// Texture copy origin in texels.
#[repr(C)]
pub struct Origin3D {
    /// Horizontal texel offset.
    pub x: u32,
    /// Vertical texel offset.
    pub y: u32,
    /// Depth slice or array layer offset.
    pub z: u32,
}

/// Texture copy extent in texels.
#[repr(C)]
pub struct Extent3D {
    /// Number of texels along the horizontal axis.
    pub width: u32,
    /// Number of texels along the vertical axis.
    pub height: u32,
    /// Number of depth slices or array layers.
    pub depth_or_array_layers: u32,
}

// ---------------------------------------------------------------------------
// Encoder lifecycle
// ---------------------------------------------------------------------------

/// ABI: creates a command encoder from the device's context.
///
/// # Safety
/// `device` must be live, `out` must be writable, and a non-null `name` must be
/// NUL-terminated. Device destruction must not overlap this call.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_encoder(
    device: DeviceHandle,
    name: *const c_char,
    out: *mut EncoderHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = EncoderHandle::NULL;
            let ctx = &borrow(device, "device")?.ctx;
            let wdesc = wgt::CommandEncoderDescriptor { label: label(name) };
            let (id, err) = ctx
                .global
                .device_create_command_encoder(ctx.device_id, &wdesc, None);
            let obj = EncoderObj {
                id,
                ctx: ctx.clone(),
            };
            if let Some(e) = err {
                set_error_from(Status::OUT_OF_MEMORY, &e);
                return Err(Status::OUT_OF_MEMORY);
            }
            *out = Handle::new(obj);
            Ok(())
        })
    })
}

/// ABI: finishes an encoder, consuming it even when core validation fails.
/// A null output pointer is rejected before the encoder is consumed.
///
/// # Safety
/// `encoder` must be live and caller-exclusive; `out` must be writable.
/// All pass recording and encoder access must finish before this call.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_finish(
    encoder: EncoderHandle,
    out: *mut CommandBufferHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = CommandBufferHandle::NULL;
            let obj = consume(encoder, "encoder")?;
            let wdesc = wgt::CommandBufferDescriptor { label: None };
            let (id, err) = obj.ctx.global.command_encoder_finish(obj.id, &wdesc, None);
            let command = CommandBufferObj {
                id,
                ctx: obj.ctx.clone(),
            };
            drop(obj);
            if let Some((_, e)) = err {
                set_error_from(Status::VALIDATION, &e);
                return Err(Status::VALIDATION);
            }
            *out = Handle::new(command);
            Ok(())
        })
    })
}

/// ABI: consumes an encoder that will never be finished.
///
/// # Safety
/// `encoder` must be live and no access may overlap its destruction.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_destroy(encoder: EncoderHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(encoder, "encoder")?);
            Ok(())
        })
    })
}

/// ABI: consumes a command buffer that was never submitted.
///
/// # Safety
/// `command_buffer` must be live and no access may overlap its destruction.
#[no_mangle]
pub unsafe extern "C-unwind" fn command_buffer_destroy(
    command_buffer: CommandBufferHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(command_buffer, "command buffer")?);
            Ok(())
        })
    })
}

// ---------------------------------------------------------------------------
// Render pass
// ---------------------------------------------------------------------------

/// ABI: begins a render pass. On failure the output handle is null.
///
/// # Safety
/// `encoder` must be live and caller-exclusive. `desc`, its non-null attachment
/// pointers, and `out` must be valid for their accesses. All attachment handles
/// must be live and belong to the encoder's context.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_begin_render_pass(
    encoder: EncoderHandle,
    desc: *const RenderPassDesc,
    out: *mut RenderPassHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = RenderPassHandle::NULL;
            let desc = desc.as_ref().ok_or_else(|| {
                set_error(Status::INVALID_ARGUMENT, "null descriptor");
                Status::INVALID_ARGUMENT
            })?;
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let count = desc.color_attachment_count as usize;
            if count > 0 && desc.color_attachments.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null color attachment array");
                return Err(Status::INVALID_ARGUMENT);
            }
            let raw = if count == 0 {
                &[][..]
            } else {
                std::slice::from_raw_parts(desc.color_attachments, count)
            };
            // Normal MRT descriptors need no temporary allocation. Larger arrays
            // remain complete so core, rather than truncation, validates limits.
            let mut inline = [const { None }; 8];
            let mut overflow = Vec::new();
            let attachments = if count <= inline.len() {
                &mut inline[..count]
            } else {
                overflow.resize_with(count, || None);
                overflow.as_mut_slice()
            };
            for index in 0..count {
                let attachment = &raw[index];
                let view = borrow(attachment.view, "texture view in color attachment")?;
                debug_assert!(Arc::ptr_eq(ctx, &view.ctx));
                let resolve_target = if attachment.resolve_view.is_null() {
                    None
                } else {
                    let resolve =
                        borrow(attachment.resolve_view, "resolve view in color attachment")?;
                    debug_assert!(Arc::ptr_eq(ctx, &resolve.ctx));
                    Some(resolve.id)
                };
                let load_op = match attachment.load_op {
                    0 => wgt::LoadOp::Load,
                    1 => wgt::LoadOp::Clear(wgt::Color {
                        r: attachment.clear_color[0] as f64,
                        g: attachment.clear_color[1] as f64,
                        b: attachment.clear_color[2] as f64,
                        a: attachment.clear_color[3] as f64,
                    }),
                    other => {
                        set_error(
                            Status::INVALID_ARGUMENT,
                            format!("invalid color load op {other}"),
                        );
                        return Err(Status::INVALID_ARGUMENT);
                    }
                };
                attachments[index] = Some(wgc::command::RenderPassColorAttachment {
                    view: view.id,
                    depth_slice: None,
                    resolve_target,
                    load_op,
                    store_op: store_op(attachment.store_op)?,
                });
            }
            let depth_stencil_attachment = match desc.depth_stencil.as_ref() {
                Some(d) => {
                    let view = borrow(d.view, "texture view in depth attachment")?;
                    debug_assert!(Arc::ptr_eq(ctx, &view.ctx));
                    Some(wgc::command::RenderPassDepthStencilAttachment {
                        view: view.id,
                        depth: pass_channel(
                            d.depth_load_op,
                            d.depth_store_op,
                            d.depth_clear,
                            "depth",
                        )?,
                        stencil: pass_channel_u32(
                            d.stencil_load_op,
                            d.stencil_store_op,
                            d.stencil_clear,
                            "stencil",
                        )?,
                    })
                }
                None => None,
            };
            let wdesc = wgc::command::RenderPassDescriptor {
                label: None,
                color_attachments: std::borrow::Cow::Borrowed(attachments),
                depth_stencil_attachment,
                timestamp_writes: pass_timestamps(ctx, desc.timestamp_writes)?,
                occlusion_query_set: None,
                multiview_mask: None,
            };
            let (pass, err) = ctx
                .global
                .command_encoder_begin_render_pass(encoder.id, &wdesc);
            if let Some(e) = err {
                set_error_from(Status::VALIDATION, &e);
                return Err(Status::VALIDATION);
            }
            *out = Handle::new(RenderPassObj {
                pass: Some(pass),
                ctx: ctx.clone(),
            });
            Ok(())
        })
    })
}

unsafe fn pass_timestamps(
    ctx: &Arc<DeviceCtx>,
    writes: *const TimestampWrites,
) -> Result<Option<wgc::command::PassTimestampWrites>, Status> {
    let Some(writes) = writes.as_ref() else {
        return Ok(None);
    };
    let query_set = borrow(writes.query_set, "query set in timestamp writes")?;
    debug_assert!(Arc::ptr_eq(ctx, &query_set.ctx));
    Ok(Some(wgc::command::PassTimestampWrites {
        query_set: query_set.id,
        beginning_of_pass_write_index: optional_index(writes.beginning_index),
        end_of_pass_write_index: optional_index(writes.end_index),
    }))
}

fn optional_index(v: u32) -> Option<u32> {
    if v == NONE {
        None
    } else {
        Some(v)
    }
}

fn pass_channel(
    load_op: u32,
    store: u32,
    clear: f32,
    what: &str,
) -> Result<wgc::command::PassChannel<Option<f32>>, Status> {
    let read_only = load_op == NONE && store == NONE;
    let load = if read_only {
        None
    } else {
        Some(match load_op {
            0 => wgt::LoadOp::Load,
            1 => wgt::LoadOp::Clear(Some(clear)),
            other => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    format!("invalid {what} load op {other}"),
                );
                return Err(Status::INVALID_ARGUMENT);
            }
        })
    };
    let store = if read_only {
        None
    } else {
        Some(store_op(store)?)
    };
    Ok(wgc::command::PassChannel {
        load_op: load,
        store_op: store,
        read_only,
    })
}

fn pass_channel_u32(
    load_op: u32,
    store: u32,
    clear: u32,
    what: &str,
) -> Result<wgc::command::PassChannel<Option<u32>>, Status> {
    let read_only = load_op == NONE && store == NONE;
    let load = if read_only {
        None
    } else {
        Some(match load_op {
            0 => wgt::LoadOp::Load,
            1 => wgt::LoadOp::Clear(Some(clear)),
            other => {
                set_error(
                    Status::INVALID_ARGUMENT,
                    format!("invalid {what} load op {other}"),
                );
                return Err(Status::INVALID_ARGUMENT);
            }
        })
    };
    let store = if read_only {
        None
    } else {
        Some(store_op(store)?)
    };
    Ok(wgc::command::PassChannel {
        load_op: load,
        store_op: store,
        read_only,
    })
}

/// ABI: ends a render pass, consuming its wrapper even on core failure.
/// End reports encoder-state errors immediately; recorded pass validation errors
/// remain on the parent encoder and are reported when it is finished.
///
/// # Safety
/// `pass` must be live and caller-exclusive. All recording must finish first.
#[no_mangle]
pub unsafe extern "C-unwind" fn render_pass_end(pass: RenderPassHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let mut obj = consume(pass, "render pass")?;
            let mut pass = obj.pass.take().expect("live wrapper must contain a pass");
            record_result(obj.ctx.global.render_pass_end(&mut pass))
        })
    })
}

/// ABI: abandons a render pass without End, consuming its wrapper.
/// A valid live pass always releases successfully, without core validation.
///
/// # Safety
/// `pass` must be live and caller-exclusive. No recording may overlap release.
#[no_mangle]
pub unsafe extern "C-unwind" fn render_pass_release(pass: RenderPassHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(pass, "render pass")?);
            Ok(())
        })
    })
}

macro_rules! render_pass_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        ///
        /// # Safety
        /// The pass must be live and caller-exclusive; referenced handles must be
        /// live and from the same context. Pointer arguments must be valid for all
        /// accesses. End and Release must be ordered after this call returns.
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(pass: RenderPassHandle, $($arg: $ty),*) -> Status {
            crate::entry::guard(|| command_status(|| {
                let obj = borrow_mut(pass, "render pass")?;
                type Body = fn(&Arc<DeviceCtx>, &mut wgc::command::RenderPass, $($ty),*) -> Result<(), Status>;
                let body: Body = $body;
                body(&obj.ctx, obj.pass.as_mut().expect("live wrapper must contain a pass") $(, $arg)*)
            }))
        }
    };
}

fn recording_error(status: Status, kind: &str) -> Status {
    set_error(status, format!("invalid {kind} handle"));
    status
}

/// Rejects null debug labels; `borrow_label` would silently record an empty one.
unsafe fn debug_label<'a>(ptr: *const c_char) -> Result<Cow<'a, str>, Status> {
    if ptr.is_null() {
        set_error(Status::INVALID_ARGUMENT, "null label");
        return Err(Status::INVALID_ARGUMENT);
    }
    Ok(borrow_label(ptr))
}

fn record_result(result: Result<(), impl std::error::Error>) -> Result<(), Status> {
    match result {
        Ok(()) => Ok(()),
        Err(e) => {
            set_error_from(Status::VALIDATION, &e);
            Err(Status::VALIDATION)
        }
    }
}

render_pass_fn!(
    /// ABI: sets the render pipeline.
    render_pass_set_pipeline(pipeline: GraphicsPipelineHandle) |ctx, pass, pipeline| {
        let pipeline = borrow(pipeline, "graphics pipeline")?;
        debug_assert!(Arc::ptr_eq(ctx, &pipeline.ctx));
        record_result(ctx.global.render_pass_set_pipeline(pass, pipeline.id))
    }
);

render_pass_fn!(
    /// ABI: binds a resource group (bind group) at `slot`.
    render_pass_set_bind_group(slot: u32, group: BindGroupHandle) |ctx, pass, slot, group| {
        let id = lookup_bind_group(ctx, group);
        match id {
            Ok(id) => record_result(ctx.global.render_pass_set_bind_group(pass, slot, Some(id), &[])),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid bind group handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: binds a vertex buffer slot.
    render_pass_set_vertex_buffer(slot: u32, buffer: BufferHandle, offset: u64, size: u64) |ctx, pass, slot, buffer, offset, size| {
        let id = lookup_buffer(ctx, buffer);
        match id {
            Ok(id) => record_result(ctx.global.render_pass_set_vertex_buffer(
                pass,
                slot,
                Some(id),
                offset,
                nonzero_size(size),
            )),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: binds the index buffer.
    render_pass_set_index_buffer(buffer: BufferHandle, format: u32, offset: u64, size: u64) |ctx, pass, buffer, format, offset, size| {
        let id = lookup_buffer(ctx, buffer);
        let wformat = index_format(format);
        match (id, wformat) {
            (Ok(id), Ok(format)) => record_result(ctx.global.render_pass_set_index_buffer(
                pass,
                id,
                format,
                offset,
                nonzero_size(size),
            )),
            (Err(_), _) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
            (_, Err(s)) => Err(s),
        }
    }
);

render_pass_fn!(
    /// ABI: sets the scissor rectangle.
    render_pass_set_scissor_rect(x: u32, y: u32, width: u32, height: u32) |ctx, pass, x, y, width, height| {
        record_result(ctx.global.render_pass_set_scissor_rect(pass, x, y, width, height))
    }
);

render_pass_fn!(
    /// ABI: sets the viewport rectangle and depth range.
    render_pass_set_viewport(x: f32, y: f32, width: f32, height: f32, depth_min: f32, depth_max: f32) |ctx, pass, x, y, width, height, depth_min, depth_max| {
        record_result(ctx.global.render_pass_set_viewport(pass, x, y, width, height, depth_min, depth_max))
    }
);

render_pass_fn!(
    /// ABI: sets the dynamic blend constant.
    render_pass_set_blend_constant(r: f32, g: f32, b: f32, a: f32) |ctx, pass, r, g, b, a| {
        record_result(ctx.global.render_pass_set_blend_constant(
            pass,
            wgt::Color {
                r: r as f64,
                g: g as f64,
                b: b as f64,
                a: a as f64,
            },
        ))
    }
);

render_pass_fn!(
    /// ABI: sets the stencil reference value.
    render_pass_set_stencil_reference(reference: u32) |ctx, pass, reference| {
        record_result(ctx.global.render_pass_set_stencil_reference(pass, reference))
    }
);

render_pass_fn!(
    /// ABI: uploads immediates (push constants) for the graphics stages.
    render_pass_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, pass, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(Status::INVALID_ARGUMENT, "null immediate data");
            return Err(Status::INVALID_ARGUMENT);
        }
        let bytes = if size == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(data, size as usize)
        };
        record_result(ctx.global.render_pass_set_immediates(pass, offset, bytes))
    }
);

render_pass_fn!(
    /// ABI: non-indexed draw.
    render_pass_draw(vertex_count: u32, instance_count: u32, first_vertex: u32, first_instance: u32) |ctx, pass, vertex_count, instance_count, first_vertex, first_instance| {
        record_result(ctx.global.render_pass_draw(pass, vertex_count, instance_count, first_vertex, first_instance))
    }
);

render_pass_fn!(
    /// ABI: indexed draw.
    render_pass_draw_indexed(index_count: u32, instance_count: u32, first_index: u32, vertex_offset: i32, first_instance: u32) |ctx, pass, index_count, instance_count, first_index, vertex_offset, first_instance| {
        record_result(ctx.global.render_pass_draw_indexed(pass, index_count, instance_count, first_index, vertex_offset, first_instance))
    }
);

render_pass_fn!(
    /// ABI: indirect non-indexed draw.
    render_pass_draw_indirect(buffer: BufferHandle, offset: u64) |ctx, pass, buffer, offset| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_pass_draw_indirect(pass, id, offset)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: indirect indexed draw.
    render_pass_draw_indexed_indirect(buffer: BufferHandle, offset: u64) |ctx, pass, buffer, offset| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_pass_draw_indexed_indirect(pass, id, offset)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: multi-draw indexed indirect.
    render_pass_multi_draw_indexed_indirect(buffer: BufferHandle, offset: u64, count: u32) |ctx, pass, buffer, offset, count| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_pass_multi_draw_indexed_indirect(pass, id, offset, count)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: multi-draw indirect.
    render_pass_multi_draw_indirect(buffer: BufferHandle, offset: u64, count: u32) |ctx, pass, buffer, offset, count| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_pass_multi_draw_indirect(pass, id, offset, count)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: multi-draw indirect with a GPU-resident draw count.
    render_pass_multi_draw_indirect_count(buffer: BufferHandle, offset: u64, count_buffer: BufferHandle, count_buffer_offset: u64, max_count: u32) |ctx, pass, buffer, offset, count_buffer, count_buffer_offset, max_count| {
        match (lookup_buffer(ctx, buffer), lookup_buffer(ctx, count_buffer)) {
            (Ok(id), Ok(count_id)) => record_result(ctx.global.render_pass_multi_draw_indirect_count(pass, id, offset, count_id, count_buffer_offset, max_count)),
            _ => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: multi-draw indexed indirect with a GPU-resident draw count.
    render_pass_multi_draw_indexed_indirect_count(buffer: BufferHandle, offset: u64, count_buffer: BufferHandle, count_buffer_offset: u64, max_count: u32) |ctx, pass, buffer, offset, count_buffer, count_buffer_offset, max_count| {
        match (lookup_buffer(ctx, buffer), lookup_buffer(ctx, count_buffer)) {
            (Ok(id), Ok(count_id)) => record_result(ctx.global.render_pass_multi_draw_indexed_indirect_count(pass, id, offset, count_id, count_buffer_offset, max_count)),
            _ => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: writes a timestamp inside the pass.
    render_pass_write_timestamp(query_set: QuerySetHandle, query_index: u32) |ctx, pass, query_set, query_index| {
        match lookup_query_set(ctx, query_set) {
            Ok(id) => record_result(ctx.global.render_pass_write_timestamp(pass, id, query_index)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid query set handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: inserts a debug marker label.
    render_pass_insert_debug_marker(label_text: *const c_char) |ctx, pass, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.render_pass_insert_debug_marker(pass, &text, 0))
    }
);

render_pass_fn!(
    /// ABI: opens a debug group.
    render_pass_push_debug_group(label_text: *const c_char) |ctx, pass, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.render_pass_push_debug_group(pass, &text, 0))
    }
);

render_pass_fn!(
    /// ABI: closes the current debug group.
    render_pass_pop_debug_group() |ctx, pass| {
        record_result(ctx.global.render_pass_pop_debug_group(pass))
    }
);

/// ABI: executes render bundles in an open pass. Up to four bundle identities
/// use fixed stack storage; larger arrays are preserved in a temporary vector.
///
/// # Safety
/// `pass` must be live and caller-exclusive. `bundles` must identify `bundle_count`
/// readable handles unless the count is zero. Every bundle must be live and from
/// the pass's context. End and Release must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn render_pass_execute_bundles(
    pass: RenderPassHandle,
    bundles: *const RenderBundleHandle,
    bundle_count: u32,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if bundles.is_null() && bundle_count > 0 {
                set_error(Status::INVALID_ARGUMENT, "null bundle array");
                return Err(Status::INVALID_ARGUMENT);
            }
            let obj = borrow_mut(pass, "render pass")?;
            let pass = obj.pass.as_mut().expect("live wrapper must contain a pass");
            let count = bundle_count as usize;
            if count == 0 {
                return record_result(obj.ctx.global.render_pass_execute_bundles(pass, &[]));
            }
            let handles = std::slice::from_raw_parts(bundles, count);
            let bundle_id = |handle: RenderBundleHandle| {
                let bundle = borrow(handle, "render bundle")?;
                debug_assert!(Arc::ptr_eq(&obj.ctx, &bundle.ctx));
                Ok::<_, Status>(bundle.id)
            };
            if count <= 4 {
                let first = bundle_id(handles[0])?;
                let mut ids = [first; 4];
                for index in 1..count {
                    ids[index] = bundle_id(handles[index])?;
                }
                record_result(
                    obj.ctx
                        .global
                        .render_pass_execute_bundles(pass, &ids[..count]),
                )
            } else {
                let mut ids = Vec::with_capacity(count);
                for &handle in handles {
                    ids.push(bundle_id(handle)?);
                }
                record_result(obj.ctx.global.render_pass_execute_bundles(pass, &ids))
            }
        })
    })
}

fn nonzero_size(size: u64) -> Option<wgt::BufferSize> {
    if size == 0 {
        None
    } else {
        wgt::BufferSize::new(size)
    }
}

// ---------------------------------------------------------------------------
// Compute pass
// ---------------------------------------------------------------------------

/// ABI: begins a compute pass; `timestamp_writes` may be null.
///
/// # Safety
/// `encoder` must be live and caller-exclusive; `out` must be writable. A non-null
/// timestamp descriptor must be readable and refer to a live same-context query set.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_begin_compute_pass(
    encoder: EncoderHandle,
    timestamp_writes: *const TimestampWrites,
    out: *mut ComputePassHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = ComputePassHandle::NULL;
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let wdesc = wgc::command::ComputePassDescriptor {
                label: None,
                timestamp_writes: pass_timestamps(ctx, timestamp_writes)?,
            };
            let (pass, err) = ctx
                .global
                .command_encoder_begin_compute_pass(encoder.id, &wdesc);
            if let Some(e) = err {
                set_error_from(Status::VALIDATION, &e);
                return Err(Status::VALIDATION);
            }
            *out = Handle::new(ComputePassObj {
                pass: Some(pass),
                ctx: ctx.clone(),
            });
            Ok(())
        })
    })
}

/// ABI: ends a compute pass, consuming its wrapper even on core failure.
/// End reports encoder-state errors immediately; recorded pass validation errors
/// remain on the parent encoder and are reported when it is finished.
///
/// # Safety
/// `pass` must be live and caller-exclusive. All recording must finish first.
#[no_mangle]
pub unsafe extern "C-unwind" fn compute_pass_end(pass: ComputePassHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let mut obj = consume(pass, "compute pass")?;
            let mut pass = obj.pass.take().expect("live wrapper must contain a pass");
            record_result(obj.ctx.global.compute_pass_end(&mut pass))
        })
    })
}

/// ABI: abandons a compute pass without End, consuming its wrapper.
/// A valid live pass always releases successfully, without core validation.
///
/// # Safety
/// `pass` must be live and caller-exclusive. No recording may overlap release.
#[no_mangle]
pub unsafe extern "C-unwind" fn compute_pass_release(pass: ComputePassHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(pass, "compute pass")?);
            Ok(())
        })
    })
}

macro_rules! compute_pass_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        ///
        /// # Safety
        /// The pass must be live and caller-exclusive; referenced handles must be
        /// live and from the same context. Pointer arguments must be valid for all
        /// accesses. End and Release must be ordered after this call returns.
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(pass: ComputePassHandle, $($arg: $ty),*) -> Status {
            crate::entry::guard(|| command_status(|| {
                let obj = borrow_mut(pass, "compute pass")?;
                type Body = fn(&Arc<DeviceCtx>, &mut wgc::command::ComputePass, $($ty),*) -> Result<(), Status>;
                let body: Body = $body;
                body(&obj.ctx, obj.pass.as_mut().expect("live wrapper must contain a pass") $(, $arg)*)
            }))
        }
    };
}

compute_pass_fn!(
    /// ABI: sets the compute pipeline.
    compute_pass_set_pipeline(pipeline: ComputePipelineHandle) |ctx, pass, pipeline| {
        let pipeline = borrow(pipeline, "compute pipeline")?;
        debug_assert!(Arc::ptr_eq(ctx, &pipeline.ctx));
        record_result(ctx.global.compute_pass_set_pipeline(pass, pipeline.id))
    }
);

compute_pass_fn!(
    /// ABI: binds a resource group at `slot`.
    compute_pass_set_bind_group(slot: u32, group: BindGroupHandle) |ctx, pass, slot, group| {
        let id = lookup_bind_group(ctx, group);
        match id {
            Ok(id) => record_result(ctx.global.compute_pass_set_bind_group(pass, slot, Some(id), &[])),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid bind group handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: uploads immediates (push constants) for the compute stage.
    compute_pass_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, pass, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(Status::INVALID_ARGUMENT, "null immediate data");
            return Err(Status::INVALID_ARGUMENT);
        }
        let bytes = if size == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(data, size as usize)
        };
        record_result(ctx.global.compute_pass_set_immediates(pass, offset, bytes))
    }
);

compute_pass_fn!(
    /// ABI: dispatches compute workgroups.
    compute_pass_dispatch_workgroups(x: u32, y: u32, z: u32) |ctx, pass, x, y, z| {
        record_result(ctx.global.compute_pass_dispatch_workgroups(pass, x, y, z))
    }
);

compute_pass_fn!(
    /// ABI: indirect compute dispatch.
    compute_pass_dispatch_workgroups_indirect(buffer: BufferHandle, offset: u64) |ctx, pass, buffer, offset| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.compute_pass_dispatch_workgroups_indirect(pass, id, offset)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: writes a timestamp inside the pass.
    compute_pass_write_timestamp(query_set: QuerySetHandle, query_index: u32) |ctx, pass, query_set, query_index| {
        match lookup_query_set(ctx, query_set) {
            Ok(id) => record_result(ctx.global.compute_pass_write_timestamp(pass, id, query_index)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid query set handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: inserts a debug marker label.
    compute_pass_insert_debug_marker(label_text: *const c_char) |ctx, pass, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.compute_pass_insert_debug_marker(pass, &text, 0))
    }
);

compute_pass_fn!(
    /// ABI: opens a debug group.
    compute_pass_push_debug_group(label_text: *const c_char) |ctx, pass, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.compute_pass_push_debug_group(pass, &text, 0))
    }
);

compute_pass_fn!(
    /// ABI: closes the current debug group.
    compute_pass_pop_debug_group() |ctx, pass| {
        record_result(ctx.global.compute_pass_pop_debug_group(pass))
    }
);

// ---------------------------------------------------------------------------
// Copies and queries
// ---------------------------------------------------------------------------

/// ABI: buffer-to-buffer copy on the open encoder.
///
/// # Safety
/// The encoder must be live and caller-exclusive; resource handles must be live
/// and from its context. Non-null pointer arguments must be valid for all accesses.
/// Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_copy_buffer_to_buffer(
    encoder: EncoderHandle,
    source: BufferHandle,
    source_offset: u64,
    destination: BufferHandle,
    destination_offset: u64,
    size: u64,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let src = lookup_buffer(ctx, source)?;
            let dst = lookup_buffer(ctx, destination)?;
            match ctx.global.command_encoder_copy_buffer_to_buffer(
                encoder_id,
                src,
                source_offset,
                dst,
                destination_offset,
                Some(size),
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

unsafe fn lookup_buffer(
    ctx: &Arc<DeviceCtx>,
    handle: BufferHandle,
) -> Result<wgc::id::BufferId, Status> {
    let obj = borrow(handle, "buffer")?;
    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
    Ok(obj.id)
}

unsafe fn lookup_texture(
    ctx: &Arc<DeviceCtx>,
    handle: TextureHandle,
) -> Result<wgc::id::TextureId, Status> {
    let obj = borrow(handle, "texture")?;
    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
    Ok(obj.id)
}

unsafe fn lookup_bind_group(
    ctx: &Arc<DeviceCtx>,
    handle: BindGroupHandle,
) -> Result<wgc::id::BindGroupId, Status> {
    let obj = borrow(handle, "bind group")?;
    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
    Ok(obj.id)
}

unsafe fn lookup_query_set(
    ctx: &Arc<DeviceCtx>,
    handle: QuerySetHandle,
) -> Result<wgc::id::QuerySetId, Status> {
    let obj = borrow(handle, "query set")?;
    debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
    Ok(obj.id)
}

fn copy_layout(layout: &CopyLayout) -> Result<wgt::TexelCopyBufferLayout, Status> {
    Ok(wgt::TexelCopyBufferLayout {
        offset: layout.offset,
        bytes_per_row: optional_index(layout.bytes_per_row),
        rows_per_image: optional_index(layout.rows_per_image),
    })
}

unsafe fn copy_texture_info(
    ctx: &Arc<DeviceCtx>,
    texture: TextureHandle,
    mip_level: u32,
    origin: Origin3D,
    aspect: u32,
) -> Result<wgt::TexelCopyTextureInfo<wgc::id::TextureId>, Status> {
    Ok(wgt::TexelCopyTextureInfo {
        texture: lookup_texture(ctx, texture)?,
        mip_level,
        origin: wgt::Origin3d {
            x: origin.x,
            y: origin.y,
            z: origin.z,
        },
        aspect: texture_aspect(aspect)?,
    })
}

fn extent3d(extent: Extent3D) -> wgt::Extent3d {
    wgt::Extent3d {
        width: extent.width,
        height: extent.height,
        depth_or_array_layers: extent.depth_or_array_layers,
    }
}

/// ABI: buffer-to-texture copy on the open encoder.
///
/// # Safety
/// The encoder must be live and caller-exclusive; resource handles must be live
/// and from its context. Non-null pointer arguments must be valid for all accesses.
/// Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_copy_buffer_to_texture(
    encoder: EncoderHandle,
    source: BufferHandle,
    source_layout: *const CopyLayout,
    destination: TextureHandle,
    destination_mip_level: u32,
    destination_aspect: u32,
    copy_size: Extent3D,
) -> Status {
    crate::entry::guard(|| {
        let source_layout = match source_layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(Status::INVALID_ARGUMENT, "null source layout");
                return Status::INVALID_ARGUMENT;
            }
        };
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let source = wgc::command::TexelCopyBufferInfo {
                buffer: lookup_buffer(ctx, source)?,
                layout: copy_layout(source_layout)?,
            };
            let destination = copy_texture_info(
                ctx,
                destination,
                destination_mip_level,
                Origin3D { x: 0, y: 0, z: 0 },
                destination_aspect,
            )?;
            let size = extent3d(copy_size);
            match ctx.global.command_encoder_copy_buffer_to_texture(
                encoder_id,
                &source,
                &destination,
                &size,
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: texture-to-buffer copy on the open encoder.
///
/// # Safety
/// The encoder must be live and caller-exclusive; resource handles must be live
/// and from its context. Non-null pointer arguments must be valid for all accesses.
/// Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_copy_texture_to_buffer(
    encoder: EncoderHandle,
    source: TextureHandle,
    source_mip_level: u32,
    source_aspect: u32,
    destination: BufferHandle,
    destination_layout: *const CopyLayout,
    copy_size: Extent3D,
) -> Status {
    crate::entry::guard(|| {
        let destination_layout = match destination_layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(Status::INVALID_ARGUMENT, "null destination layout");
                return Status::INVALID_ARGUMENT;
            }
        };
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let source = copy_texture_info(
                ctx,
                source,
                source_mip_level,
                Origin3D { x: 0, y: 0, z: 0 },
                source_aspect,
            )?;
            let destination = wgc::command::TexelCopyBufferInfo {
                buffer: lookup_buffer(ctx, destination)?,
                layout: copy_layout(destination_layout)?,
            };
            let size = extent3d(copy_size);
            match ctx.global.command_encoder_copy_texture_to_buffer(
                encoder_id,
                &source,
                &destination,
                &size,
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: texture-to-texture copy on the open encoder.
///
/// # Safety
/// The encoder must be live and caller-exclusive; resource handles must be live
/// and from its context. Non-null pointer arguments must be valid for all accesses.
/// Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_copy_texture_to_texture(
    encoder: EncoderHandle,
    source: TextureHandle,
    source_mip_level: u32,
    destination: TextureHandle,
    destination_mip_level: u32,
    aspect: u32,
    copy_size: Extent3D,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let source = copy_texture_info(
                ctx,
                source,
                source_mip_level,
                Origin3D { x: 0, y: 0, z: 0 },
                aspect,
            )?;
            let destination = copy_texture_info(
                ctx,
                destination,
                destination_mip_level,
                Origin3D { x: 0, y: 0, z: 0 },
                aspect,
            )?;
            let size = extent3d(copy_size);
            match ctx.global.command_encoder_copy_texture_to_texture(
                encoder_id,
                &source,
                &destination,
                &size,
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: resolves timestamp queries into a buffer.
///
/// # Safety
/// The encoder must be live and caller-exclusive; resource handles must be live
/// and from its context. Non-null pointer arguments must be valid for all accesses.
/// Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_resolve_query_set(
    encoder: EncoderHandle,
    query_set: QuerySetHandle,
    first_query: u32,
    query_count: u32,
    destination: BufferHandle,
    destination_offset: u64,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let query_set_id = lookup_query_set(ctx, query_set)?;
            let destination = lookup_buffer(ctx, destination)?;
            match ctx.global.command_encoder_resolve_query_set(
                encoder_id,
                query_set_id,
                first_query,
                query_count,
                destination,
                destination_offset,
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// Subresource range of a texture clear; counts of zero or `NONE` mean
/// "the rest" from the base level or layer.
#[repr(C)]
pub struct SubresourceRange {
    /// `TextureAspect` value: 0/1 all, 2 stencil only, 3 depth only.
    pub aspect: u32,
    /// First cleared mip level.
    pub base_mip_level: u32,
    /// Cleared mip level count; zero or `NONE` = the rest.
    pub mip_level_count: u32,
    /// First cleared array layer.
    pub base_array_layer: u32,
    /// Cleared array layer count; zero or `NONE` = the rest.
    pub array_layer_count: u32,
}

/// ABI: zeroes a buffer range on the open encoder; a `size` of zero clears
/// from `offset` to the end of the buffer.
///
/// # Safety
/// The encoder and buffer must be live, caller-exclusive, and from the same
/// context. Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_clear_buffer(
    encoder: EncoderHandle,
    buffer: BufferHandle,
    offset: u64,
    size: u64,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let destination = lookup_buffer(ctx, buffer)?;
            let size = if size == 0 { None } else { Some(size) };
            record_result(ctx.global.command_encoder_clear_buffer(
                encoder_id,
                destination,
                offset,
                size,
            ))
        })
    })
}

/// ABI: zeroes texture subresources on the open encoder to the format's zero
/// value.
///
/// # Safety
/// The encoder and texture must be live, caller-exclusive, and from the same
/// context. Finish and destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_clear_texture(
    encoder: EncoderHandle,
    texture: TextureHandle,
    range: SubresourceRange,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            let ctx = &encoder.ctx;
            let encoder_id = encoder.id;
            let destination = lookup_texture(ctx, texture)?;
            let aspect = texture_aspect(range.aspect)?;
            let wrange = wgt::ImageSubresourceRange {
                aspect,
                base_mip_level: range.base_mip_level,
                mip_level_count: plain_or_none(range.mip_level_count),
                base_array_layer: range.base_array_layer,
                array_layer_count: plain_or_none(range.array_layer_count),
            };
            record_result(ctx.global.command_encoder_clear_texture(
                encoder_id,
                destination,
                &wrange,
            ))
        })
    })
}

/// ABI: inserts a debug marker label into the encoded stream.
///
/// # Safety
/// The encoder must be live and caller-exclusive; `label_text` must reference
/// a NUL-terminated UTF-8 string for the call. Finish and destruction must be
/// ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_insert_debug_marker(
    encoder: EncoderHandle,
    label_text: *const c_char,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let text = debug_label(label_text)?;
            let encoder = borrow(encoder, "encoder")?;
            record_result(
                encoder
                    .ctx
                    .global
                    .command_encoder_insert_debug_marker(encoder.id, &text),
            )
        })
    })
}

/// ABI: opens a debug group in the encoded stream.
///
/// # Safety
/// The encoder must be live and caller-exclusive; `label_text` must reference
/// a NUL-terminated UTF-8 string for the call. Finish and destruction must be
/// ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_push_debug_group(
    encoder: EncoderHandle,
    label_text: *const c_char,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let text = debug_label(label_text)?;
            let encoder = borrow(encoder, "encoder")?;
            record_result(
                encoder
                    .ctx
                    .global
                    .command_encoder_push_debug_group(encoder.id, &text),
            )
        })
    })
}

/// ABI: closes the current debug group in the encoded stream.
///
/// # Safety
/// The encoder must be live and caller-exclusive. Finish and destruction must
/// be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn encoder_pop_debug_group(encoder: EncoderHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let encoder = borrow(encoder, "encoder")?;
            record_result(
                encoder
                    .ctx
                    .global
                    .command_encoder_pop_debug_group(encoder.id),
            )
        })
    })
}

// ---------------------------------------------------------------------------
// Queue
// ---------------------------------------------------------------------------

/// ABI: writes bytes into a buffer through the queue (bypasses command
/// encoding).
///
/// # Safety
/// The device and resource must be live and from the same context. Pointer
/// arguments must be valid for all accesses; data may be null only for zero size.
/// Device and resource destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn queue_write_buffer(
    device: DeviceHandle,
    buffer: BufferHandle,
    offset: u64,
    data: *const u8,
    size: u32,
) -> Status {
    crate::entry::guard(|| {
        if data.is_null() && size > 0 {
            set_error(Status::INVALID_ARGUMENT, "null data pointer");
            return Status::INVALID_ARGUMENT;
        }
        command_status(|| {
            let ctx = &borrow(device, "device")?.ctx;
            let buffer_id = lookup_buffer(ctx, buffer)?;
            let bytes = if size == 0 {
                &[]
            } else {
                std::slice::from_raw_parts(data, size as usize)
            };
            match ctx
                .global
                .queue_write_buffer(ctx.queue_id, buffer_id, offset, bytes)
            {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: writes raw bytes into a texture region through the queue.
///
/// # Safety
/// The device and resource must be live and from the same context. Pointer
/// arguments must be valid for all accesses; data may be null only for zero size.
/// Device and resource destruction must be ordered after this call returns.
#[no_mangle]
pub unsafe extern "C-unwind" fn queue_write_texture(
    device: DeviceHandle,
    texture: TextureHandle,
    mip_level: u32,
    origin: Origin3D,
    aspect: u32,
    data: *const u8,
    data_size: u32,
    layout: *const CopyLayout,
    size: Extent3D,
) -> Status {
    crate::entry::guard(|| {
        if data.is_null() && data_size > 0 {
            set_error(Status::INVALID_ARGUMENT, "null data pointer");
            return Status::INVALID_ARGUMENT;
        }
        let layout = match layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(Status::INVALID_ARGUMENT, "null layout");
                return Status::INVALID_ARGUMENT;
            }
        };
        command_status(|| {
            let ctx = &borrow(device, "device")?.ctx;
            let destination = copy_texture_info(ctx, texture, mip_level, origin, aspect)?;
            let bytes = if data_size == 0 {
                &[]
            } else {
                std::slice::from_raw_parts(data, data_size as usize)
            };
            let wlayout = copy_layout(layout)?;
            let wsize = extent3d(size);
            match ctx.global.queue_write_texture(
                ctx.queue_id,
                &destination,
                bytes,
                &wlayout,
                &wsize,
            ) {
                Ok(()) => Ok(()),
                Err(e) => {
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: submits a command buffer through the device's sole queue. The wrapper
/// and core registration are consumed on success or core failure. `out_index`
/// receives the submission index usable with `device_poll`.
///
/// # Safety
/// `device` and `command_buffer` must be live and from the same context. No
/// command-buffer access may overlap submission. `out_index`, if non-null, must
/// point to a writable `u64`. Device destruction must not overlap this call.
#[no_mangle]
pub unsafe extern "C-unwind" fn queue_submit(
    device: DeviceHandle,
    command_buffer: CommandBufferHandle,
    out_index: *mut u64,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let ctx = &borrow(device, "device")?.ctx;
            let obj = consume(command_buffer, "command buffer")?;
            debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
            let result = ctx.global.queue_submit(ctx.queue_id, &[obj.id]);
            // Core consumes contents, not the registry entry; Drop releases that
            // entry on either outcome before the error callback may unwind.
            drop(obj);
            match result {
                Ok(index) => {
                    if !out_index.is_null() {
                        *out_index = index;
                    }
                    Ok(())
                }
                Err((index, e)) => {
                    if !out_index.is_null() {
                        *out_index = index;
                    }
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

/// ABI: submits an array of command buffers through the device's sole queue as
/// one core submission, in array order. Every wrapper and core registration is
/// consumed on success or core failure; an invalid entry after partial
/// consumption releases the already-consumed buffers without executing them.
/// `out_index` receives the submission index usable with `device_poll`.
///
/// # Safety
/// `device` and every entry of `command_buffers[0..count]` must be live and from
/// the same context. No command-buffer access may overlap submission. `out_index`,
/// if non-null, must point to a writable `u64`. Device destruction must not
/// overlap this call.
#[no_mangle]
pub unsafe extern "C-unwind" fn queue_submit_batch(
    device: DeviceHandle,
    command_buffers: *const CommandBufferHandle,
    count: u32,
    out_index: *mut u64,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            let ctx = &borrow(device, "device")?.ctx;
            if command_buffers.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null command buffer array");
                return Err(Status::INVALID_ARGUMENT);
            }
            if count == 0 {
                set_error(Status::INVALID_ARGUMENT, "zero command buffer count");
                return Err(Status::INVALID_ARGUMENT);
            }
            let handles = std::slice::from_raw_parts(command_buffers, count as usize);
            // Consume every wrapper before submitting so any invalid entry
            // releases the already-consumed buffers instead of orphaning them.
            let mut objects = Vec::with_capacity(handles.len());
            for &handle in handles {
                let obj = consume(handle, "command buffer")?;
                debug_assert!(Arc::ptr_eq(ctx, &obj.ctx));
                objects.push(obj);
            }
            // Typical frame submissions stay on the stack; larger batches are
            // preserved so core validates the complete array in order.
            let count = handles.len();
            let result = {
                let inline: [wgc::id::CommandBufferId; 8];
                let overflow: Vec<wgc::id::CommandBufferId>;
                let ids = if count <= 8 {
                    let first = objects[0].id;
                    let mut buffer = [first; 8];
                    for index in 1..count {
                        buffer[index] = objects[index].id;
                    }
                    inline = buffer;
                    &inline[..count]
                } else {
                    overflow = objects.iter().map(|obj| obj.id).collect();
                    &overflow[..]
                };
                ctx.global.queue_submit(ctx.queue_id, ids)
            };
            // Core consumes contents, not the registry entries; Drop releases
            // them on either outcome before the error callback may unwind.
            drop(objects);
            match result {
                Ok(index) => {
                    if !out_index.is_null() {
                        *out_index = index;
                    }
                    Ok(())
                }
                Err((index, e)) => {
                    if !out_index.is_null() {
                        *out_index = index;
                    }
                    set_error_from(Status::VALIDATION, &e);
                    Err(Status::VALIDATION)
                }
            }
        })
    })
}

// ---------------------------------------------------------------------------
// Render bundles
// ---------------------------------------------------------------------------

/// C# `RenderBundleDescriptor`.
#[repr(C)]
pub struct BundleEncoderDesc {
    /// Readable color format array, nullable when its count is zero.
    pub color_formats: *const u32,
    /// Number of color target formats.
    pub color_format_count: u32,
    /// `NONE`/0 when the bundle targets no depth attachment.
    pub depth_stencil_format: u32,
    /// Whether the depth aspect is read-only.
    pub depth_read_only: u32,
    /// Whether the stencil aspect is read-only.
    pub stencil_read_only: u32,
    /// Target sample count; zero uses one sample.
    pub sample_count: u32,
    /// Optional NUL-terminated debug label.
    pub name: *const c_char,
}

/// ABI: creates a render bundle encoder. On failure the output handle is null.
///
/// # Safety
/// `device` must be live, `desc` and its non-null format/name pointers must be
/// readable, and `out` must be writable. Device destruction must not overlap.
#[no_mangle]
pub unsafe extern "C-unwind" fn device_create_bundle_encoder(
    device: DeviceHandle,
    desc: *const BundleEncoderDesc,
    out: *mut BundleEncoderHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = BundleEncoderHandle::NULL;
            let desc = desc.as_ref().ok_or_else(|| {
                set_error(Status::INVALID_ARGUMENT, "null descriptor");
                Status::INVALID_ARGUMENT
            })?;
            let ctx = &borrow(device, "device")?.ctx;
            let mut formats = Vec::new();
            if desc.color_format_count > 0 {
                if desc.color_formats.is_null() {
                    set_error(Status::INVALID_ARGUMENT, "null color format array");
                    return Err(Status::INVALID_ARGUMENT);
                }
                let raw = std::slice::from_raw_parts(
                    desc.color_formats,
                    desc.color_format_count as usize,
                );
                formats.reserve(raw.len());
                for &format in raw {
                    formats.push(Some(pixel_format(format)?));
                }
            }
            let depth_stencil =
                if desc.depth_stencil_format != NONE && desc.depth_stencil_format != 0 {
                    Some(wgt::RenderBundleDepthStencil {
                        format: pixel_format(desc.depth_stencil_format)?,
                        depth_read_only: desc.depth_read_only != 0,
                        stencil_read_only: desc.stencil_read_only != 0,
                    })
                } else {
                    None
                };
            let wdesc = wgc::command::RenderBundleEncoderDescriptor {
                label: label(desc.name),
                color_formats: std::borrow::Cow::Owned(formats),
                depth_stencil,
                sample_count: desc.sample_count.max(1),
                multiview: None,
            };
            let (encoder, err) = ctx
                .global
                .device_create_render_bundle_encoder(ctx.device_id, &wdesc);
            if let Some(e) = err {
                set_error_from(Status::VALIDATION, &e);
                return Err(Status::VALIDATION);
            }
            *out = Handle::new(BundleEncoderObj {
                encoder: Some(encoder),
                ctx: ctx.clone(),
            });
            Ok(())
        })
    })
}

/// ABI: finishes a bundle encoder, consuming it even if core validation fails.
/// A null output pointer is rejected before the encoder is consumed.
///
/// # Safety
/// `bundle_encoder` must be live and caller-exclusive; `out` must be writable.
/// All recording must finish before this call.
#[no_mangle]
pub unsafe extern "C-unwind" fn bundle_encoder_finish(
    bundle_encoder: BundleEncoderHandle,
    out: *mut RenderBundleHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            if out.is_null() {
                set_error(Status::INVALID_ARGUMENT, "null out pointer");
                return Err(Status::INVALID_ARGUMENT);
            }
            *out = RenderBundleHandle::NULL;
            let mut obj = consume(bundle_encoder, "bundle encoder")?;
            let mut encoder = obj
                .encoder
                .take()
                .expect("live wrapper must contain a bundle encoder");
            let wdesc = wgc::command::RenderBundleDescriptor { label: None };
            let (id, err) = obj
                .ctx
                .global
                .render_bundle_encoder_finish(&mut encoder, &wdesc, None);
            let bundle = RenderBundleObj {
                id,
                ctx: obj.ctx.clone(),
            };
            // Drop core recording state before releasing either context reference.
            drop(encoder);
            drop(obj);
            if let Some(e) = err {
                set_error_from(Status::VALIDATION, &e);
                return Err(Status::VALIDATION);
            }
            *out = Handle::new(bundle);
            Ok(())
        })
    })
}

/// ABI: destroys an unfinished bundle encoder without validation.
///
/// # Safety
/// `bundle_encoder` must be live and no recording may overlap destruction.
#[no_mangle]
pub unsafe extern "C-unwind" fn bundle_encoder_destroy(
    bundle_encoder: BundleEncoderHandle,
) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(bundle_encoder, "bundle encoder")?);
            Ok(())
        })
    })
}

/// ABI: consumes a finished render bundle and releases its core registration.
///
/// # Safety
/// `bundle` must be live and all access must complete before destruction.
#[no_mangle]
pub unsafe extern "C-unwind" fn render_bundle_destroy(bundle: RenderBundleHandle) -> Status {
    crate::entry::guard(|| {
        command_status(|| {
            drop(consume(bundle, "render bundle")?);
            Ok(())
        })
    })
}

macro_rules! bundle_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        ///
        /// # Safety
        /// The encoder must be live and caller-exclusive; referenced handles must
        /// be live and from the same context. Pointer arguments must be valid for
        /// all accesses. Finish and destruction must follow this call.
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(bundle_encoder: BundleEncoderHandle, $($arg: $ty),*) -> Status {
            crate::entry::guard(|| command_status(|| {
                let obj = borrow_mut(bundle_encoder, "bundle encoder")?;
                type Body = fn(&Arc<DeviceCtx>, &mut wgc::command::RenderBundleEncoder, $($ty),*) -> Result<(), Status>;
                let body: Body = $body;
                body(&obj.ctx, obj.encoder.as_deref_mut().expect("live wrapper must contain a bundle encoder") $(, $arg)*)
            }))
        }
    };
}

bundle_fn!(
    /// ABI: sets the bundle's graphics pipeline.
    bundle_set_pipeline(pipeline: GraphicsPipelineHandle) |ctx, bundle, pipeline| {
        let pipeline = borrow(pipeline, "graphics pipeline")?;
        debug_assert!(Arc::ptr_eq(ctx, &pipeline.ctx));
        record_result(ctx.global.render_bundle_encoder_set_pipeline(bundle, pipeline.id))
    }
);

bundle_fn!(
    /// ABI: binds a resource group at `slot`.
    bundle_set_bind_group(slot: u32, group: BindGroupHandle) |ctx, bundle, slot, group| {
        let id = lookup_bind_group(ctx, group);
        match id {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_set_bind_group(bundle, slot, Some(id), &[])),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid bind group handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: binds a vertex buffer slot.
    bundle_set_vertex_buffer(slot: u32, buffer: BufferHandle, offset: u64, size: u64) |ctx, bundle, slot, buffer, offset, size| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_set_vertex_buffer(
                bundle,
                slot,
                Some(id),
                offset,
                nonzero_size(size),
            )),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: binds the index buffer.
    bundle_set_index_buffer(buffer: BufferHandle, format: u32, offset: u64, size: u64) |ctx, bundle, buffer, format, offset, size| {
        let wformat = index_format(format);
        match lookup_buffer(ctx, buffer) {
            Ok(id) => match wformat {
                Ok(format) => record_result(ctx.global.render_bundle_encoder_set_index_buffer(
                    bundle,
                    id,
                    format,
                    offset,
                    nonzero_size(size),
                )),
                Err(s) => Err(s),
            },
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: uploads immediates (push constants).
    bundle_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, bundle, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(Status::INVALID_ARGUMENT, "null immediate data");
            return Err(Status::INVALID_ARGUMENT);
        }
        let bytes = if size == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(data, size as usize)
        };
        record_result(ctx.global.render_bundle_encoder_set_immediates(bundle, offset, bytes))
    }
);

bundle_fn!(
    /// ABI: non-indexed draw.
    bundle_draw(vertex_count: u32, instance_count: u32, first_vertex: u32, first_instance: u32) |ctx, bundle, vertex_count, instance_count, first_vertex, first_instance| {
        record_result(ctx.global.render_bundle_encoder_draw(bundle, vertex_count, instance_count, first_vertex, first_instance))
    }
);

bundle_fn!(
    /// ABI: indexed draw.
    bundle_draw_indexed(index_count: u32, instance_count: u32, first_index: u32, vertex_offset: i32, first_instance: u32) |ctx, bundle, index_count, instance_count, first_index, vertex_offset, first_instance| {
        record_result(ctx.global.render_bundle_encoder_draw_indexed(bundle, index_count, instance_count, first_index, vertex_offset, first_instance))
    }
);

bundle_fn!(
    /// ABI: indirect non-indexed draw.
    bundle_draw_indirect(buffer: BufferHandle, offset: u64) |ctx, bundle, buffer, offset| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_draw_indirect(bundle, id, offset)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: inserts a debug marker label recorded into the bundle.
    bundle_insert_debug_marker(label_text: *const c_char) |ctx, bundle, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.render_bundle_encoder_insert_debug_marker(bundle, &text))
    }
);

bundle_fn!(
    /// ABI: opens a debug group recorded into the bundle.
    bundle_push_debug_group(label_text: *const c_char) |ctx, bundle, label_text| {
        let text = debug_label(label_text)?;
        record_result(ctx.global.render_bundle_encoder_push_debug_group(bundle, &text))
    }
);

bundle_fn!(
    /// ABI: closes the current debug group recorded into the bundle.
    bundle_pop_debug_group() |ctx, bundle| {
        record_result(ctx.global.render_bundle_encoder_pop_debug_group(bundle))
    }
);

bundle_fn!(
    /// ABI: indirect indexed draw.
    bundle_draw_indexed_indirect(buffer: BufferHandle, offset: u64) |ctx, bundle, buffer, offset| {
        match lookup_buffer(ctx, buffer) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_draw_indexed_indirect(bundle, id, offset)),
            Err(_) => {
                set_error(Status::INVALID_HANDLE, "invalid buffer handle");
                Err(Status::INVALID_HANDLE)
            }
        }
    }
);

#[cfg(test)]
mod tests {
    use super::*;
    use crate::device::device_poll;
    use crate::objects::*;
    use crate::test_support::{last_error, TestDevice};
    use std::ptr;

    unsafe fn registry_counts(device: DeviceHandle) -> (usize, usize, usize) {
        let report = device.get().unwrap().ctx.global.generate_report().hub;
        assert_eq!(
            report.command_buffers.num_allocated,
            report.command_buffers.num_kept_from_user
        );
        (
            report.command_buffers.num_allocated,
            report.command_buffers.num_kept_from_user,
            report.command_encoders.num_allocated,
        )
    }

    unsafe fn encoder(device: DeviceHandle) -> EncoderHandle {
        let mut handle = EncoderHandle::NULL;
        assert_eq!(
            device_create_encoder(device, ptr::null(), &mut handle),
            Status::OK,
            "{}",
            last_error()
        );
        handle
    }

    unsafe fn finish(encoder: EncoderHandle) -> CommandBufferHandle {
        let mut handle = CommandBufferHandle::NULL;
        assert_eq!(
            encoder_finish(encoder, &mut handle),
            Status::OK,
            "{}",
            last_error()
        );
        handle
    }

    unsafe fn buffer(device: DeviceHandle, usage: u32) -> BufferHandle {
        let desc = BufferDesc {
            size: 4,
            usage,
            name: ptr::null(),
        };
        let mut handle = BufferHandle::NULL;
        assert_eq!(
            device_create_buffer(device, &desc, &mut handle),
            Status::OK,
            "{}",
            last_error()
        );
        handle
    }

    #[test]
    fn vulkan_independent_recording_and_resource_lifetimes_run_concurrently() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        const WORKERS: usize = 4;
        let start = std::sync::Barrier::new(WORKERS);
        let handle = device.handle;
        std::thread::scope(|scope| {
            for _ in 0..WORKERS {
                let start = &start;
                scope.spawn(move || {
                    start.wait();
                    unsafe {
                        for _ in 0..32 {
                            let encoder = encoder(handle);
                            let mut pass = ComputePassHandle::NULL;
                            assert_eq!(
                                encoder_begin_compute_pass(encoder, ptr::null(), &mut pass),
                                Status::OK,
                                "{}",
                                last_error()
                            );
                            for _ in 0..8 {
                                let buffer = buffer(handle, 1 << 3);
                                assert_eq!(
                                    compute_pass_set_immediates(pass, 0, ptr::null(), 0),
                                    Status::OK,
                                    "{}",
                                    last_error()
                                );
                                assert_eq!(buffer_destroy(buffer), Status::OK, "{}", last_error());
                            }
                            assert_eq!(compute_pass_end(pass), Status::OK, "{}", last_error());
                            // Discard the pass without requiring a compute pipeline or dispatch.
                            assert_eq!(encoder_destroy(encoder), Status::OK, "{}", last_error());
                        }
                    }
                });
            }
        });
        unsafe {
            assert_eq!(registry_counts(handle), (0, 0, 0));
            let report = handle
                .get()
                .unwrap()
                .ctx
                .global
                .generate_report()
                .hub
                .buffers;
            assert_eq!(report.num_allocated, 0, "{report:?}");
            assert_eq!(report.num_kept_from_user, 0, "{report:?}");
        }
    }

    #[test]
    fn vulkan_successful_submissions_release_core_registry_entries() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            let mut previous_index = 0;
            for iteration in 0..1024 {
                let encoder = encoder(device.handle);
                let command = finish(encoder);
                assert_eq!(registry_counts(device.handle), (1, 1, 0));
                let mut index = 0;
                assert_eq!(
                    queue_submit(device.handle, command, &mut index),
                    Status::OK,
                    "{}",
                    last_error()
                );
                assert!(index > previous_index);
                previous_index = index;
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
                if iteration % 64 == 63 {
                    assert_eq!(
                        device_poll(device.handle, TRUE, index, ptr::null_mut()),
                        Status::OK,
                        "{}",
                        last_error()
                    );
                    assert_eq!(registry_counts(device.handle), (0, 0, 0));
                }
            }
            // The optional output pointer must not alter cleanup semantics.
            let encoder = encoder(device.handle);
            let command = finish(encoder);
            assert_eq!(
                queue_submit(device.handle, command, ptr::null_mut()),
                Status::OK
            );
            assert_eq!(
                device_poll(device.handle, TRUE, u64::MAX, ptr::null_mut()),
                Status::OK
            );
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_submission_consumes_handle_and_core_registry_entry() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            // A destroyed resource is detected at submission, after a valid finish.
            let ctx = &device.handle.get().unwrap().ctx;
            let baseline = Arc::strong_count(ctx);
            let source = buffer(device.handle, 1 << 2);
            let destination = buffer(device.handle, 1 << 3);
            let encoder = encoder(device.handle);
            assert_eq!(
                encoder_copy_buffer_to_buffer(encoder, source, 0, destination, 0, 4),
                Status::OK,
                "{}",
                last_error()
            );
            let command = finish(encoder);
            assert_eq!(registry_counts(device.handle), (1, 1, 0));
            assert_eq!(buffer_destroy(source), Status::OK);
            let mut index = u64::MAX;
            let status = queue_submit(device.handle, command, &mut index);
            assert_eq!(status, Status::VALIDATION, "{}", last_error());
            assert!(last_error().contains("destroyed"), "{}", last_error());
            assert_ne!(index, u64::MAX);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(buffer_destroy(destination), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);
        }
    }

    #[test]
    fn vulkan_batch_submissions_release_core_registry_entries() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            // A batch spanning the inline stack storage and the overflow path.
            let mut previous_index = 0;
            for batch_size in [1usize, 3, 8, 9, 17] {
                let commands: Vec<CommandBufferHandle> = (0..batch_size)
                    .map(|_| finish(encoder(device.handle)))
                    .collect();
                assert_eq!(registry_counts(device.handle), (batch_size, batch_size, 0));
                let mut index = 0;
                assert_eq!(
                    queue_submit_batch(
                        device.handle,
                        commands.as_ptr(),
                        commands.len() as u32,
                        &mut index,
                    ),
                    Status::OK,
                    "{}",
                    last_error()
                );
                assert!(index > previous_index);
                previous_index = index;
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
                assert_eq!(
                    device_poll(device.handle, TRUE, index, ptr::null_mut()),
                    Status::OK,
                    "{}",
                    last_error()
                );
            }
            // The optional output pointer must not alter cleanup semantics.
            let commands: Vec<CommandBufferHandle> =
                (0..2).map(|_| finish(encoder(device.handle))).collect();
            assert_eq!(
                queue_submit_batch(
                    device.handle,
                    commands.as_ptr(),
                    commands.len() as u32,
                    ptr::null_mut(),
                ),
                Status::OK
            );
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            // Rejected arguments must not consume any wrapper.
            let command = finish(encoder(device.handle));
            assert_eq!(
                queue_submit_batch(device.handle, ptr::null(), 2, ptr::null_mut()),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(
                queue_submit_batch(device.handle, &command, 0, ptr::null_mut(),),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(registry_counts(device.handle), (1, 1, 0));
            assert_eq!(
                command_buffer_destroy(command),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_batch_submission_consumes_every_handle() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            // The middle buffer references a destroyed resource, detected by
            // core at submission after every wrapper was already consumed.
            let ctx = &device.handle.get().unwrap().ctx;
            let baseline = Arc::strong_count(ctx);
            let source = buffer(device.handle, 1 << 2);
            let destination = buffer(device.handle, 1 << 3);
            let first = finish(encoder(device.handle));
            let failing_encoder = encoder(device.handle);
            assert_eq!(
                encoder_copy_buffer_to_buffer(failing_encoder, source, 0, destination, 0, 4),
                Status::OK,
                "{}",
                last_error()
            );
            let middle = finish(failing_encoder);
            let last = finish(encoder(device.handle));
            let commands = [first, middle, last];
            assert_eq!(registry_counts(device.handle), (3, 3, 0));
            assert_eq!(buffer_destroy(source), Status::OK);
            let mut index = u64::MAX;
            assert_eq!(
                queue_submit_batch(
                    device.handle,
                    commands.as_ptr(),
                    commands.len() as u32,
                    &mut index,
                ),
                Status::VALIDATION,
                "{}",
                last_error()
            );
            assert!(last_error().contains("destroyed"), "{}", last_error());
            assert_ne!(index, u64::MAX);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(buffer_destroy(destination), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);
        }
    }

    #[test]
    fn vulkan_dynamic_pass_state_and_debug_markers_record_cleanly() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            // A pass needs at least one attachment to survive finish
            // validation; the viewport matches this 64x32 target.
            let texture_desc = TextureDesc {
                dimension: 1,
                format: 18,
                usage: 1 << 4, // ColorAttachment
                width: 64,
                height: 32,
                depth_or_array_layers: 1,
                mip_level_count: 1,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut target = TextureHandle::NULL;
            assert_eq!(
                device_create_texture(device.handle, &texture_desc, &mut target),
                Status::OK,
                "{}",
                last_error()
            );
            let mut view = TextureViewHandle::NULL;
            assert_eq!(
                texture_create_view(target, ptr::null(), &mut view),
                Status::OK,
                "{}",
                last_error()
            );
            let attachment = ColorAttachment {
                view,
                resolve_view: TextureViewHandle::NULL,
                load_op: 1,
                store_op: 0,
                clear_color: [0.0; 4],
            };
            let pass_desc = RenderPassDesc {
                color_attachments: &attachment,
                color_attachment_count: 1,
                depth_stencil: ptr::null(),
                timestamp_writes: ptr::null(),
            };
            let render_encoder = encoder(device.handle);
            let mut render = RenderPassHandle::NULL;
            assert_eq!(
                encoder_begin_render_pass(render_encoder, &pass_desc, &mut render),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                render_pass_set_viewport(render, 0.0, 0.0, 64.0, 32.0, 0.0, 1.0),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                render_pass_set_blend_constant(render, 0.25, 0.5, 0.75, 1.0),
                Status::OK,
                "{}",
                last_error()
            );
            // Every multi-draw export rejects null buffers before touching core.
            for status in [
                render_pass_multi_draw_indirect(render, BufferHandle::NULL, 0, 0),
                render_pass_multi_draw_indirect_count(
                    render,
                    BufferHandle::NULL,
                    0,
                    BufferHandle::NULL,
                    0,
                    0,
                ),
                render_pass_multi_draw_indexed_indirect_count(
                    render,
                    BufferHandle::NULL,
                    0,
                    BufferHandle::NULL,
                    0,
                    0,
                ),
            ] {
                assert_eq!(status, Status::INVALID_HANDLE, "{}", last_error());
            }
            assert_eq!(
                render_pass_insert_debug_marker(render, c"marker".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                render_pass_push_debug_group(render, c"group".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                render_pass_pop_debug_group(render),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                render_pass_insert_debug_marker(render, ptr::null()),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(render_pass_end(render), Status::OK);
            assert_eq!(
                encoder_insert_debug_marker(render_encoder, c"marker".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_push_debug_group(render_encoder, c"group".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_pop_debug_group(render_encoder),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_insert_debug_marker(render_encoder, ptr::null()),
                Status::INVALID_ARGUMENT
            );
            let mut command = CommandBufferHandle::NULL;
            assert_eq!(
                encoder_finish(render_encoder, &mut command),
                Status::OK,
                "{}",
                last_error()
            );
            let mut index = 0;
            assert_eq!(
                queue_submit(device.handle, command, &mut index),
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

            let compute_encoder = encoder(device.handle);
            let mut compute = ComputePassHandle::NULL;
            assert_eq!(
                encoder_begin_compute_pass(compute_encoder, ptr::null(), &mut compute),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                compute_pass_insert_debug_marker(compute, c"marker".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                compute_pass_push_debug_group(compute, c"group".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                compute_pass_pop_debug_group(compute),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                compute_pass_insert_debug_marker(compute, ptr::null()),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(compute_pass_end(compute), Status::OK);
            assert_eq!(encoder_destroy(compute_encoder), Status::OK);

            let bundle_desc = BundleEncoderDesc {
                color_formats: ptr::null(),
                color_format_count: 0,
                depth_stencil_format: 42,
                depth_read_only: TRUE,
                stencil_read_only: TRUE,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut bundle = BundleEncoderHandle::NULL;
            assert_eq!(
                device_create_bundle_encoder(device.handle, &bundle_desc, &mut bundle),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                bundle_insert_debug_marker(bundle, c"marker".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                bundle_push_debug_group(bundle, c"group".as_ptr()),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                bundle_pop_debug_group(bundle),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                bundle_insert_debug_marker(bundle, ptr::null()),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(bundle_encoder_destroy(bundle), Status::OK);
            assert_eq!(texture_view_destroy(view), Status::OK);
            assert_eq!(texture_destroy(target), Status::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_clear_operations_zero_buffer_contents() {
        // Clearing textures needs the CLEAR_TEXTURE device feature; the buffer
        // clear below needs no feature.
        let Some(device) = TestDevice::with_features(crate::device::gpu_features::CLEAR_TEXTURE)
        else {
            return;
        };
        const SIZE: u64 = 64;
        unsafe {
            let target_desc = BufferDesc {
                size: SIZE,
                usage: (1 << 2) | (1 << 3), // COPY_SRC | COPY_DST
                name: ptr::null(),
            };
            let readback_desc = BufferDesc {
                size: SIZE,
                usage: (1 << 0) | (1 << 3), // MAP_READ | COPY_DST
                name: ptr::null(),
            };
            let mut target = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &target_desc, &mut target),
                Status::OK,
                "{}",
                last_error()
            );
            let mut readback = BufferHandle::NULL;
            assert_eq!(
                device_create_buffer(device.handle, &readback_desc, &mut readback),
                Status::OK,
                "{}",
                last_error()
            );
            let pattern = [0x5Au8; SIZE as usize];
            let read_contents = |readback: BufferHandle| {
                assert_eq!(
                    buffer_map_read(readback, 0, SIZE),
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
                assert_eq!(buffer_map_poll(readback), Status::OK, "{}", last_error());
                let mut mapped = std::ptr::null();
                assert_eq!(
                    buffer_get_mapped_range(readback, 0, SIZE, &mut mapped),
                    Status::OK,
                    "{}",
                    last_error()
                );
                let bytes = std::slice::from_raw_parts(mapped, SIZE as usize);
                let copy = bytes.to_vec();
                assert_eq!(buffer_unmap(readback), Status::OK);
                copy
            };
            let run = |encoder: EncoderHandle| {
                let mut command = CommandBufferHandle::NULL;
                assert_eq!(
                    encoder_finish(encoder, &mut command),
                    Status::OK,
                    "{}",
                    last_error()
                );
                let mut index = 0;
                assert_eq!(
                    queue_submit(device.handle, command, &mut index),
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
            };
            // Whole-buffer clear: size zero clears from the offset to the end.
            assert_eq!(
                queue_write_buffer(device.handle, target, 0, pattern.as_ptr(), SIZE as u32),
                Status::OK,
                "{}",
                last_error()
            );
            let whole_encoder = encoder(device.handle);
            assert_eq!(
                encoder_clear_buffer(whole_encoder, target, 0, 0),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_copy_buffer_to_buffer(whole_encoder, target, 0, readback, 0, SIZE),
                Status::OK,
                "{}",
                last_error()
            );
            run(whole_encoder);
            let whole_contents = read_contents(readback);
            assert!(whole_contents.iter().all(|&b| b == 0));

            // Partial clear: only bytes 8..16 are zeroed.
            assert_eq!(
                queue_write_buffer(device.handle, target, 0, pattern.as_ptr(), SIZE as u32),
                Status::OK,
                "{}",
                last_error()
            );
            let partial_encoder = encoder(device.handle);
            assert_eq!(
                encoder_clear_buffer(partial_encoder, target, 8, 8),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                encoder_copy_buffer_to_buffer(partial_encoder, target, 0, readback, 0, SIZE),
                Status::OK,
                "{}",
                last_error()
            );
            run(partial_encoder);
            let contents = read_contents(readback);
            for (element, &byte) in contents.iter().enumerate() {
                let expected = if (8..16).contains(&element) { 0 } else { 0x5A };
                assert_eq!(byte, expected, "element {element}");
            }

            // Clearing a texture with COPY_DST usage records and executes.
            let texture_desc = TextureDesc {
                dimension: 1,
                format: 18,
                usage: (1 << 0) | (1 << 1), // COPY_SRC | COPY_DST
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
            let texture_encoder = encoder(device.handle);
            let whole_range = SubresourceRange {
                aspect: 0,
                base_mip_level: 0,
                mip_level_count: 0,
                base_array_layer: 0,
                array_layer_count: 0,
            };
            assert_eq!(
                encoder_clear_texture(texture_encoder, texture, whole_range),
                Status::OK,
                "{}",
                last_error()
            );
            run(texture_encoder);
            assert_eq!(texture_destroy(texture), Status::OK);
            assert_eq!(buffer_destroy(target), Status::OK);
            assert_eq!(buffer_destroy(readback), Status::OK);
        }
    }

    #[test]
    fn vulkan_pass_errors_preserve_distinct_attachment_root_causes() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let texture_desc = TextureDesc {
                dimension: 1,
                format: 42,
                usage: 1 << 5,
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
            let mut messages = Vec::new();
            for (clear, stencil_load, stencil_store, expected) in [
                (1.0, 0, 0, "without stencil aspect"),
                (2.0, NONE, NONE, "must be between 0.0 and 1.0"),
            ] {
                let encoder = encoder(device.handle);
                let attachment = DepthStencilAttachment {
                    view,
                    depth_load_op: 1,
                    depth_store_op: 0,
                    depth_clear: clear,
                    stencil_load_op: stencil_load,
                    stencil_store_op: stencil_store,
                    stencil_clear: 0,
                };
                let pass_desc = RenderPassDesc {
                    color_attachments: ptr::null(),
                    color_attachment_count: 0,
                    depth_stencil: &attachment,
                    timestamp_writes: ptr::null(),
                };
                let mut pass = RenderPassHandle::NULL;
                assert_eq!(
                    encoder_begin_render_pass(encoder, &pass_desc, &mut pass),
                    Status::OK,
                    "{}",
                    last_error()
                );
                assert_eq!(render_pass_end(pass), Status::OK);
                let mut command = CommandBufferHandle::NULL;
                assert_eq!(encoder_finish(encoder, &mut command), Status::VALIDATION);
                let message = last_error();
                assert!(message.contains("In a pass parameter"), "{message}");
                assert!(message.contains("Caused by:"), "{message}");
                assert!(message.contains(expected), "{message}");
                if stencil_load != NONE {
                    assert!(message.contains("Depth32Float"), "{message}");
                    assert!(
                        message.contains("LoadOp") && message.contains("StoreOp"),
                        "{message}"
                    );
                }
                messages.push(message);
                assert!(command.is_null());
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
            }
            assert_ne!(messages[0], messages[1]);
            assert_eq!(texture_view_destroy(view), Status::OK);
            assert_eq!(texture_destroy(texture), Status::OK);
        }
    }

    #[test]
    fn vulkan_empty_texture_writes_preserve_validation() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let desc = TextureDesc {
                dimension: 1,
                format: 18,
                usage: 1 << 1,
                width: 1,
                height: 1,
                depth_or_array_layers: 1,
                mip_level_count: 1,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut texture = TextureHandle::NULL;
            assert_eq!(
                device_create_texture(device.handle, &desc, &mut texture),
                Status::OK,
                "{}",
                last_error()
            );
            let layout = CopyLayout {
                offset: 0,
                bytes_per_row: NONE,
                rows_per_image: NONE,
            };
            let write = |texture, data, data_size, width| {
                queue_write_texture(
                    device.handle,
                    texture,
                    0,
                    Origin3D { x: 0, y: 0, z: 0 },
                    0,
                    data,
                    data_size,
                    &layout,
                    Extent3D {
                        width,
                        height: 1,
                        depth_or_array_layers: 1,
                    },
                )
            };
            let bytes = [0u8; 4];
            for data in [ptr::null(), bytes.as_ptr()] {
                assert_eq!(write(texture, data, 0, 0), Status::OK, "{}", last_error());
                assert_eq!(
                    write(texture, data, 0, 1),
                    Status::VALIDATION,
                    "{}",
                    last_error()
                );
            }
            assert_eq!(write(texture, ptr::null(), 4, 0), Status::INVALID_ARGUMENT);
            assert_eq!(
                write(TextureHandle::NULL, ptr::null(), 0, 0),
                Status::INVALID_HANDLE
            );
            assert_eq!(texture_destroy(texture), Status::OK);
        }
    }

    #[test]
    fn vulkan_empty_immediate_setters_accept_null_and_reject_nonempty_null() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            unsafe fn check_immediates<T>(
                handle: Handle<T>,
                set: unsafe extern "C-unwind" fn(Handle<T>, u32, *const u8, u32) -> Status,
            ) {
                let bytes = [0u8; 4];
                assert_eq!(set(handle, 0, ptr::null(), 4), Status::INVALID_ARGUMENT);
                assert_eq!(set(Handle::NULL, 0, ptr::null(), 0), Status::INVALID_HANDLE);
                for data in [ptr::null(), bytes.as_ptr()] {
                    assert_eq!(set(handle, 0, data, 0), Status::OK, "{}", last_error());
                }
            }
            let bytes = [0u8; 4];
            let render_encoder = encoder(device.handle);
            let desc = RenderPassDesc {
                color_attachments: ptr::null(),
                color_attachment_count: 0,
                depth_stencil: ptr::null(),
                timestamp_writes: ptr::null(),
            };
            let mut render = RenderPassHandle::NULL;
            assert_eq!(
                encoder_begin_render_pass(render_encoder, &desc, &mut render),
                Status::OK,
                "{}",
                last_error()
            );
            check_immediates(render, render_pass_set_immediates);
            assert_eq!(render_pass_end(render), Status::OK, "{}", last_error());
            assert_eq!(encoder_destroy(render_encoder), Status::OK);

            let compute_encoder = encoder(device.handle);
            let mut compute = ComputePassHandle::NULL;
            assert_eq!(
                encoder_begin_compute_pass(compute_encoder, ptr::null(), &mut compute),
                Status::OK,
                "{}",
                last_error()
            );
            check_immediates(compute, compute_pass_set_immediates);
            assert_eq!(compute_pass_end(compute), Status::OK, "{}", last_error());
            assert_eq!(encoder_destroy(compute_encoder), Status::OK);
            let desc = BundleEncoderDesc {
                color_formats: ptr::null(),
                color_format_count: 0,
                depth_stencil_format: 42,
                depth_read_only: TRUE,
                stencil_read_only: TRUE,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut bundle = BundleEncoderHandle::NULL;
            assert_eq!(
                device_create_bundle_encoder(device.handle, &desc, &mut bundle),
                Status::OK,
                "{}",
                last_error()
            );
            assert_eq!(
                bundle_set_immediates(bundle, 0, ptr::null(), 4),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(
                bundle_set_immediates(BundleEncoderHandle::NULL, 0, ptr::null(), 0),
                Status::INVALID_HANDLE
            );
            for data in [ptr::null(), bytes.as_ptr()] {
                assert_eq!(
                    bundle_set_immediates(bundle, 0, data, 0),
                    Status::OK,
                    "{}",
                    last_error()
                );
            }
            assert_eq!(bundle_encoder_destroy(bundle), Status::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_pass_release_abandons_without_end_or_validation() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let ctx = &device.handle.get().unwrap().ctx;
            let baseline = Arc::strong_count(ctx);
            let render_encoder = encoder(device.handle);
            let desc = RenderPassDesc {
                color_attachments: ptr::null(),
                color_attachment_count: 0,
                depth_stencil: ptr::null(),
                timestamp_writes: ptr::null(),
            };
            let mut render = RenderPassHandle::NULL;
            assert_eq!(
                encoder_begin_render_pass(render_encoder, &desc, &mut render),
                Status::OK
            );
            assert_eq!(Arc::strong_count(ctx), baseline + 2);
            assert_eq!(render_pass_release(render), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            let mut command = CommandBufferHandle::NULL;
            // Release does not append the pass or unlock its parent encoder.
            assert_eq!(
                encoder_finish(render_encoder, &mut command),
                Status::VALIDATION
            );
            assert!(command.is_null());
            assert!(last_error().contains("locked"), "{}", last_error());
            assert_eq!(Arc::strong_count(ctx), baseline);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));

            let compute_encoder = encoder(device.handle);
            let mut compute = ComputePassHandle::NULL;
            assert_eq!(
                encoder_begin_compute_pass(compute_encoder, ptr::null(), &mut compute),
                Status::OK
            );
            assert_eq!(
                compute_pass_dispatch_workgroups(compute, 1, 1, 1),
                Status::OK
            );
            assert_eq!(compute_pass_release(compute), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(
                encoder_finish(compute_encoder, &mut command),
                Status::VALIDATION
            );
            assert!(command.is_null());
            assert_eq!(Arc::strong_count(ctx), baseline);
            let report = ctx.global.generate_report().hub;
            assert_eq!(report.render_passes.num_allocated, 0);
            assert_eq!(report.compute_passes.num_allocated, 0);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_pass_end_consumes_wrapper_and_context_reference() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            unsafe fn finish_core_while_pass_open(ctx: &DeviceCtx, encoder: EncoderHandle) {
                // Inject a core state failure without dereferencing a freed ABI wrapper.
                let desc = wgt::CommandBufferDescriptor { label: None };
                let (id, error) =
                    ctx.global
                        .command_encoder_finish(encoder.get().unwrap().id, &desc, None);
                assert!(error.is_some());
                ctx.global.command_buffer_drop(id);
            }
            let ctx = &device.handle.get().unwrap().ctx;
            let baseline = Arc::strong_count(ctx);
            let render_encoder = encoder(device.handle);
            let desc = RenderPassDesc {
                color_attachments: ptr::null(),
                color_attachment_count: 0,
                depth_stencil: ptr::null(),
                timestamp_writes: ptr::null(),
            };
            let mut render = RenderPassHandle::NULL;
            assert_eq!(
                encoder_begin_render_pass(render_encoder, &desc, &mut render),
                Status::OK
            );
            finish_core_while_pass_open(ctx, render_encoder);
            assert_eq!(render_pass_end(render), Status::VALIDATION);
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(encoder_destroy(render_encoder), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);

            let compute_encoder = encoder(device.handle);
            let mut compute = ComputePassHandle::NULL;
            assert_eq!(
                encoder_begin_compute_pass(compute_encoder, ptr::null(), &mut compute),
                Status::OK
            );
            finish_core_while_pass_open(ctx, compute_encoder);
            assert_eq!(compute_pass_end(compute), Status::VALIDATION);
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(encoder_destroy(compute_encoder), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_bundle_finish_and_destroy_release_core_and_wrapper_ownership() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let ctx = &device.handle.get().unwrap().ctx;
            let baseline = Arc::strong_count(ctx);
            let formats = [18];
            let desc = BundleEncoderDesc {
                color_formats: formats.as_ptr(),
                color_format_count: 1,
                depth_stencil_format: NONE,
                depth_read_only: TRUE,
                stencil_read_only: TRUE,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut recording = BundleEncoderHandle::NULL;
            let mut bundle = RenderBundleHandle::NULL;
            for fail in [false, true] {
                assert_eq!(
                    device_create_bundle_encoder(device.handle, &desc, &mut recording),
                    Status::OK
                );
                assert_eq!(Arc::strong_count(ctx), baseline + 1);
                assert_eq!(
                    bundle_encoder_finish(recording, ptr::null_mut()),
                    Status::INVALID_ARGUMENT
                );
                assert_eq!(Arc::strong_count(ctx), baseline + 1);
                if fail {
                    // Recording accepts the draw; Finish rejects its missing pipeline.
                    assert_eq!(bundle_draw(recording, 3, 1, 0, 0), Status::OK);
                }
                assert_eq!(
                    bundle_encoder_finish(recording, &mut bundle),
                    if fail { Status::VALIDATION } else { Status::OK },
                    "{}",
                    last_error()
                );
                if fail {
                    assert!(bundle.is_null());
                } else {
                    let report = ctx.global.generate_report().hub.render_bundles;
                    assert_eq!(report.num_allocated, 1);
                    assert_eq!(report.num_kept_from_user, 1);
                    assert_eq!(Arc::strong_count(ctx), baseline + 1);
                    assert_eq!(render_bundle_destroy(bundle), Status::OK);
                }
                assert_eq!(Arc::strong_count(ctx), baseline);
                let report = ctx.global.generate_report().hub;
                assert_eq!(report.render_bundles.num_allocated, 0);
                assert_eq!(report.render_bundle_encoders.num_allocated, 0);
            }
            assert_eq!(
                device_create_bundle_encoder(device.handle, &desc, &mut recording),
                Status::OK
            );
            assert_eq!(bundle_encoder_destroy(recording), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);

            let open = encoder(device.handle);
            assert_eq!(
                encoder_finish(open, ptr::null_mut()),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(Arc::strong_count(ctx), baseline + 1);
            assert_eq!(encoder_destroy(open), Status::OK);
            let open = encoder(device.handle);
            let command = finish(open);
            assert_eq!(command_buffer_destroy(command), Status::OK);
            assert_eq!(Arc::strong_count(ctx), baseline);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_bundle_execution_preserves_inline_and_overflow_handle_arrays() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let formats = [18];
            let desc = BundleEncoderDesc {
                color_formats: formats.as_ptr(),
                color_format_count: 1,
                depth_stencil_format: NONE,
                depth_read_only: TRUE,
                stencil_read_only: TRUE,
                sample_count: 1,
                name: ptr::null(),
            };
            let mut recording = BundleEncoderHandle::NULL;
            let mut bundle = RenderBundleHandle::NULL;
            assert_eq!(
                device_create_bundle_encoder(device.handle, &desc, &mut recording),
                Status::OK
            );
            assert_eq!(bundle_encoder_finish(recording, &mut bundle), Status::OK);
            let open = encoder(device.handle);
            let desc = RenderPassDesc {
                color_attachments: ptr::null(),
                color_attachment_count: 0,
                depth_stencil: ptr::null(),
                timestamp_writes: ptr::null(),
            };
            let mut pass = RenderPassHandle::NULL;
            assert_eq!(
                encoder_begin_render_pass(open, &desc, &mut pass),
                Status::OK
            );
            assert_eq!(
                render_pass_execute_bundles(pass, ptr::null(), 0),
                Status::OK
            );
            assert_eq!(
                render_pass_execute_bundles(pass, ptr::null(), 1),
                Status::INVALID_ARGUMENT
            );
            let mut bundles = [bundle; 9];
            for count in [1, 4, 5, 9] {
                assert_eq!(
                    render_pass_execute_bundles(pass, bundles.as_ptr(), count),
                    Status::OK
                );
            }
            bundles[8] = RenderBundleHandle::NULL;
            assert_eq!(
                render_pass_execute_bundles(pass, bundles.as_ptr(), 9),
                Status::INVALID_HANDLE
            );
            assert_eq!(render_pass_release(pass), Status::OK);
            assert_eq!(encoder_destroy(open), Status::OK);
            assert_eq!(render_bundle_destroy(bundle), Status::OK);
            assert_eq!(
                device
                    .handle
                    .get()
                    .unwrap()
                    .ctx
                    .global
                    .generate_report()
                    .hub
                    .render_bundles
                    .num_allocated,
                0
            );
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_encoder_creation_releases_core_registry_entries() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let ctx = &device.handle.get().unwrap().ctx;
            // Lose the core device while keeping the ABI context valid for retries.
            ctx.global.device_destroy(ctx.device_id);
            for _ in 0..32 {
                let mut handle = EncoderHandle::NULL;
                assert_eq!(
                    device_create_encoder(device.handle, ptr::null(), &mut handle),
                    Status::OUT_OF_MEMORY,
                    "{}",
                    last_error()
                );
                assert!(last_error().contains("device is lost"), "{}", last_error());
                assert!(handle.is_null());
                let report = ctx.global.generate_report().hub.command_encoders;
                assert_eq!(report.num_allocated, 0, "{report:?}");
                assert_eq!(report.num_kept_from_user, 0, "{report:?}");
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
            }
        }
    }

    #[test]
    fn vulkan_readback_submission_retains_work_without_retaining_registration() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let source = buffer(device.handle, (1 << 2) | (1 << 3));
            let destination = buffer(device.handle, (1 << 0) | (1 << 3));
            let bytes = [3, 19, 127, 254];
            assert_eq!(
                queue_write_buffer(device.handle, source, 0, bytes.as_ptr(), 4),
                Status::OK
            );
            for data in [ptr::null(), bytes.as_ptr()] {
                for offset in [0, 4] {
                    assert_eq!(
                        queue_write_buffer(device.handle, source, offset, data, 0),
                        Status::OK,
                        "{}",
                        last_error()
                    );
                }
            }
            assert_eq!(
                queue_write_buffer(device.handle, source, 0, ptr::null(), 4),
                Status::INVALID_ARGUMENT
            );
            assert_eq!(
                queue_write_buffer(device.handle, BufferHandle::NULL, 0, ptr::null(), 0),
                Status::INVALID_HANDLE
            );
            for offset in [1, 8] {
                assert_eq!(
                    queue_write_buffer(device.handle, source, offset, ptr::null(), 0),
                    Status::VALIDATION,
                    "{}",
                    last_error()
                );
            }
            let encoder = encoder(device.handle);
            assert_eq!(
                encoder_copy_buffer_to_buffer(encoder, source, 0, destination, 0, 4),
                Status::OK
            );
            let command = finish(encoder);
            let mut index = 0;
            assert_eq!(queue_submit(device.handle, command, &mut index), Status::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(buffer_map_read(destination, 0, 4), Status::OK);
            assert_eq!(
                device_poll(device.handle, TRUE, index, ptr::null_mut()),
                Status::OK
            );
            assert_eq!(buffer_map_poll(destination), Status::OK);
            let mut mapped = ptr::null();
            assert_eq!(
                buffer_get_mapped_range(destination, 0, 4, &mut mapped),
                Status::OK
            );
            assert_eq!(std::slice::from_raw_parts(mapped, 4), &bytes);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(buffer_unmap(destination), Status::OK);
            assert_eq!(buffer_destroy(destination), Status::OK);
            assert_eq!(buffer_destroy(source), Status::OK);
        }
    }
}
