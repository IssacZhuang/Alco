//! Command encoding: encoders, render/compute passes, copies, query
//! resolution, queue writes/submission and render bundles.
//!
//! wgpu-core 30 records passes by value (`RenderPass`/`ComputePass` live in
//! per-device handle tables while open); `*_end` consumes the handle and
//! appends the recorded pass to its parent encoder. `alco_queue_submit`
//! consumes the command-buffer handle, mirroring the old backend's
//! take-buffer-on-submit semantics.

use crate::abi::*;
use crate::convert::*;
use crate::device::{DeviceCtx, DEVICES};
use crate::entry::{set_error, set_error_from};
use crate::handle::{HandleTable, RecordingTable};
use crate::objects::label;
use std::ffi::c_char;
use wgpu_core as wgc;
use wgpu_types as wgt;

#[derive(Clone, Copy)]
pub(crate) struct EncoderObj {
    pub id: wgc::id::CommandEncoderId,
}

#[derive(Clone, Copy)]
pub(crate) struct CommandBufferObj {
    pub id: wgc::id::CommandBufferId,
}

pub(crate) struct RenderPassObj {
    pub pass: wgc::command::RenderPass,
}

pub(crate) struct ComputePassObj {
    pub pass: wgc::command::ComputePass,
}

pub(crate) struct BundleEncoderObj {
    pub encoder: Box<wgc::command::RenderBundleEncoder>,
}

#[derive(Clone, Copy)]
pub(crate) struct RenderBundleObj {
    pub id: wgc::id::RenderBundleId,
}

/// Per-device handle tables for command recording and submission objects.
#[derive(Default)]
pub(crate) struct CommandTables {
    pub encoders: HandleTable<EncoderObj>,
    pub command_buffers: HandleTable<CommandBufferObj>,
    pub render_passes: RecordingTable<RenderPassObj>,
    pub compute_passes: RecordingTable<ComputePassObj>,
    pub bundle_encoders: RecordingTable<BundleEncoderObj>,
    pub render_bundles: HandleTable<RenderBundleObj>,
}

impl DeviceCtx {
    pub fn encoders(&self) -> &HandleTable<EncoderObj> {
        &self.commands.encoders
    }
    pub fn command_buffers(&self) -> &HandleTable<CommandBufferObj> {
        &self.commands.command_buffers
    }
    pub fn render_passes(&self) -> &RecordingTable<RenderPassObj> {
        &self.commands.render_passes
    }
    pub fn compute_passes(&self) -> &RecordingTable<ComputePassObj> {
        &self.commands.compute_passes
    }
    pub fn bundle_encoders(&self) -> &RecordingTable<BundleEncoderObj> {
        &self.commands.bundle_encoders
    }
    pub fn render_bundles(&self) -> &HandleTable<RenderBundleObj> {
        &self.commands.render_bundles
    }
}

/// Runs a fallible body with the device context; the body reports failures as
/// `Err(status)` (after recording a message), enabling `?` on lookups.
pub(crate) fn run_with_device(
    device: AlcoHandle,
    body: impl FnOnce(&DeviceCtx) -> Result<(), AlcoStatus>,
) -> AlcoStatus {
    match DEVICES.with(device, |ctx| body(ctx)) {
        Ok(Ok(())) => AlcoStatus::OK,
        Ok(Err(status)) => status,
        Err(s) => {
            set_error(s, "invalid device handle");
            s
        }
    }
}

// ---------------------------------------------------------------------------
// ABI structs
// ---------------------------------------------------------------------------

/// One color attachment of a render pass.
#[repr(C)]
pub struct AlcoColorAttachment {
    pub view: AlcoHandle,
    /// Resolve target view, `AlcoHandle::NULL` when unused.
    pub resolve_view: AlcoHandle,
    /// C# `AttachmentLoadOp`: 0 Load, 1 Clear.
    pub load_op: u32,
    /// C# `AttachmentStoreOp`: 0 Store, 1 Discard.
    pub store_op: u32,
    pub clear_color: [f32; 4],
}

/// Depth-stencil attachment of a render pass. A load/store op value of
/// `ALCO_NONE` marks the channel read-only (wgpu `PassChannel` with no ops).
#[repr(C)]
pub struct AlcoDepthStencilAttachment {
    pub view: AlcoHandle,
    pub depth_load_op: u32,
    pub depth_store_op: u32,
    pub depth_clear: f32,
    pub stencil_load_op: u32,
    pub stencil_store_op: u32,
    pub stencil_clear: u32,
}

#[repr(C)]
pub struct AlcoTimestampWrites {
    pub query_set: AlcoHandle,
    /// `ALCO_NONE` when not written.
    pub beginning_index: u32,
    pub end_index: u32,
}

#[repr(C)]
pub struct AlcoRenderPassDesc {
    pub color_attachments: *const AlcoColorAttachment,
    pub color_attachment_count: u32,
    /// Null when the pass has no depth-stencil attachment.
    pub depth_stencil: *const AlcoDepthStencilAttachment,
    /// Null when the pass writes no timestamps.
    pub timestamp_writes: *const AlcoTimestampWrites,
}

/// Full-mip source layout for texture copies/writes (computed C#-side, which
/// owns the row-pitch rules per format).
#[repr(C)]
pub struct AlcoCopyLayout {
    pub offset: u64,
    /// `ALCO_NONE` when not required (single row).
    pub bytes_per_row: u32,
    pub rows_per_image: u32,
}

#[repr(C)]
pub struct AlcoOrigin3D {
    pub x: u32,
    pub y: u32,
    pub z: u32,
}

#[repr(C)]
pub struct AlcoExtent3D {
    pub width: u32,
    pub height: u32,
    pub depth_or_array_layers: u32,
}

// ---------------------------------------------------------------------------
// Encoder lifecycle
// ---------------------------------------------------------------------------

/// ABI: creates a command encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_encoder_create(
    device: AlcoHandle,
    name: *const c_char,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let wdesc = wgt::CommandEncoderDescriptor {
                    label: label(name),
                };
                let (id, err) = ctx
                    .global
                    .device_create_command_encoder(ctx.device_id, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::OUT_OF_MEMORY, &e);
                    ctx.global.command_encoder_drop(id);
                    return (AlcoStatus::OUT_OF_MEMORY, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.encoders().insert(EncoderObj { id }))
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

/// ABI: finishes an encoder into a command buffer. The encoder handle is
/// consumed (the wgpu encoder object is dropped — recording is complete).
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_encoder_finish(
    device: AlcoHandle,
    encoder: AlcoHandle,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let encoder_obj = match ctx.encoders().remove(encoder) {
                    Ok(obj) => obj,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid encoder handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };
                let wdesc = wgt::CommandBufferDescriptor { label: None };
                let (id, err) = ctx
                    .global
                    .command_encoder_finish(encoder_obj.id, &wdesc, None);
                ctx.global.command_encoder_drop(encoder_obj.id);
                if let Some((_, e)) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.command_buffer_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.command_buffers().insert(CommandBufferObj { id }))
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

