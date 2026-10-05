//! Graphics and compute pipelines. The pipeline layout (bind-group-layout
//! handles + immediate size) is built internally and dropped right after
//! pipeline creation — it is never visible through the ABI, matching the old
//! backend where C# released the layout immediately after pipeline creation.

use crate::abi::*;
use crate::convert::*;
use crate::device::{DeviceCtx, DEVICES};
use crate::entry::{set_error, set_error_from};
use crate::objects::{borrow_label, label};
use std::ffi::c_char;
use wgpu_core as wgc;
use wgpu_types as wgt;

pub(crate) enum PipelineObj {
    Graphics(wgc::id::RenderPipelineId),
    Compute(wgc::id::ComputePipelineId),
}

/// One C# `VertexElement`.
#[repr(C)]
pub struct AlcoVertexElement {
    pub location: u32,
    pub offset: u32,
    /// C# `VertexFormat`.
    pub format: u32,
}

/// One C# `VertexInputLayout` (a vertex buffer slot).
#[repr(C)]
pub struct AlcoVertexLayout {
    pub stride: u32,
    /// C# `VertexStepMode`.
    pub step_mode: u32,
    pub elements: *const AlcoVertexElement,
    pub element_count: u32,
}

/// C# `BlendComponent`.
#[repr(C)]
pub struct AlcoBlendComponent {
    pub src_factor: u32,
    pub dst_factor: u32,
    pub operation: u32,
}

/// C# `BlendState`.
#[repr(C)]
pub struct AlcoBlendState {
    pub color: AlcoBlendComponent,
    pub alpha: AlcoBlendComponent,
}

/// C# `StencilFaceState`.
#[repr(C)]
pub struct AlcoStencilFace {
    pub compare: u32,
    pub stencil_fail_op: u32,
    pub depth_fail_op: u32,
    pub pass_op: u32,
}

/// C# `DepthStencilState`.
#[repr(C)]
pub struct AlcoDepthStencilState {
    pub depth_write_enabled: u32,
    /// Present in the C# struct but unsupported by WebGPU; kept for layout parity.
    pub depth_bounds_test_enabled: u32,
    pub depth_compare: u32,
    pub front: AlcoStencilFace,
    pub back: AlcoStencilFace,
    pub stencil_read_mask: u32,
    pub stencil_write_mask: u32,
}

/// C# `GraphicsPipelineDescriptor`.
#[repr(C)]
pub struct AlcoGraphicsPipelineDesc {
    /// `AlcoHandle`s of bind-group layouts (C# `GPUBindGroup`).
    pub bind_group_layouts: *const AlcoHandle,
    pub bind_group_layout_count: u32,
    pub vertex_module: AlcoHandle,
    pub vertex_entry: *const c_char,
    pub fragment_module: AlcoHandle,
    pub fragment_entry: *const c_char,
    pub vertex_layouts: *const AlcoVertexLayout,
    pub vertex_layout_count: u32,
    /// C# `FillMode` (Solid/Wireframe). Wireframe is accepted but rasterized
    /// solid, matching the old backend (no POLYGON_MODE_LINE feature request).
    pub fill_mode: u32,
    /// C# `CullMode`.
    pub cull_mode: u32,
    /// C# `FrontFace`.
    pub front_face: u32,
    pub blend: AlcoBlendState,
    pub depth_stencil: AlcoDepthStencilState,
    /// C# `PixelFormat` value or `ALCO_NONE` for no depth attachment.
    pub depth_stencil_format: u32,
    /// C# `PrimitiveTopology`.
    pub topology: u32,
    pub color_formats: *const u32,
    pub color_format_count: u32,
    /// Number of fragment outputs; targets at or beyond it get a zero write
    /// mask instead of failing validation. `ALCO_NONE` means "all targets".
    pub fragment_output_count: u32,
    /// Total immediates (push constants) size in bytes, 0 when unused.
    pub immediate_size: u32,
    pub name: *const c_char,
}

/// C# `ComputePipelineDescriptor`.
#[repr(C)]
pub struct AlcoComputePipelineDesc {
    pub bind_group_layouts: *const AlcoHandle,
    pub bind_group_layout_count: u32,
    pub compute_module: AlcoHandle,
    pub compute_entry: *const c_char,
    pub immediate_size: u32,
    pub name: *const c_char,
}

