//! Graphics and compute pipelines. The pipeline layout (bind-group-layout
//! handles + immediate size) is built internally and dropped right after
//! pipeline creation — it is never visible through the ABI, matching the old
//! backend where C# released the layout immediately after pipeline creation.

use crate::abi::*;
use crate::convert::*;
use crate::device::DeviceCtx;
use crate::entry::{set_error, set_error_from};
use crate::objects::{borrow_label, label, DescriptorStorage};
use std::borrow::Cow;
use std::ffi::c_char;
use std::sync::Arc;
use wgpu_core as wgc;
use wgpu_types as wgt;

/// Owned core graphics-pipeline registration.
pub struct GraphicsPipelineObj {
    /// Core render-pipeline identity.
    pub id: wgc::id::RenderPipelineId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for GraphicsPipelineObj {
    fn drop(&mut self) {
        self.ctx.global.render_pipeline_drop(self.id);
    }
}

/// Owned core compute-pipeline registration.
pub struct ComputePipelineObj {
    /// Core compute-pipeline identity.
    pub id: wgc::id::ComputePipelineId,
    /// Device context retained until core cleanup completes.
    pub ctx: Arc<DeviceCtx>,
}

impl Drop for ComputePipelineObj {
    fn drop(&mut self) {
        self.ctx.global.compute_pipeline_drop(self.id);
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

/// One C# `VertexElement`.
#[repr(C)]
pub struct AlcoVertexElement {
    /// Shader vertex-input location.
    pub location: u32,
    /// Byte offset of this attribute within a vertex.
    pub offset: u32,
    /// C# `VertexFormat`.
    pub format: u32,
}

/// One C# `VertexInputLayout` (a vertex buffer slot).
#[repr(C)]
pub struct AlcoVertexLayout {
    /// Byte stride between vertices.
    pub stride: u32,
    /// C# `VertexStepMode`.
    pub step_mode: u32,
    /// Pointer to element_count initialized vertex elements.
    pub elements: *const AlcoVertexElement,
    /// Number of vertex elements in this layout.
    pub element_count: u32,
}

/// C# `BlendComponent`.
#[repr(C)]
pub struct AlcoBlendComponent {
    /// C# source blend-factor discriminant.
    pub src_factor: u32,
    /// C# destination blend-factor discriminant.
    pub dst_factor: u32,
    /// C# blend-operation discriminant.
    pub operation: u32,
}

/// C# `BlendState`.
#[repr(C)]
pub struct AlcoBlendState {
    /// Blend component applied to color channels.
    pub color: AlcoBlendComponent,
    /// Blend component applied to the alpha channel.
    pub alpha: AlcoBlendComponent,
}

/// C# `StencilFaceState`.
#[repr(C)]
pub struct AlcoStencilFace {
    /// C# comparison-function discriminant used by stencil tests.
    pub compare: u32,
    /// Stencil operation when the stencil test fails.
    pub stencil_fail_op: u32,
    /// Stencil operation when the depth test fails.
    pub depth_fail_op: u32,
    /// Stencil operation when both tests pass.
    pub pass_op: u32,
}

/// C# `DepthStencilState`.
#[repr(C)]
pub struct AlcoDepthStencilState {
    /// Whether successful depth tests write the depth attachment.
    pub depth_write_enabled: u32,
    /// Present in the C# struct but unsupported by WebGPU; kept for layout parity.
    pub depth_bounds_test_enabled: u32,
    /// C# comparison function used for depth tests.
    pub depth_compare: u32,
    /// Stencil state for front-facing primitives.
    pub front: AlcoStencilFace,
    /// Stencil state for back-facing primitives.
    pub back: AlcoStencilFace,
    /// Mask applied to values compared by the stencil test.
    pub stencil_read_mask: u32,
    /// Mask applied to stencil attachment writes.
    pub stencil_write_mask: u32,
}

/// C# `GraphicsPipelineDescriptor`.
#[repr(C)]
pub struct AlcoGraphicsPipelineDesc {
    /// Typed bind-group-layout pointers (C# `GPUBindGroup`).
    pub bind_group_layouts: *const AlcoBindGroupLayoutHandle,
    /// Number of layout handles; zero permits a null array.
    pub bind_group_layout_count: u32,
    /// Live typed vertex shader-module pointer.
    pub vertex_module: AlcoShaderModuleHandle,
    /// NUL-terminated UTF-8 vertex entry-point name.
    pub vertex_entry: *const c_char,
    /// Live typed fragment shader-module pointer.
    pub fragment_module: AlcoShaderModuleHandle,
    /// NUL-terminated UTF-8 fragment entry-point name.
    pub fragment_entry: *const c_char,
    /// Pointer to vertex_layout_count initialized vertex layouts.
    pub vertex_layouts: *const AlcoVertexLayout,
    /// Number of vertex-buffer layouts; zero permits a null array.
    pub vertex_layout_count: u32,
    /// C# `FillMode` (Solid/Wireframe). Wireframe is accepted but rasterized
    /// solid, matching the old backend (no POLYGON_MODE_LINE feature request).
    pub fill_mode: u32,
    /// C# `CullMode`.
    pub cull_mode: u32,
    /// C# `FrontFace`.
    pub front_face: u32,
    /// Blend state shared by writable color targets.
    pub blend: AlcoBlendState,
    /// Depth and stencil state when an attachment format is present.
    pub depth_stencil: AlcoDepthStencilState,
    /// C# `PixelFormat` value or `ALCO_NONE` for no depth attachment.
    pub depth_stencil_format: u32,
    /// C# `PrimitiveTopology`.
    pub topology: u32,
    /// Pointer to color_format_count C# pixel-format values.
    pub color_formats: *const u32,
    /// Number of color targets; zero permits a null array.
    pub color_format_count: u32,
    /// Number of fragment outputs; targets at or beyond it get a zero write
    /// mask instead of failing validation. `ALCO_NONE` means "all targets".
    pub fragment_output_count: u32,
    /// Total immediates (push constants) size in bytes, 0 when unused.
    pub immediate_size: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// C# `ComputePipelineDescriptor`.
#[repr(C)]
pub struct AlcoComputePipelineDesc {
    /// Pointer to bind_group_layout_count typed layout handles.
    pub bind_group_layouts: *const AlcoBindGroupLayoutHandle,
    /// Number of layout handles; zero permits a null array.
    pub bind_group_layout_count: u32,
    /// Live typed compute shader-module pointer.
    pub compute_module: AlcoShaderModuleHandle,
    /// NUL-terminated UTF-8 compute entry-point name.
    pub compute_entry: *const c_char,
    /// Total immediate-data size in bytes; zero disables immediates.
    pub immediate_size: u32,
    /// Optional NUL-terminated UTF-8 debug name borrowed for the call.
    pub name: *const c_char,
}

/// Builds the internal pipeline layout from bind-group-layout handles.
/// Returns `Err` with a status already recorded on failure.
unsafe fn create_pipeline_layout(
    ctx: &DeviceCtx,
    layouts: *const AlcoBindGroupLayoutHandle,
    count: u32,
    immediate_size: u32,
    name: *const c_char,
) -> Result<wgc::id::PipelineLayoutId, AlcoStatus> {
    let mut wlayouts = DescriptorStorage::<_, 8>::new(count as usize);
    if count > 0 {
        if layouts.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null bind group layout array");
            return Err(AlcoStatus::INVALID_ARGUMENT);
        }
        let handles = std::slice::from_raw_parts(layouts, count as usize);
        for handle in handles {
            let obj = match handle.get() {
                Ok(obj) => obj,
                Err(status) => {
                    set_error(status, "invalid bind group layout handle");
                    return Err(status);
                }
            };
            debug_assert!(std::ptr::eq(ctx, Arc::as_ptr(&obj.ctx)));
            wlayouts.push(Some(obj.id));
        }
    }
    let wdesc = wgc::binding_model::PipelineLayoutDescriptor {
        label: label(name),
        bind_group_layouts: Cow::Borrowed(wlayouts.as_slice()),
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

fn programmable_stage<'a>(
    module: wgc::id::ShaderModuleId,
    entry_point: &'a str,
) -> wgc::pipeline::ProgrammableStageDescriptor<'a> {
    wgc::pipeline::ProgrammableStageDescriptor {
        module,
        entry_point: Some(Cow::Borrowed(entry_point)),
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
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_graphics_pipeline_create(
    device: AlcoDeviceHandle,
    desc: *const AlcoGraphicsPipelineDesc,
    out: *mut AlcoGraphicsPipelineHandle,
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
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        // --- Resolve stages -------------------------------------
        let vertex_module = object_ref!(desc.vertex_module, "invalid vertex shader module handle");
        debug_assert!(Arc::ptr_eq(ctx, &vertex_module.ctx));
        let vertex_id = vertex_module.id;
        let fragment_module = object_ref!(
            desc.fragment_module,
            "invalid fragment shader module handle"
        );
        debug_assert!(Arc::ptr_eq(ctx, &fragment_module.ctx));
        let fragment_id = fragment_module.id;

        // --- Vertex layouts -------------------------------------
        if desc.vertex_layout_count > 0 && desc.vertex_layouts.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null vertex layout array");
            return AlcoStatus::INVALID_ARGUMENT;
        }
        let layouts = if desc.vertex_layouts.is_null() {
            &[][..]
        } else {
            std::slice::from_raw_parts(desc.vertex_layouts, desc.vertex_layout_count as usize)
        };
        let mut attribute_count = 0usize;
        for layout in layouts {
            if !layout.elements.is_null() {
                attribute_count += layout.element_count as usize;
            }
        }
        let mut wattributes = DescriptorStorage::<_, 32>::new(attribute_count);
        for layout in layouts {
            let elements = if layout.elements.is_null() || layout.element_count == 0 {
                &[][..]
            } else {
                std::slice::from_raw_parts(layout.elements, layout.element_count as usize)
            };
            for element in elements {
                let format = match vertex_format(element.format) {
                    Ok(f) => f,
                    Err(s) => return s,
                };
                wattributes.push(wgt::VertexAttribute {
                    format,
                    offset: element.offset as u64,
                    shader_location: element.location,
                });
            }
        }
        let mut wbuffers = DescriptorStorage::<_, 8>::new(layouts.len());
        let mut attribute_offset = 0;
        for layout in layouts {
            let count = if layout.elements.is_null() {
                0
            } else {
                layout.element_count as usize
            };
            let step_mode = match vertex_step_mode(layout.step_mode) {
                Ok(m) => m,
                Err(s) => return s,
            };
            wbuffers.push(Some(wgc::pipeline::VertexBufferLayout {
                array_stride: layout.stride as u64,
                step_mode,
                attributes: Cow::Borrowed(
                    &wattributes.as_slice()[attribute_offset..attribute_offset + count],
                ),
            }));
            attribute_offset += count;
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
            Err(s) => return s,
        };
        let alpha_blend = match blend(&desc.blend.alpha) {
            Ok(b) => b,
            Err(s) => return s,
        };

        if desc.color_format_count > 0 && desc.color_formats.is_null() {
            set_error(AlcoStatus::INVALID_ARGUMENT, "null color format array");
            return AlcoStatus::INVALID_ARGUMENT;
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
        let mut targets = DescriptorStorage::<_, 8>::new(formats.len());
        for (i, &format) in formats.iter().enumerate() {
            let format = match pixel_format(format) {
                Ok(f) => f,
                Err(s) => return s,
            };
            // WebGPU rejects a color target without a matching fragment
            // output unless its write mask is zero, so extra targets
            // (e.g. an MRT buffer a shader does not write) are masked
            // out instead of failing pipeline validation.
            let writes_target = i < output_count;
            targets.push(Some(wgt::ColorTargetState {
                format,
                blend: writes_target.then_some(wgt::BlendState {
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
            Err(s) => return s,
        };
        // Strip topologies draw indexed with primitive restart, which
        // the old backend always declared as Uint32.
        let is_strip = topology == wgt::PrimitiveTopology::TriangleStrip
            || topology == wgt::PrimitiveTopology::LineStrip;
        // Line list rasterization has no meaningful face winding to cull.
        let cull = if topology == wgt::PrimitiveTopology::LineList {
            None
        } else {
            match cull_mode(desc.cull_mode) {
                Ok(c) => c,
                Err(s) => return s,
            }
        };
        let front_face = match front_face(desc.front_face) {
            Ok(f) => f,
            Err(s) => return s,
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
        let depth_stencil =
            if desc.depth_stencil_format != ALCO_NONE && desc.depth_stencil_format != 0 {
                let format = match pixel_format(desc.depth_stencil_format) {
                    Ok(f) => f,
                    Err(s) => return s,
                };
                let front = match stencil_face(&desc.depth_stencil.front) {
                    Ok(f) => f,
                    Err(s) => return s,
                };
                let back = match stencil_face(&desc.depth_stencil.back) {
                    Ok(b) => b,
                    Err(s) => return s,
                };
                let depth_compare = match compare_function(desc.depth_stencil.depth_compare) {
                    Ok(c) => c,
                    Err(s) => return s,
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
            Err(s) => return s,
        };

        let vertex_entry = borrow_label(desc.vertex_entry);
        let fragment_entry = borrow_label(desc.fragment_entry);
        let wdesc = wgc::pipeline::RenderPipelineDescriptor {
            label: label(desc.name),
            layout: Some(layout_id),
            vertex: wgc::pipeline::VertexState {
                stage: programmable_stage(vertex_id, &vertex_entry),
                buffers: Cow::Borrowed(wbuffers.as_slice()),
            },
            primitive,
            depth_stencil,
            multisample: wgt::MultisampleState {
                count: 1,
                mask: !0,
                alpha_to_coverage_enabled: false,
            },
            fragment: Some(wgc::pipeline::FragmentState {
                stage: programmable_stage(fragment_id, &fragment_entry),
                targets: Cow::Borrowed(targets.as_slice()),
            }),
            multiview_mask: None,
            cache: None,
        };

        let (id, err) = ctx
            .global
            .device_create_render_pipeline(ctx.device_id, &wdesc, None);
        ctx.global.pipeline_layout_drop(layout_id);
        if let Some(e) = err {
            set_error_from(AlcoStatus::VALIDATION, &e);
            ctx.global.render_pipeline_drop(id);
            return AlcoStatus::VALIDATION;
        }
        let handle = AlcoGraphicsPipelineHandle::new(GraphicsPipelineObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        AlcoStatus::OK
    })
}

/// ABI: creates a compute pipeline.
///
/// # Safety
/// All non-null pointers and typed handles must remain valid for the call.
/// Callers must order object destruction and exclusively own mutable mapping access.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_compute_pipeline_create(
    device: AlcoDeviceHandle,
    desc: *const AlcoComputePipelineDesc,
    out: *mut AlcoComputePipelineHandle,
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
        let ctx = &object_ref!(device, "invalid device handle").ctx;
        let compute_module =
            object_ref!(desc.compute_module, "invalid compute shader module handle");
        debug_assert!(Arc::ptr_eq(ctx, &compute_module.ctx));
        let compute_id = compute_module.id;
        let layout_id = match create_pipeline_layout(
            ctx,
            desc.bind_group_layouts,
            desc.bind_group_layout_count,
            desc.immediate_size,
            desc.name,
        ) {
            Ok(id) => id,
            Err(s) => return s,
        };
        let compute_entry = borrow_label(desc.compute_entry);
        let wdesc = wgc::pipeline::ComputePipelineDescriptor {
            label: label(desc.name),
            layout: Some(layout_id),
            stage: programmable_stage(compute_id, &compute_entry),
            cache: None,
        };
        let (id, err) = ctx
            .global
            .device_create_compute_pipeline(ctx.device_id, &wdesc, None);
        ctx.global.pipeline_layout_drop(layout_id);
        if let Some(e) = err {
            set_error_from(AlcoStatus::VALIDATION, &e);
            ctx.global.compute_pipeline_drop(id);
            return AlcoStatus::VALIDATION;
        }
        let handle = AlcoComputePipelineHandle::new(ComputePipelineObj {
            id,
            ctx: Arc::clone(ctx),
        });
        *out = handle;
        AlcoStatus::OK
    })
}

/// ABI: consumes a graphics pipeline handle and unregisters its core identity.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_graphics_pipeline_destroy(
    pipeline: AlcoGraphicsPipelineHandle,
) -> AlcoStatus {
    crate::entry::guard(|| match pipeline.take() {
        Ok(obj) => {
            drop(obj);
            AlcoStatus::OK
        }
        Err(status) => {
            set_error(status, "invalid graphics pipeline handle");
            status
        }
    })
}

/// ABI: consumes a compute pipeline handle and unregisters its core identity.
///
/// # Safety
/// The typed handle must be live, consumed exactly once, and not borrowed during destruction.
/// Non-null invalid or previously freed pointers violate the ABI contract.
#[no_mangle]
pub unsafe extern "C-unwind" fn alco_compute_pipeline_destroy(
    pipeline: AlcoComputePipelineHandle,
) -> AlcoStatus {
    crate::entry::guard(|| match pipeline.take() {
        Ok(obj) => {
            drop(obj);
            AlcoStatus::OK
        }
        Err(status) => {
            set_error(status, "invalid compute pipeline handle");
            status
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::objects::{
        alco_shader_module_create, alco_shader_module_destroy, shader_language,
        AlcoShaderModuleDesc,
    };
    use crate::test_support::{last_error, TestDevice};
    use std::ptr;

    #[test]
    fn distinct_pipeline_destroy_exports_reject_null_handles() {
        unsafe {
            assert_eq!(
                alco_graphics_pipeline_destroy(AlcoGraphicsPipelineHandle::NULL),
                AlcoStatus::INVALID_HANDLE
            );
            assert_eq!(
                alco_compute_pipeline_destroy(AlcoComputePipelineHandle::NULL),
                AlcoStatus::INVALID_HANDLE
            );
        }
    }

    #[test]
    fn vulkan_compute_pipeline_retains_context_and_borrows_entry_point() {
        let Some(device) = TestDevice::new() else {
            return;
        };
        unsafe {
            let source = b"@compute @workgroup_size(1) fn main() {}";
            let module_desc = AlcoShaderModuleDesc {
                language: shader_language::WGSL,
                data: source.as_ptr(),
                size: source.len() as u32,
                entry_point: c"main".as_ptr(),
                workgroup_x: 1,
                workgroup_y: 1,
                workgroup_z: 1,
                name: ptr::null(),
                flags: 0,
            };
            let mut module = AlcoShaderModuleHandle::NULL;
            assert_eq!(
                alco_shader_module_create(device.handle, &module_desc, &mut module),
                AlcoStatus::OK,
                "{}",
                last_error()
            );
            let entry = borrow_label(module_desc.entry_point);
            let stage = programmable_stage(module.get().unwrap().id, &entry);
            let converted = stage.entry_point.as_ref().unwrap();
            assert!(matches!(converted, Cow::Borrowed(_)));
            assert_eq!(converted.as_ptr(), module_desc.entry_point.cast());
            let weak = Arc::downgrade(&device.handle.get().unwrap().ctx);
            let desc = AlcoComputePipelineDesc {
                bind_group_layouts: ptr::null(),
                bind_group_layout_count: 0,
                compute_module: module,
                compute_entry: c"main".as_ptr(),
                immediate_size: 0,
                name: ptr::null(),
            };
            let mut pipeline = AlcoComputePipelineHandle::NULL;
            assert_eq!(
                alco_compute_pipeline_create(device.handle, &desc, &mut pipeline),
                AlcoStatus::OK,
                "{}",
                last_error()
            );
            assert_eq!(weak.strong_count(), 3);
            let hub = device
                .handle
                .get()
                .unwrap()
                .ctx
                .global
                .generate_report()
                .hub;
            assert_eq!(hub.pipeline_layouts.num_allocated, 0);
            assert_eq!(hub.compute_pipelines.num_allocated, 1);
            assert_eq!(alco_shader_module_destroy(module), AlcoStatus::OK);
            drop(device);
            assert_eq!(weak.strong_count(), 1);
            assert_eq!(
                alco_compute_pipeline_destroy(pipeline),
                AlcoStatus::OK,
                "{}",
                last_error()
            );
            assert!(weak.upgrade().is_none());
        }
    }
}