/// ABI: destroys an encoder that will never be finished.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_encoder_destroy(device: AlcoHandle, encoder: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.encoders().remove(encoder) {
                Ok(obj) => {
                    ctx.global.command_encoder_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid encoder handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a command buffer that was never submitted.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_command_buffer_destroy(
    device: AlcoHandle,
    command_buffer: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.command_buffers().remove(command_buffer) {
                Ok(obj) => {
                    ctx.global.command_buffer_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid command buffer handle");
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
// Render pass
// ---------------------------------------------------------------------------

/// ABI: begins a render pass. On begin failure no pass handle is produced.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_render_pass_begin(
    device: AlcoHandle,
    encoder: AlcoHandle,
    desc: *const AlcoRenderPassDesc,
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
                let encoder_id = match ctx.encoders().with(encoder, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid encoder handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };

                let mut attachments = Vec::new();
                if desc.color_attachment_count > 0 {
                    if desc.color_attachments.is_null() {
                        set_error(AlcoStatus::INVALID_ARGUMENT, "null color attachment array");
                        return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
                    }
                    let raw = std::slice::from_raw_parts(
                        desc.color_attachments,
                        desc.color_attachment_count as usize,
                    );
                    for attachment in raw {
                        let view = match ctx.views().with(attachment.view, |obj| obj.id) {
                            Ok(id) => id,
                            Err(_) => {
                                set_error(AlcoStatus::INVALID_HANDLE, "invalid texture view handle in color attachment");
                                return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                            }
                        };
                        let resolve = if attachment.resolve_view.is_null() {
                            None
                        } else {
                            match ctx.views().with(attachment.resolve_view, |obj| obj.id) {
                                Ok(id) => Some(id),
                                Err(_) => {
                                    set_error(AlcoStatus::INVALID_HANDLE, "invalid resolve view handle in color attachment");
                                    return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                                }
                            }
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
                                set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid color load op {other}"));
                                return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
                            }
                        };
                        let store_op = match store_op(attachment.store_op) {
                            Ok(op) => op,
                            Err(s) => return (s, AlcoHandle::NULL),
                        };
                        attachments.push(Some(wgc::command::RenderPassColorAttachment {
                            view,
                            depth_slice: None,
                            resolve_target: resolve,
                            load_op,
                            store_op,
                        }));
                    }
                }

                let depth_stencil = match desc.depth_stencil.as_ref() {
                    Some(d) => {
                        let view = match ctx.views().with(d.view, |obj| obj.id) {
                            Ok(id) => id,
                            Err(_) => {
                                set_error(AlcoStatus::INVALID_HANDLE, "invalid texture view handle in depth attachment");
                                return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                            }
                        };
                        let depth = match pass_channel(
                            d.depth_load_op,
                            d.depth_store_op,
                            d.depth_clear,
                            "depth",
                        ) {
                            Ok(c) => c,
                            Err(status) => return (status, AlcoHandle::NULL),
                        };
                        let stencil = match pass_channel_u32(
                            d.stencil_load_op,
                            d.stencil_store_op,
                            d.stencil_clear,
                            "stencil",
                        ) {
                            Ok(c) => c,
                            Err(status) => return (status, AlcoHandle::NULL),
                        };
                        Some(wgc::command::RenderPassDepthStencilAttachment {
                            view,
                            depth,
                            stencil,
                        })
                    }
                    None => None,
                };

                let timestamp_writes = match desc.timestamp_writes.as_ref() {
                    Some(t) => {
                        let query_set = match ctx.query_sets().with(t.query_set, |obj| obj.id) {
                            Ok(id) => id,
                            Err(_) => {
                                set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle in timestamp writes");
                                return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                            }
                        };
                        Some(wgc::command::PassTimestampWrites {
                            query_set,
                            beginning_of_pass_write_index: optional_index(t.beginning_index),
                            end_of_pass_write_index: optional_index(t.end_index),
                        })
                    }
                    None => None,
                };

                let wdesc = wgc::command::RenderPassDescriptor {
                    label: None,
                    color_attachments: std::borrow::Cow::Owned(attachments),
                    depth_stencil_attachment: depth_stencil,
                    timestamp_writes,
                    occlusion_query_set: None,
                    multiview_mask: None,
                };

                let (pass, err) = ctx
                    .global
                    .command_encoder_begin_render_pass(encoder_id, &wdesc);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    drop(pass);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.render_passes().insert(RenderPassObj { pass }))
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

fn optional_index(v: u32) -> Option<u32> {
    if v == ALCO_NONE {
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
) -> Result<wgc::command::PassChannel<Option<f32>>, AlcoStatus> {
    let read_only = load_op == ALCO_NONE && store == ALCO_NONE;
    let load = if read_only {
        None
    } else {
        Some(match load_op {
            0 => wgt::LoadOp::Load,
            1 => wgt::LoadOp::Clear(Some(clear)),
            other => {
                set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid {what} load op {other}"));
                return Err(AlcoStatus::INVALID_ARGUMENT);
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
) -> Result<wgc::command::PassChannel<Option<u32>>, AlcoStatus> {
    let read_only = load_op == ALCO_NONE && store == ALCO_NONE;
    let load = if read_only {
        None
    } else {
        Some(match load_op {
            0 => wgt::LoadOp::Load,
            1 => wgt::LoadOp::Clear(Some(clear)),
            other => {
                set_error(AlcoStatus::INVALID_ARGUMENT, format!("invalid {what} load op {other}"));
                return Err(AlcoStatus::INVALID_ARGUMENT);
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

/// ABI: ends a render pass, consuming the handle unless overlapping access is rejected.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_render_pass_end(device: AlcoHandle, pass: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let mut obj = match ctx.render_passes().remove(pass) {
                    Ok(obj) => obj,
                    Err(status) => return recording_error(status, "render pass"),
                };
                match ctx.global.render_pass_end(&mut obj.pass) {
                    Ok(()) => AlcoStatus::OK,
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

macro_rules! render_pass_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(device: AlcoHandle, pass: AlcoHandle, $($arg: $ty),*) -> AlcoStatus {
            crate::entry::guard(|| {
                DEVICES
                    .with(device, |ctx| {
                        let result = ctx.render_passes().with(pass, |obj| {
                            type Body = fn(&DeviceCtx, &mut wgc::command::RenderPass, $($ty),*) -> Result<(), AlcoStatus>;
                            let body: Body = $body;
                            body(ctx, &mut obj.pass $(, $arg)*)
                        });
                        match result {
                            Ok(Ok(())) => AlcoStatus::OK,
                            Ok(Err(status)) => status,
                            Err(status) => recording_error(status, "render pass"),
                        }
                    })
                    .unwrap_or_else(|s| {
                        set_error(s, "invalid device handle");
                        s
                    })
            })
        }
    };
}

fn recording_error(status: AlcoStatus, kind: &str) -> AlcoStatus {
    if status == AlcoStatus::INVALID_ARGUMENT {
        set_error(status, format!("concurrent access to {kind}"));
    } else {
        set_error(status, format!("invalid {kind} handle"));
    }
    status
}

fn record_result(
    result: Result<(), impl std::error::Error>,
) -> Result<(), AlcoStatus> {
    match result {
        Ok(()) => Ok(()),
        Err(e) => {
            set_error_from(AlcoStatus::VALIDATION, &e);
            Err(AlcoStatus::VALIDATION)
        }
    }
}

render_pass_fn!(
    /// ABI: sets the render pipeline.
    alco_render_pass_set_pipeline(pipeline: AlcoHandle) |ctx, pass, pipeline| {
        match ctx.pipelines().with(pipeline, |obj| match obj {
            crate::pipeline::PipelineObj::Graphics(id) => Some(*id),
            crate::pipeline::PipelineObj::Compute(_) => None,
        }) {
            Ok(Some(id)) => record_result(ctx.global.render_pass_set_pipeline(pass, id)),
            Ok(None) => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "pipeline is not a graphics pipeline");
                Err(AlcoStatus::INVALID_ARGUMENT)
            }
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid graphics pipeline handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: binds a resource group (bind group) at `slot`.
    alco_render_pass_set_bind_group(slot: u32, group: AlcoHandle) |ctx, pass, slot, group| {
        let id = ctx.bind_groups().with(group, |obj| obj.id);
        match id {
            Ok(id) => record_result(ctx.global.render_pass_set_bind_group(pass, slot, Some(id), &[])),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: binds a vertex buffer slot.
    alco_render_pass_set_vertex_buffer(slot: u32, buffer: AlcoHandle, offset: u64, size: u64) |ctx, pass, slot, buffer, offset, size| {
        let id = ctx.buffers().with(buffer, |obj| obj.id);
        match id {
            Ok(id) => record_result(ctx.global.render_pass_set_vertex_buffer(
                pass,
                slot,
                Some(id),
                offset,
                nonzero_size(size),
            )),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: binds the index buffer.
    alco_render_pass_set_index_buffer(buffer: AlcoHandle, format: u32, offset: u64, size: u64) |ctx, pass, buffer, format, offset, size| {
        let id = ctx.buffers().with(buffer, |obj| obj.id);
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
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
            (_, Err(s)) => Err(s),
        }
    }
);

render_pass_fn!(
    /// ABI: sets the scissor rectangle.
    alco_render_pass_set_scissor_rect(x: u32, y: u32, width: u32, height: u32) |ctx, pass, x, y, width, height| {
        record_result(ctx.global.render_pass_set_scissor_rect(pass, x, y, width, height))
    }
);

render_pass_fn!(
    /// ABI: sets the stencil reference value.
    alco_render_pass_set_stencil_reference(reference: u32) |ctx, pass, reference| {
        record_result(ctx.global.render_pass_set_stencil_reference(pass, reference))
    }
);

render_pass_fn!(
    /// ABI: uploads immediates (push constants) for the graphics stages.
    alco_render_pass_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, pass, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null immediate data");
            return Err(AlcoStatus::INVALID_ARGUMENT);
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
    alco_render_pass_draw(vertex_count: u32, instance_count: u32, first_vertex: u32, first_instance: u32) |ctx, pass, vertex_count, instance_count, first_vertex, first_instance| {
        record_result(ctx.global.render_pass_draw(pass, vertex_count, instance_count, first_vertex, first_instance))
    }
);

render_pass_fn!(
    /// ABI: indexed draw.
    alco_render_pass_draw_indexed(index_count: u32, instance_count: u32, first_index: u32, vertex_offset: i32, first_instance: u32) |ctx, pass, index_count, instance_count, first_index, vertex_offset, first_instance| {
        record_result(ctx.global.render_pass_draw_indexed(pass, index_count, instance_count, first_index, vertex_offset, first_instance))
    }
);

render_pass_fn!(
    /// ABI: indirect non-indexed draw.
    alco_render_pass_draw_indirect(buffer: AlcoHandle, offset: u64) |ctx, pass, buffer, offset| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_pass_draw_indirect(pass, id, offset)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: indirect indexed draw.
    alco_render_pass_draw_indexed_indirect(buffer: AlcoHandle, offset: u64) |ctx, pass, buffer, offset| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_pass_draw_indexed_indirect(pass, id, offset)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: multi-draw indexed indirect.
    alco_render_pass_multi_draw_indexed_indirect(buffer: AlcoHandle, offset: u64, count: u32) |ctx, pass, buffer, offset, count| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_pass_multi_draw_indexed_indirect(pass, id, offset, count)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

render_pass_fn!(
    /// ABI: writes a timestamp inside the pass.
    alco_render_pass_write_timestamp(query_set: AlcoHandle, query_index: u32) |ctx, pass, query_set, query_index| {
        match ctx.query_sets().with(query_set, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_pass_write_timestamp(pass, id, query_index)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

/// ABI: executes render bundles in the open pass.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_render_pass_execute_bundles(
    device: AlcoHandle,
    pass: AlcoHandle,
    bundles: *const AlcoHandle,
    bundle_count: u32,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if bundles.is_null() && bundle_count > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null bundle array");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let result = ctx.render_passes().with(pass, |obj| {
                    let handles = if bundles.is_null() {
                        &[][..]
                    } else {
                        std::slice::from_raw_parts(bundles, bundle_count as usize)
                    };
                    let mut ids = Vec::with_capacity(handles.len());
                    for handle in handles {
                        match ctx.render_bundles().with(*handle, |obj| obj.id) {
                            Ok(id) => ids.push(id),
                            Err(_) => {
                                set_error(AlcoStatus::INVALID_HANDLE, "invalid render bundle handle");
                                return Err(AlcoStatus::INVALID_HANDLE);
                            }
                        }
                    }
                    record_result(ctx.global.render_pass_execute_bundles(&mut obj.pass, &ids))
                });
                match result {
                    Ok(Ok(())) => AlcoStatus::OK,
                    Ok(Err(status)) => status,
                    Err(status) => recording_error(status, "render pass"),
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
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
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_compute_pass_begin(
    device: AlcoHandle,
    encoder: AlcoHandle,
    timestamp_writes: *const AlcoTimestampWrites,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let encoder_id = match ctx.encoders().with(encoder, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid encoder handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };
                let timestamp_writes = match timestamp_writes.as_ref() {
                    Some(t) => {
                        let query_set = match ctx.query_sets().with(t.query_set, |obj| obj.id) {
                            Ok(id) => id,
                            Err(_) => {
                                set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle in timestamp writes");
                                return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                            }
                        };
                        Some(wgc::command::PassTimestampWrites {
                            query_set,
                            beginning_of_pass_write_index: optional_index(t.beginning_index),
                            end_of_pass_write_index: optional_index(t.end_index),
                        })
                    }
                    None => None,
                };
                let wdesc = wgc::command::ComputePassDescriptor {
                    label: None,
                    timestamp_writes,
                };
                let (pass, err) = ctx
                    .global
                    .command_encoder_begin_compute_pass(encoder_id, &wdesc);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    drop(pass);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.compute_passes().insert(ComputePassObj { pass }))
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

/// ABI: ends a compute pass, consuming the handle unless overlapping access is rejected.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_compute_pass_end(device: AlcoHandle, pass: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let mut obj = match ctx.compute_passes().remove(pass) {
                    Ok(obj) => obj,
                    Err(status) => return recording_error(status, "compute pass"),
                };
                match ctx.global.compute_pass_end(&mut obj.pass) {
                    Ok(()) => AlcoStatus::OK,
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

macro_rules! compute_pass_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(device: AlcoHandle, pass: AlcoHandle, $($arg: $ty),*) -> AlcoStatus {
            crate::entry::guard(|| {
                DEVICES
                    .with(device, |ctx| {
                        let result = ctx.compute_passes().with(pass, |obj| {
                            type Body = fn(&DeviceCtx, &mut wgc::command::ComputePass, $($ty),*) -> Result<(), AlcoStatus>;
                            let body: Body = $body;
                            body(ctx, &mut obj.pass $(, $arg)*)
                        });
                        match result {
                            Ok(Ok(())) => AlcoStatus::OK,
                            Ok(Err(status)) => status,
                            Err(status) => recording_error(status, "compute pass"),
                        }
                    })
                    .unwrap_or_else(|s| {
                        set_error(s, "invalid device handle");
                        s
                    })
            })
        }
    };
}

compute_pass_fn!(
    /// ABI: sets the compute pipeline.
    alco_compute_pass_set_pipeline(pipeline: AlcoHandle) |ctx, pass, pipeline| {
        match ctx.pipelines().with(pipeline, |obj| match obj {
            crate::pipeline::PipelineObj::Compute(id) => Some(*id),
            crate::pipeline::PipelineObj::Graphics(_) => None,
        }) {
            Ok(Some(id)) => record_result(ctx.global.compute_pass_set_pipeline(pass, id)),
            Ok(None) => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "pipeline is not a compute pipeline");
                Err(AlcoStatus::INVALID_ARGUMENT)
            }
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid compute pipeline handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: binds a resource group at `slot`.
    alco_compute_pass_set_bind_group(slot: u32, group: AlcoHandle) |ctx, pass, slot, group| {
        let id = ctx.bind_groups().with(group, |obj| obj.id);
        match id {
            Ok(id) => record_result(ctx.global.compute_pass_set_bind_group(pass, slot, Some(id), &[])),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: uploads immediates (push constants) for the compute stage.
    alco_compute_pass_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, pass, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null immediate data");
            return Err(AlcoStatus::INVALID_ARGUMENT);
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
    alco_compute_pass_dispatch_workgroups(x: u32, y: u32, z: u32) |ctx, pass, x, y, z| {
        record_result(ctx.global.compute_pass_dispatch_workgroups(pass, x, y, z))
    }
);

compute_pass_fn!(
    /// ABI: indirect compute dispatch.
    alco_compute_pass_dispatch_workgroups_indirect(buffer: AlcoHandle, offset: u64) |ctx, pass, buffer, offset| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.compute_pass_dispatch_workgroups_indirect(pass, id, offset)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

compute_pass_fn!(
    /// ABI: writes a timestamp inside the pass.
    alco_compute_pass_write_timestamp(query_set: AlcoHandle, query_index: u32) |ctx, pass, query_set, query_index| {
        match ctx.query_sets().with(query_set, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.compute_pass_write_timestamp(pass, id, query_index)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

// ---------------------------------------------------------------------------
// Copies and queries
// ---------------------------------------------------------------------------

/// ABI: buffer-to-buffer copy on the open encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_copy_buffer_to_buffer(
    device: AlcoHandle,
    encoder: AlcoHandle,
    source: AlcoHandle,
    source_offset: u64,
    destination: AlcoHandle,
    destination_offset: u64,
    size: u64,
) -> AlcoStatus {
    crate::entry::guard(|| {
        run_with_device(device, |ctx| {
                let encoder_id = lookup_encoder(ctx, encoder)?;
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

fn lookup_encoder(ctx: &DeviceCtx, handle: AlcoHandle) -> Result<wgc::id::CommandEncoderId, AlcoStatus> {
    ctx.encoders().with(handle, |obj| obj.id).map_err(|_| {
        set_error(AlcoStatus::INVALID_HANDLE, "invalid encoder handle");
        AlcoStatus::INVALID_HANDLE
    })
}

fn lookup_buffer(ctx: &DeviceCtx, handle: AlcoHandle) -> Result<wgc::id::BufferId, AlcoStatus> {
    ctx.buffers().with(handle, |obj| obj.id).map_err(|_| {
        set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
        AlcoStatus::INVALID_HANDLE
    })
}

fn lookup_texture(ctx: &DeviceCtx, handle: AlcoHandle) -> Result<wgc::id::TextureId, AlcoStatus> {
    ctx.textures().with(handle, |obj| obj.id).map_err(|_| {
        set_error(AlcoStatus::INVALID_HANDLE, "invalid texture handle");
        AlcoStatus::INVALID_HANDLE
    })
}

fn copy_layout(layout: &AlcoCopyLayout) -> Result<wgt::TexelCopyBufferLayout, AlcoStatus> {
    Ok(wgt::TexelCopyBufferLayout {
        offset: layout.offset,
        bytes_per_row: optional_index(layout.bytes_per_row),
        rows_per_image: optional_index(layout.rows_per_image),
    })
}

fn copy_texture_info(
    ctx: &DeviceCtx,
    texture: AlcoHandle,
    mip_level: u32,
    origin: AlcoOrigin3D,
    aspect: u32,
) -> Result<wgt::TexelCopyTextureInfo<wgc::id::TextureId>, AlcoStatus> {
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

fn extent3d(extent: AlcoExtent3D) -> wgt::Extent3d {
    wgt::Extent3d {
        width: extent.width,
        height: extent.height,
        depth_or_array_layers: extent.depth_or_array_layers,
    }
}

/// ABI: buffer-to-texture copy on the open encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_copy_buffer_to_texture(
    device: AlcoHandle,
    encoder: AlcoHandle,
    source: AlcoHandle,
    source_layout: *const AlcoCopyLayout,
    destination: AlcoHandle,
    destination_mip_level: u32,
    destination_aspect: u32,
    copy_size: AlcoExtent3D,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let source_layout = match source_layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null source layout");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        run_with_device(device, |ctx| {
                let encoder_id = lookup_encoder(ctx, encoder)?;
                let source = wgc::command::TexelCopyBufferInfo {
                    buffer: lookup_buffer(ctx, source)?,
                    layout: copy_layout(source_layout)?,
                };
                let destination = copy_texture_info(
                    ctx,
                    destination,
                    destination_mip_level,
                    AlcoOrigin3D { x: 0, y: 0, z: 0 },
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

/// ABI: texture-to-buffer copy on the open encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_copy_texture_to_buffer(
    device: AlcoHandle,
    encoder: AlcoHandle,
    source: AlcoHandle,
    source_mip_level: u32,
    source_aspect: u32,
    destination: AlcoHandle,
    destination_layout: *const AlcoCopyLayout,
    copy_size: AlcoExtent3D,
) -> AlcoStatus {
    crate::entry::guard(|| {
        let destination_layout = match destination_layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null destination layout");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        run_with_device(device, |ctx| {
                let encoder_id = lookup_encoder(ctx, encoder)?;
                let source = copy_texture_info(
                    ctx,
                    source,
                    source_mip_level,
                    AlcoOrigin3D { x: 0, y: 0, z: 0 },
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

/// ABI: texture-to-texture copy on the open encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_copy_texture_to_texture(
    device: AlcoHandle,
    encoder: AlcoHandle,
    source: AlcoHandle,
    source_mip_level: u32,
    destination: AlcoHandle,
    destination_mip_level: u32,
    aspect: u32,
    copy_size: AlcoExtent3D,
) -> AlcoStatus {
    crate::entry::guard(|| {
        run_with_device(device, |ctx| {
                let encoder_id = lookup_encoder(ctx, encoder)?;
                let source = copy_texture_info(
                    ctx,
                    source,
                    source_mip_level,
                    AlcoOrigin3D { x: 0, y: 0, z: 0 },
                    aspect,
                )?;
                let destination = copy_texture_info(
                    ctx,
                    destination,
                    destination_mip_level,
                    AlcoOrigin3D { x: 0, y: 0, z: 0 },
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

/// ABI: resolves timestamp queries into a buffer.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_resolve_query_set(
    device: AlcoHandle,
    encoder: AlcoHandle,
    query_set: AlcoHandle,
    first_query: u32,
    query_count: u32,
    destination: AlcoHandle,
    destination_offset: u64,
) -> AlcoStatus {
    crate::entry::guard(|| {
        run_with_device(device, |ctx| {
                let encoder_id = lookup_encoder(ctx, encoder)?;
                let query_set_id = ctx.query_sets().with(query_set, |obj| obj.id).map_err(|_| {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid query set handle");
                    AlcoStatus::INVALID_HANDLE
                })?;
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

// ---------------------------------------------------------------------------
// Queue
// ---------------------------------------------------------------------------

/// ABI: writes bytes into a buffer through the queue (bypasses command
/// encoding).
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_queue_write_buffer(
    device: AlcoHandle,
    buffer: AlcoHandle,
    offset: u64,
    data: *const u8,
    size: u32,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if data.is_null() && size > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null data pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        run_with_device(device, |ctx| {
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

/// ABI: writes raw bytes into a texture region through the queue.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_queue_write_texture(
    device: AlcoHandle,
    texture: AlcoHandle,
    mip_level: u32,
    origin: AlcoOrigin3D,
    aspect: u32,
    data: *const u8,
    data_size: u32,
    layout: *const AlcoCopyLayout,
    size: AlcoExtent3D,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if data.is_null() && data_size > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null data pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        let layout = match layout.as_ref() {
            Some(l) => l,
            None => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "null layout");
                return AlcoStatus::INVALID_ARGUMENT;
            }
        };
        run_with_device(device, |ctx| {
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
                        set_error_from(AlcoStatus::VALIDATION, &e);
                        Err(AlcoStatus::VALIDATION)
                    }
                }
            })
    })
}

/// ABI: submits a command buffer. Both the handle and core registration are
/// consumed on success or failure; `out_index` receives the submission index
/// usable with `alco_device_poll`.
///
/// # Safety
/// `out_index`, if non-null, must point to a writable `u64`.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_queue_submit(
    device: AlcoHandle,
    command_buffer: AlcoHandle,
    out_index: *mut u64,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| {
                let obj = match ctx.command_buffers().remove(command_buffer) {
                    Ok(obj) => obj,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid command buffer handle");
                        return AlcoStatus::INVALID_HANDLE;
                    }
                };
                let result = ctx.global.queue_submit(ctx.queue_id, &[obj.id]);
                // Submission consumes command contents, not the core registry entry.
                // Keep its result/index while releasing the ID on both paths.
                ctx.global.command_buffer_drop(obj.id);
                match result {
                    Ok(index) => {
                        if !out_index.is_null() {
                            *out_index = index;
                        }
                        AlcoStatus::OK
                    }
                    Err((index, e)) => {
                        if !out_index.is_null() {
                            *out_index = index;
                        }
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

// ---------------------------------------------------------------------------
// Render bundles
// ---------------------------------------------------------------------------

/// C# `RenderBundleDescriptor`.
#[repr(C)]
pub struct AlcoBundleEncoderDesc {
    pub color_formats: *const u32,
    pub color_format_count: u32,
    /// `ALCO_NONE`/0 when the bundle targets no depth attachment.
    pub depth_stencil_format: u32,
    pub depth_read_only: u32,
    pub stencil_read_only: u32,
    pub sample_count: u32,
    pub name: *const c_char,
}

/// ABI: creates a render bundle encoder.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_bundle_encoder_create(
    device: AlcoHandle,
    desc: *const AlcoBundleEncoderDesc,
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
                let mut formats = Vec::new();
                if desc.color_format_count > 0 {
                    if desc.color_formats.is_null() {
                        set_error(AlcoStatus::INVALID_ARGUMENT, "null color format array");
                        return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
                    }
                    let raw = std::slice::from_raw_parts(desc.color_formats, desc.color_format_count as usize);
                    for format in raw {
                        match pixel_format(*format) {
                            Ok(f) => formats.push(Some(f)),
                            Err(s) => return (s, AlcoHandle::NULL),
                        }
                    }
                }
                let depth_stencil = if desc.depth_stencil_format != ALCO_NONE && desc.depth_stencil_format != 0 {
                    match pixel_format(desc.depth_stencil_format) {
                        Ok(f) => Some(wgt::RenderBundleDepthStencil {
                            format: f,
                            depth_read_only: desc.depth_read_only != 0,
                            stencil_read_only: desc.stencil_read_only != 0,
                        }),
                        Err(s) => return (s, AlcoHandle::NULL),
                    }
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
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.bundle_encoders().insert(BundleEncoderObj { encoder }))
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

/// ABI: finishes a bundle encoder, consuming it unless overlapping access is rejected.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_bundle_encoder_finish(
    device: AlcoHandle,
    bundle_encoder: AlcoHandle,
    out: *mut AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        if out.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null out pointer");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        DEVICES
            .with(device, |ctx| {
                let mut obj = match ctx.bundle_encoders().remove(bundle_encoder) {
                    Ok(obj) => obj,
                    Err(status) => return (recording_error(status, "bundle encoder"), AlcoHandle::NULL),
                };
                let wdesc = wgc::command::RenderBundleDescriptor { label: None };
                let (id, err) = ctx
                    .global
                    .render_bundle_encoder_finish(&mut obj.encoder, &wdesc, None);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.render_bundle_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                (AlcoStatus::OK, ctx.render_bundles().insert(RenderBundleObj { id }))
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

/// ABI: destroys an unfinished bundle encoder; overlapping access leaves the handle intact.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_bundle_encoder_destroy(
    device: AlcoHandle,
    bundle_encoder: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.bundle_encoders().remove(bundle_encoder) {
                // Dropping the boxed encoder is the whole cleanup (wgpu-core has
                // no separate unfinished-encoder drop entry point).
                Ok(_obj) => AlcoStatus::OK,
                Err(status) => recording_error(status, "bundle encoder"),
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

/// ABI: destroys a render bundle.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_render_bundle_destroy(
    device: AlcoHandle,
    bundle: AlcoHandle,
) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.render_bundles().remove(bundle) {
                Ok(obj) => {
                    ctx.global.render_bundle_drop(obj.id);
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid render bundle handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}

macro_rules! bundle_fn {
    ($(#[$doc:meta])* $name:ident($($arg:ident: $ty:ty),*) $body:expr) => {
        $(#[$doc])*
        #[no_mangle]
        pub unsafe extern "C-unwind" fn $name(device: AlcoHandle, bundle_encoder: AlcoHandle, $($arg: $ty),*) -> AlcoStatus {
            crate::entry::guard(|| {
                DEVICES
                    .with(device, |ctx| {
                        let result = ctx.bundle_encoders().with(bundle_encoder, |obj| {
                            type Body = fn(&DeviceCtx, &mut wgc::command::RenderBundleEncoder, $($ty),*) -> Result<(), AlcoStatus>;
                            let body: Body = $body;
                            body(ctx, &mut obj.encoder $(, $arg)*)
                        });
                        match result {
                            Ok(Ok(())) => AlcoStatus::OK,
                            Ok(Err(status)) => status,
                            Err(status) => recording_error(status, "bundle encoder"),
                        }
                    })
                    .unwrap_or_else(|s| {
                        set_error(s, "invalid device handle");
                        s
                    })
            })
        }
    };
}

bundle_fn!(
    /// ABI: sets the bundle's graphics pipeline.
    alco_bundle_set_pipeline(pipeline: AlcoHandle) |ctx, bundle, pipeline| {
        match ctx.pipelines().with(pipeline, |obj| match obj {
            crate::pipeline::PipelineObj::Graphics(id) => Some(*id),
            crate::pipeline::PipelineObj::Compute(_) => None,
        }) {
            Ok(Some(id)) => record_result(ctx.global.render_bundle_encoder_set_pipeline(bundle, id)),
            Ok(None) => {
                set_error(AlcoStatus::INVALID_ARGUMENT, "pipeline is not a graphics pipeline");
                Err(AlcoStatus::INVALID_ARGUMENT)
            }
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid graphics pipeline handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: binds a resource group at `slot`.
    alco_bundle_set_bind_group(slot: u32, group: AlcoHandle) |ctx, bundle, slot, group| {
        let id = ctx.bind_groups().with(group, |obj| obj.id);
        match id {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_set_bind_group(bundle, slot, Some(id), &[])),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: binds a vertex buffer slot.
    alco_bundle_set_vertex_buffer(slot: u32, buffer: AlcoHandle, offset: u64, size: u64) |ctx, bundle, slot, buffer, offset, size| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_set_vertex_buffer(
                bundle,
                slot,
                Some(id),
                offset,
                nonzero_size(size),
            )),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: binds the index buffer.
    alco_bundle_set_index_buffer(buffer: AlcoHandle, format: u32, offset: u64, size: u64) |ctx, bundle, buffer, format, offset, size| {
        let wformat = index_format(format);
        match ctx.buffers().with(buffer, |obj| obj.id) {
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
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: uploads immediates (push constants).
    alco_bundle_set_immediates(offset: u32, data: *const u8, size: u32) |ctx, bundle, offset, data, size| {
        if data.is_null() && size > 0 {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null immediate data");
            return Err(AlcoStatus::INVALID_ARGUMENT);
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
    alco_bundle_draw(vertex_count: u32, instance_count: u32, first_vertex: u32, first_instance: u32) |ctx, bundle, vertex_count, instance_count, first_vertex, first_instance| {
        record_result(ctx.global.render_bundle_encoder_draw(bundle, vertex_count, instance_count, first_vertex, first_instance))
    }
);

bundle_fn!(
    /// ABI: indexed draw.
    alco_bundle_draw_indexed(index_count: u32, instance_count: u32, first_index: u32, vertex_offset: i32, first_instance: u32) |ctx, bundle, index_count, instance_count, first_index, vertex_offset, first_instance| {
        record_result(ctx.global.render_bundle_encoder_draw_indexed(bundle, index_count, instance_count, first_index, vertex_offset, first_instance))
    }
);

bundle_fn!(
    /// ABI: indirect non-indexed draw.
    alco_bundle_draw_indirect(buffer: AlcoHandle, offset: u64) |ctx, bundle, buffer, offset| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_draw_indirect(bundle, id, offset)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

bundle_fn!(
    /// ABI: indirect indexed draw.
    alco_bundle_draw_indexed_indirect(buffer: AlcoHandle, offset: u64) |ctx, bundle, buffer, offset| {
        match ctx.buffers().with(buffer, |obj| obj.id) {
            Ok(id) => record_result(ctx.global.render_bundle_encoder_draw_indexed_indirect(bundle, id, offset)),
            Err(_) => {
                set_error(AlcoStatus::INVALID_HANDLE, "invalid buffer handle");
                Err(AlcoStatus::INVALID_HANDLE)
            }
        }
    }
);

#[cfg(test)]
mod tests {
    use super::*;
    use crate::device::alco_device_poll;
    use crate::objects::*;
    use crate::test_support::{last_error, TestDevice};
    use std::ptr;

    fn registry_counts(device: AlcoHandle) -> (usize, usize, usize) {
        DEVICES.with(device, |ctx| {
            let report = ctx.global.generate_report().hub;
            assert_eq!(report.command_buffers.num_allocated,
                report.command_buffers.num_kept_from_user);
            (report.command_buffers.num_allocated,
                report.command_buffers.num_kept_from_user,
                report.command_encoders.num_allocated)
        }).unwrap()
    }

    unsafe fn encoder(device: AlcoHandle) -> AlcoHandle {
        let mut handle = AlcoHandle::NULL;
        assert_eq!(alco_encoder_create(device, ptr::null(), &mut handle), AlcoStatus::OK,
            "{}", last_error());
        handle
    }

    unsafe fn finish(device: AlcoHandle, encoder: AlcoHandle) -> AlcoHandle {
        let mut handle = AlcoHandle::NULL;
        assert_eq!(alco_encoder_finish(device, encoder, &mut handle), AlcoStatus::OK,
            "{}", last_error());
        handle
    }

    unsafe fn buffer(device: AlcoHandle, usage: u32) -> AlcoHandle {
        let desc = AlcoBufferDesc { size: 4, usage, name: ptr::null() };
        let mut handle = AlcoHandle::NULL;
        assert_eq!(alco_buffer_create(device, &desc, &mut handle), AlcoStatus::OK,
            "{}", last_error());
        handle
    }

    #[test]
    fn vulkan_successful_submissions_release_core_registry_entries() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            let mut previous_index = 0;
            for iteration in 0..1024 {
                let encoder = encoder(device.handle);
                let command = finish(device.handle, encoder);
                assert_eq!(registry_counts(device.handle), (1, 1, 0));
                let mut index = 0;
                assert_eq!(alco_queue_submit(device.handle, command, &mut index), AlcoStatus::OK,
                    "{}", last_error());
                assert!(index > previous_index);
                previous_index = index;
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
                assert_eq!(alco_command_buffer_destroy(device.handle, command),
                    AlcoStatus::INVALID_HANDLE);
                if iteration % 64 == 63 {
                    assert_eq!(alco_device_poll(device.handle, ALCO_TRUE, index, ptr::null_mut()),
                        AlcoStatus::OK, "{}", last_error());
                    assert_eq!(registry_counts(device.handle), (0, 0, 0));
                }
            }
            // The optional output pointer must not alter cleanup semantics.
            let encoder = encoder(device.handle);
            let command = finish(device.handle, encoder);
            assert_eq!(alco_queue_submit(device.handle, command, ptr::null_mut()), AlcoStatus::OK);
            assert_eq!(alco_device_poll(device.handle, ALCO_TRUE, u64::MAX, ptr::null_mut()),
                AlcoStatus::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_submission_consumes_handle_and_core_registry_entry() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            // A destroyed resource is detected at submission, after a valid finish.
            let source = buffer(device.handle, 1 << 2);
            let destination = buffer(device.handle, 1 << 3);
            let encoder = encoder(device.handle);
            assert_eq!(alco_copy_buffer_to_buffer(device.handle, encoder, source, 0,
                destination, 0, 4), AlcoStatus::OK, "{}", last_error());
            let command = finish(device.handle, encoder);
            assert_eq!(registry_counts(device.handle), (1, 1, 0));
            assert_eq!(alco_buffer_destroy(device.handle, source), AlcoStatus::OK);
            let mut index = u64::MAX;
            let status = alco_queue_submit(device.handle, command, &mut index);
            assert_eq!(status, AlcoStatus::VALIDATION, "{}", last_error());
            assert!(last_error().contains("destroyed"), "{}", last_error());
            assert_ne!(index, u64::MAX);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(alco_queue_submit(device.handle, command, ptr::null_mut()),
                AlcoStatus::INVALID_HANDLE);
            assert_eq!(alco_command_buffer_destroy(device.handle, command),
                AlcoStatus::INVALID_HANDLE);
            assert_eq!(alco_buffer_destroy(device.handle, destination), AlcoStatus::OK);
        }
    }

    #[test]
    fn vulkan_pass_errors_preserve_distinct_attachment_root_causes() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            let texture_desc = AlcoTextureDesc {
                dimension: 1, format: 42, usage: 1 << 5, width: 4, height: 4,
                depth_or_array_layers: 1, mip_level_count: 1, sample_count: 1,
                name: ptr::null(),
            };
            let mut texture = AlcoHandle::NULL;
            assert_eq!(alco_texture_create(device.handle, &texture_desc, &mut texture),
                AlcoStatus::OK, "{}", last_error());
            let mut view = AlcoHandle::NULL;
            assert_eq!(alco_texture_create_view(device.handle, texture, ptr::null(), &mut view),
                AlcoStatus::OK, "{}", last_error());
            let mut messages = Vec::new();
            for (clear, stencil_load, stencil_store, expected) in [
                (1.0, 0, 0, "without stencil aspect"),
                (2.0, ALCO_NONE, ALCO_NONE, "must be between 0.0 and 1.0"),
            ] {
                let encoder = encoder(device.handle);
                let attachment = AlcoDepthStencilAttachment {
                    view, depth_load_op: 1, depth_store_op: 0, depth_clear: clear,
                    stencil_load_op: stencil_load, stencil_store_op: stencil_store, stencil_clear: 0,
                };
                let pass_desc = AlcoRenderPassDesc {
                    color_attachments: ptr::null(), color_attachment_count: 0,
                    depth_stencil: &attachment, timestamp_writes: ptr::null(),
                };
                let mut pass = AlcoHandle::NULL;
                assert_eq!(alco_render_pass_begin(device.handle, encoder, &pass_desc, &mut pass),
                    AlcoStatus::OK, "{}", last_error());
                assert_eq!(alco_render_pass_end(device.handle, pass), AlcoStatus::OK);
                let mut command = AlcoHandle::NULL;
                assert_eq!(alco_encoder_finish(device.handle, encoder, &mut command),
                    AlcoStatus::VALIDATION);
                let message = last_error();
                assert!(message.contains("In a pass parameter"), "{message}");
                assert!(message.contains("Caused by:"), "{message}");
                assert!(message.contains(expected), "{message}");
                if stencil_load != ALCO_NONE {
                    assert!(message.contains("Depth32Float"), "{message}");
                    assert!(message.contains("LoadOp") && message.contains("StoreOp"), "{message}");
                }
                messages.push(message);
                assert!(command.is_null());
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
            }
            assert_ne!(messages[0], messages[1]);
            assert_eq!(alco_texture_view_destroy(device.handle, view), AlcoStatus::OK);
            assert_eq!(alco_texture_destroy(device.handle, texture), AlcoStatus::OK);
        }
    }

    #[test]
    fn vulkan_empty_texture_writes_preserve_validation() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            let desc = AlcoTextureDesc {
                dimension: 1, format: 18, usage: 1 << 1, width: 1, height: 1,
                depth_or_array_layers: 1, mip_level_count: 1, sample_count: 1,
                name: ptr::null(),
            };
            let mut texture = AlcoHandle::NULL;
            assert_eq!(alco_texture_create(device.handle, &desc, &mut texture),
                AlcoStatus::OK, "{}", last_error());
            let layout = AlcoCopyLayout {
                offset: 0, bytes_per_row: ALCO_NONE, rows_per_image: ALCO_NONE,
            };
            let write = |texture, data, data_size, width| alco_queue_write_texture(
                device.handle, texture, 0, AlcoOrigin3D { x: 0, y: 0, z: 0 }, 0,
                data, data_size, &layout,
                AlcoExtent3D { width, height: 1, depth_or_array_layers: 1 },
            );
            let bytes = [0u8; 4];
            for data in [ptr::null(), bytes.as_ptr()] {
                assert_eq!(write(texture, data, 0, 0), AlcoStatus::OK, "{}", last_error());
                assert_eq!(write(texture, data, 0, 1), AlcoStatus::VALIDATION,
                    "{}", last_error());
            }
            assert_eq!(write(texture, ptr::null(), 4, 0), AlcoStatus::INVALID_ARGUMENT);
            assert_eq!(write(AlcoHandle::NULL, ptr::null(), 0, 0), AlcoStatus::INVALID_HANDLE);
            assert_eq!(alco_texture_destroy(device.handle, texture), AlcoStatus::OK);
        }
    }

    #[test]
    fn vulkan_empty_immediate_setters_accept_null_and_reject_nonempty_null() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            type Setter = unsafe extern "C-unwind" fn(
                AlcoHandle, AlcoHandle, u32, *const u8, u32,
            ) -> AlcoStatus;
            let bytes = [0u8; 4];
            for (compute, set) in [
                (false, alco_render_pass_set_immediates as Setter),
                (true, alco_compute_pass_set_immediates as Setter),
            ] {
                let encoder = encoder(device.handle);
                let mut pass = AlcoHandle::NULL;
                let status = if compute {
                    alco_compute_pass_begin(device.handle, encoder, ptr::null(), &mut pass)
                } else {
                    let desc = AlcoRenderPassDesc {
                        color_attachments: ptr::null(), color_attachment_count: 0,
                        depth_stencil: ptr::null(), timestamp_writes: ptr::null(),
                    };
                    alco_render_pass_begin(device.handle, encoder, &desc, &mut pass)
                };
                assert_eq!(status, AlcoStatus::OK, "{}", last_error());
                assert_eq!(set(device.handle, pass, 0, ptr::null(), 4),
                    AlcoStatus::INVALID_ARGUMENT);
                assert_eq!(set(device.handle, AlcoHandle::NULL, 0, ptr::null(), 0),
                    AlcoStatus::INVALID_HANDLE);
                for data in [ptr::null(), bytes.as_ptr()] {
                    assert_eq!(set(device.handle, pass, 0, data, 0),
                        AlcoStatus::OK, "{}", last_error());
                }
                let status = if compute {
                    alco_compute_pass_end(device.handle, pass)
                } else {
                    alco_render_pass_end(device.handle, pass)
                };
                assert_eq!(status, AlcoStatus::OK, "{}", last_error());
                assert_eq!(alco_encoder_destroy(device.handle, encoder), AlcoStatus::OK);
            }
            let desc = AlcoBundleEncoderDesc {
                color_formats: ptr::null(), color_format_count: 0, depth_stencil_format: 42,
                depth_read_only: ALCO_TRUE, stencil_read_only: ALCO_TRUE,
                sample_count: 1, name: ptr::null(),
            };
            let mut bundle = AlcoHandle::NULL;
            assert_eq!(alco_bundle_encoder_create(device.handle, &desc, &mut bundle),
                AlcoStatus::OK, "{}", last_error());
            assert_eq!(alco_bundle_set_immediates(device.handle, bundle, 0, ptr::null(), 4),
                AlcoStatus::INVALID_ARGUMENT);
            assert_eq!(alco_bundle_set_immediates(device.handle, AlcoHandle::NULL, 0, ptr::null(), 0),
                AlcoStatus::INVALID_HANDLE);
            for data in [ptr::null(), bytes.as_ptr()] {
                assert_eq!(alco_bundle_set_immediates(device.handle, bundle, 0, data, 0),
                    AlcoStatus::OK, "{}", last_error());
            }
            assert_eq!(alco_bundle_encoder_destroy(device.handle, bundle), AlcoStatus::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
        }
    }

    #[test]
    fn vulkan_failed_encoder_creation_releases_core_registry_entries() {
        let Some(device) = TestDevice::new() else { return };
        DEVICES.with(device.handle, |ctx| {
            // Lose the core device while keeping the ABI context valid for retries.
            ctx.global.device_destroy(ctx.device_id);
        }).unwrap();
        unsafe {
            for _ in 0..32 {
                let mut handle = AlcoHandle::NULL;
                assert_eq!(alco_encoder_create(device.handle, ptr::null(), &mut handle),
                    AlcoStatus::OUT_OF_MEMORY, "{}", last_error());
                assert!(last_error().contains("device is lost"), "{}", last_error());
                assert!(handle.is_null());
                DEVICES.with(device.handle, |ctx| {
                    let report = ctx.global.generate_report().hub.command_encoders;
                    assert_eq!(report.num_allocated, 0, "{report:?}");
                    assert_eq!(report.num_kept_from_user, 0, "{report:?}");
                }).unwrap();
                assert_eq!(registry_counts(device.handle), (0, 0, 0));
            }
        }
    }

    #[test]
    fn vulkan_readback_submission_retains_work_without_retaining_registration() {
        let Some(device) = TestDevice::new() else { return };
        unsafe {
            let source = buffer(device.handle, (1 << 2) | (1 << 3));
            let destination = buffer(device.handle, (1 << 0) | (1 << 3));
            let bytes = [3, 19, 127, 254];
            assert_eq!(alco_queue_write_buffer(device.handle, source, 0, bytes.as_ptr(), 4),
                AlcoStatus::OK);
            for data in [ptr::null(), bytes.as_ptr()] {
                for offset in [0, 4] {
                    assert_eq!(alco_queue_write_buffer(device.handle, source, offset, data, 0),
                        AlcoStatus::OK, "{}", last_error());
                }
            }
            assert_eq!(alco_queue_write_buffer(device.handle, source, 0, ptr::null(), 4),
                AlcoStatus::INVALID_ARGUMENT);
            assert_eq!(alco_queue_write_buffer(device.handle, AlcoHandle::NULL, 0, ptr::null(), 0),
                AlcoStatus::INVALID_HANDLE);
            for offset in [1, 8] {
                assert_eq!(alco_queue_write_buffer(device.handle, source, offset, ptr::null(), 0),
                    AlcoStatus::VALIDATION, "{}", last_error());
            }
            let encoder = encoder(device.handle);
            assert_eq!(alco_copy_buffer_to_buffer(device.handle, encoder, source, 0,
                destination, 0, 4), AlcoStatus::OK);
            let command = finish(device.handle, encoder);
            let mut index = 0;
            assert_eq!(alco_queue_submit(device.handle, command, &mut index), AlcoStatus::OK);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(alco_buffer_map_read(device.handle, destination, 0, 4), AlcoStatus::OK);
            assert_eq!(alco_device_poll(device.handle, ALCO_TRUE, index, ptr::null_mut()),
                AlcoStatus::OK);
            assert_eq!(alco_buffer_map_poll(device.handle, destination), AlcoStatus::OK);
            let mut mapped = ptr::null();
            assert_eq!(alco_buffer_get_mapped_range(device.handle, destination, 0, 4, &mut mapped),
                AlcoStatus::OK);
            assert_eq!(std::slice::from_raw_parts(mapped, 4), &bytes);
            assert_eq!(registry_counts(device.handle), (0, 0, 0));
            assert_eq!(alco_buffer_unmap(device.handle, destination), AlcoStatus::OK);
            assert_eq!(alco_buffer_destroy(device.handle, destination), AlcoStatus::OK);
            assert_eq!(alco_buffer_destroy(device.handle, source), AlcoStatus::OK);
        }
    }
}