/// Builds the internal pipeline layout from bind-group-layout handles.
/// Returns `Err` with a status already recorded on failure.
unsafe fn create_pipeline_layout(
    ctx: &DeviceCtx,
    layouts: *const AlcoHandle,
    count: u32,
    immediate_size: u32,
    name: *const c_char,
) -> Result<wgc::id::PipelineLayoutId, AlcoStatus> {
    let mut wlayouts = Vec::with_capacity(count as usize);
    if count > 0 {
        if layouts.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null bind group layout array");
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
        let handles = std::slice::from_raw_parts(layouts, count as usize);
        for handle in handles {
            match ctx.bind_group_layouts().with(*handle, |obj| obj.id) {
                Ok(id) => wlayouts.push(Some(id)),
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid bind group layout handle");
                    return Err(AlcoStatus::INVALID_HANDLE);
                }
            }
        }
    }
    let wdesc = wgc::binding_model::PipelineLayoutDescriptor {
        label: label(name),
        bind_group_layouts: std::borrow::Cow::Owned(wlayouts),
        immediate_size,
    };
    let (id, err) = ctx
        .global
        .device_create_pipeline_layout(ctx.device_id, &wdesc, None);
    if let Some(e) = err {
        set_error_from(AlcoStatus::VALIDATION, &e);
        ctx.global.pipeline_layout_drop(id);
        return Err(AlcoStatus::VALIDATION);
    }
    Ok(id)
}

fn programmable_stage(
    module: wgc::id::ShaderModuleId,
    entry_point: &str,
) -> wgc::pipeline::ProgrammableStageDescriptor<'static> {
    wgc::pipeline::ProgrammableStageDescriptor {
        module,
        entry_point: Some(std::borrow::Cow::Owned(entry_point.to_string())),
        constants: Default::default(),
        zero_initialize_workgroup_memory: true,
    }
}

fn stencil_face(face: &AlcoStencilFace) -> Result<wgt::StencilFaceState, AlcoStatus> {
    Ok(wgt::StencilFaceState {
        compare: compare_function(face.compare)?,
        fail_op: stencil_operation(face.stencil_fail_op)?,
        depth_fail_op: stencil_operation(face.depth_fail_op)?,
        pass_op: stencil_operation(face.pass_op)?,
    })
}

/// ABI: creates a graphics pipeline.
#[no_mangle]
pub unsafe extern "C" fn alco_graphics_pipeline_create(
    device: AlcoHandle,
    desc: *const AlcoGraphicsPipelineDesc,
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
                // --- Resolve stages -------------------------------------
                let vertex_id = match ctx.shader_modules().with(desc.vertex_module, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid vertex shader module handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };
                let fragment_id = match ctx.shader_modules().with(desc.fragment_module, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid fragment shader module handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };

                // --- Vertex layouts -------------------------------------
                if desc.vertex_layout_count > 0 && desc.vertex_layouts.is_null() {
                    set_error(AlcoStatus::INVALID_ARGUMENT, "null vertex layout array");
                    return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
                }
                let layouts = if desc.vertex_layouts.is_null() {
                    &[][..]
                } else {
                    std::slice::from_raw_parts(desc.vertex_layouts, desc.vertex_layout_count as usize)
                };
                let mut wbuffers = Vec::with_capacity(layouts.len());
                for layout in layouts {
                    let elements = if layout.elements.is_null() || layout.element_count == 0 {
                        &[][..]
                    } else {
                        std::slice::from_raw_parts(layout.elements, layout.element_count as usize)
                    };
                    let mut wattributes = Vec::with_capacity(elements.len());
                    for element in elements {
                        let format = match vertex_format(element.format) {
                            Ok(f) => f,
                            Err(s) => return (s, AlcoHandle::NULL),
                        };
                        wattributes.push(wgt::VertexAttribute {
                            format,
                            offset: element.offset as u64,
                            shader_location: element.location,
                        });
                    }
                    let step_mode = match vertex_step_mode(layout.step_mode) {
                        Ok(m) => m,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    wbuffers.push(Some(wgc::pipeline::VertexBufferLayout {
                        array_stride: layout.stride as u64,
                        step_mode,
                        attributes: std::borrow::Cow::Owned(wattributes),
                    }));
                }

                // --- Blend + color targets -------------------------------------
                let blend = |c: &AlcoBlendComponent| -> Result<wgt::BlendComponent, AlcoStatus> {
                    Ok(wgt::BlendComponent {
                        src_factor: blend_factor(c.src_factor)?,
                        dst_factor: blend_factor(c.dst_factor)?,
                        operation: blend_operation(c.operation)?,
                    })
                };
                let color_blend = match blend(&desc.blend.color) {
                    Ok(b) => b,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let alpha_blend = match blend(&desc.blend.alpha) {
                    Ok(b) => b,
                    Err(s) => return (s, AlcoHandle::NULL),
                };

                if desc.color_format_count > 0 && desc.color_formats.is_null() {
                    set_error(AlcoStatus::INVALID_ARGUMENT, "null color format array");
                    return (AlcoStatus::INVALID_ARGUMENT, AlcoHandle::NULL);
                }
                let formats = if desc.color_formats.is_null() {
                    &[][..]
                } else {
                    std::slice::from_raw_parts(desc.color_formats, desc.color_format_count as usize)
                };
                let output_count = if desc.fragment_output_count == ALCO_NONE {
                    formats.len()
                } else {
                    desc.fragment_output_count as usize
                };
                let mut targets = Vec::with_capacity(formats.len());
                for (i, format) in formats.iter().enumerate() {
                    let format = match pixel_format(*format) {
                        Ok(f) => f,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    // WebGPU rejects a color target without a matching fragment
                    // output unless its write mask is zero, so extra targets
                    // (e.g. an MRT buffer a shader does not write) are masked
                    // out instead of failing pipeline validation.
                    let writes_target = i < output_count;
                    targets.push(Some(wgt::ColorTargetState {
                        format,
                        blend: writes_target.then(|| wgt::BlendState {
                            color: color_blend,
                            alpha: alpha_blend,
                        }),
                        write_mask: if writes_target {
                            wgt::ColorWrites::ALL
                        } else {
                            wgt::ColorWrites::empty()
                        },
                    }));
                }

                // --- Primitive state -------------------------------------
                let topology = match primitive_topology(desc.topology) {
                    Ok(t) => t,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                // Strip topologies draw indexed with primitive restart, which
                // the old backend always declared as Uint32.
                let is_strip =
                    topology == wgt::PrimitiveTopology::TriangleStrip || topology == wgt::PrimitiveTopology::LineStrip;
                // Line list rasterization has no meaningful face winding to cull.
                let cull = if topology == wgt::PrimitiveTopology::LineList {
                    None
                } else {
                    match cull_mode(desc.cull_mode) {
                        Ok(c) => c,
                        Err(s) => return (s, AlcoHandle::NULL),
                    }
                };
                let front_face = match front_face(desc.front_face) {
                    Ok(f) => f,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let primitive = wgt::PrimitiveState {
                    topology,
                    strip_index_format: is_strip.then_some(wgt::IndexFormat::Uint32),
                    front_face,
                    cull_mode: cull,
                    unclipped_depth: false,
                    polygon_mode: wgt::PolygonMode::Fill,
                    conservative: false,
                };

                // --- Depth stencil -------------------------------------
                let depth_stencil = if desc.depth_stencil_format != ALCO_NONE && desc.depth_stencil_format != 0 {
                    let format = match pixel_format(desc.depth_stencil_format) {
                        Ok(f) => f,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    let front = match stencil_face(&desc.depth_stencil.front) {
                        Ok(f) => f,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    let back = match stencil_face(&desc.depth_stencil.back) {
                        Ok(b) => b,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    let depth_compare = match compare_function(desc.depth_stencil.depth_compare) {
                        Ok(c) => c,
                        Err(s) => return (s, AlcoHandle::NULL),
                    };
                    Some(wgt::DepthStencilState {
                        format,
                        depth_write_enabled: Some(desc.depth_stencil.depth_write_enabled != 0),
                        depth_compare: Some(depth_compare),
                        stencil: wgt::StencilState {
                            front,
                            back,
                            read_mask: desc.depth_stencil.stencil_read_mask,
                            write_mask: desc.depth_stencil.stencil_write_mask,
                        },
                        bias: wgt::DepthBiasState {
                            constant: 0,
                            slope_scale: 0.0,
                            clamp: 0.0,
                        },
                    })
                } else {
                    None
                };

                // --- Pipeline layout (internal) -------------------------------------
                let layout_id = match create_pipeline_layout(
                    ctx,
                    desc.bind_group_layouts,
                    desc.bind_group_layout_count,
                    desc.immediate_size,
                    desc.name,
                ) {
                    Ok(id) => id,
                    Err(s) => return (s, AlcoHandle::NULL),
                };

                let wdesc = wgc::pipeline::RenderPipelineDescriptor {
                    label: label(desc.name),
                    layout: Some(layout_id),
                    vertex: wgc::pipeline::VertexState {
                        stage: programmable_stage(vertex_id, &borrow_label(desc.vertex_entry)),
                        buffers: std::borrow::Cow::Owned(wbuffers),
                    },
                    primitive,
                    depth_stencil,
                    multisample: wgt::MultisampleState {
                        count: 1,
                        mask: !0,
                        alpha_to_coverage_enabled: false,
                    },
                    fragment: Some(wgc::pipeline::FragmentState {
                        stage: programmable_stage(fragment_id, &borrow_label(desc.fragment_entry)),
                        targets: std::borrow::Cow::Owned(targets),
                    }),
                    multiview_mask: None,
                    cache: None,
                };

                let (id, err) = ctx.global.device_create_render_pipeline(ctx.device_id, &wdesc, None);
                ctx.global.pipeline_layout_drop(layout_id);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.render_pipeline_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.pipelines().insert(PipelineObj::Graphics(id));
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

/// ABI: creates a compute pipeline.
#[no_mangle]
pub unsafe extern "C" fn alco_compute_pipeline_create(
    device: AlcoHandle,
    desc: *const AlcoComputePipelineDesc,
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
                let compute_id = match ctx.shader_modules().with(desc.compute_module, |obj| obj.id) {
                    Ok(id) => id,
                    Err(_) => {
                        set_error(AlcoStatus::INVALID_HANDLE, "invalid compute shader module handle");
                        return (AlcoStatus::INVALID_HANDLE, AlcoHandle::NULL);
                    }
                };
                let layout_id = match create_pipeline_layout(
                    ctx,
                    desc.bind_group_layouts,
                    desc.bind_group_layout_count,
                    desc.immediate_size,
                    desc.name,
                ) {
                    Ok(id) => id,
                    Err(s) => return (s, AlcoHandle::NULL),
                };
                let wdesc = wgc::pipeline::ComputePipelineDescriptor {
                    label: label(desc.name),
                    layout: Some(layout_id),
                    stage: programmable_stage(compute_id, &borrow_label(desc.compute_entry)),
                    cache: None,
                };
                let (id, err) = ctx.global.device_create_compute_pipeline(ctx.device_id, &wdesc, None);
                ctx.global.pipeline_layout_drop(layout_id);
                if let Some(e) = err {
                    set_error_from(AlcoStatus::VALIDATION, &e);
                    ctx.global.compute_pipeline_drop(id);
                    return (AlcoStatus::VALIDATION, AlcoHandle::NULL);
                }
                let handle = ctx.pipelines().insert(PipelineObj::Compute(id));
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

/// ABI: destroys a graphics or compute pipeline.
#[no_mangle]
pub unsafe extern "C" fn alco_pipeline_destroy(device: AlcoHandle, pipeline: AlcoHandle) -> AlcoStatus {
    crate::entry::guard(|| {
        DEVICES
            .with(device, |ctx| match ctx.pipelines().remove(pipeline) {
                Ok(obj) => {
                    match obj {
                        PipelineObj::Graphics(id) => ctx.global.render_pipeline_drop(id),
                        PipelineObj::Compute(id) => ctx.global.compute_pipeline_drop(id),
                    }
                    AlcoStatus::OK
                }
                Err(_) => {
                    set_error(AlcoStatus::INVALID_HANDLE, "invalid pipeline handle");
                    AlcoStatus::INVALID_HANDLE
                }
            })
            .unwrap_or_else(|s| {
                set_error(s, "invalid device handle");
                s
            })
    })
}
