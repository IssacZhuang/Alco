using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// P/Invoke surface of the alco-gpu native library. Fallible entries return a
/// status code from <see cref="AlcoGpuAbi.Status"/>, but the process-wide
/// error callback registered at load time throws a
/// <see cref="GraphicsException"/> from the native call site on every
/// failure, so managed callers never observe failure status returns
/// (<see cref="AlcoGpuAbi.Status.NotReady"/> is control flow and never fires).
/// </summary>
internal static unsafe partial class AlcoGpuNative
{
    private const string LibraryName = "alco_gpu";

    /// <summary>
    /// Ensures the native library is resolvable (probes the app directory first)
    /// and registers the throwing error callback so all failures — including
    /// device creation itself — surface as managed exceptions.
    /// </summary>
    static AlcoGpuNative()
    {
        AlcoGpuNativeLibrary.EnsureLoaded();
        SetErrorCallback(&AlcoGpuMarshal.OnNativeError, null);
    }

    [LibraryImport(LibraryName, EntryPoint = "alco_abi_version")]
    public static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "alco_build_info")]
    public static partial void BuildInfo(ref AlcoBuildInfo info);

    [LibraryImport(LibraryName, EntryPoint = "alco_get_last_error")]
    public static partial void GetLastError(ref AlcoErrorInfo info);

    /// <summary>
    /// Registers the process-wide error callback; null unregisters. The
    /// callback fires synchronously on the calling thread for every failure
    /// status, with the message borrowed until the next alco call.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_set_error_callback")]
    public static partial void SetErrorCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    [LibraryImport(LibraryName, EntryPoint = "alco_device_create")]
    public static partial uint DeviceCreate(in AlcoDeviceDesc desc, out AlcoHandle device);

    [LibraryImport(LibraryName, EntryPoint = "alco_device_destroy")]
    public static partial uint DeviceDestroy(AlcoHandle device);

    [LibraryImport(LibraryName, EntryPoint = "alco_device_get_info")]
    public static partial uint DeviceGetInfo(AlcoHandle device, ref AlcoDeviceInfo info);

    [LibraryImport(LibraryName, EntryPoint = "alco_device_poll")]
    public static partial uint DevicePoll(AlcoHandle device, uint wait, ulong submitIndex, uint* queueEmpty);

    [LibraryImport(LibraryName, EntryPoint = "alco_device_pop_message")]
    public static partial uint DevicePopMessage(AlcoHandle device, ref AlcoDeviceMessage message);

    // ------------------------------------------------------------------
    // Buffers
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_create")]
    public static partial uint BufferCreate(AlcoHandle device, in AlcoBufferDesc desc, out AlcoHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_destroy")]
    public static partial uint BufferDestroy(AlcoHandle device, AlcoHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_map_read")]
    public static partial uint BufferMapRead(AlcoHandle device, AlcoHandle buffer, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_map_poll")]
    public static partial uint BufferMapPoll(AlcoHandle device, AlcoHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_get_mapped_range")]
    public static partial uint BufferGetMappedRange(AlcoHandle device, AlcoHandle buffer, ulong offset, ulong size, void** mapped);

    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_unmap")]
    public static partial uint BufferUnmap(AlcoHandle device, AlcoHandle buffer);

    // ------------------------------------------------------------------
    // Textures / views / samplers
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_texture_create")]
    public static partial uint TextureCreate(AlcoHandle device, in AlcoTextureDesc desc, out AlcoHandle texture);

    [LibraryImport(LibraryName, EntryPoint = "alco_texture_destroy")]
    public static partial uint TextureDestroy(AlcoHandle device, AlcoHandle texture);

    [LibraryImport(LibraryName, EntryPoint = "alco_texture_get_info")]
    public static partial uint TextureGetInfo(AlcoHandle device, AlcoHandle texture, ref AlcoTextureInfo info);

    [LibraryImport(LibraryName, EntryPoint = "alco_texture_create_view")]
    public static partial uint TextureCreateView(AlcoHandle device, AlcoHandle texture, AlcoTextureViewDesc* desc, out AlcoHandle view);

    [LibraryImport(LibraryName, EntryPoint = "alco_texture_view_destroy")]
    public static partial uint TextureViewDestroy(AlcoHandle device, AlcoHandle view);

    [LibraryImport(LibraryName, EntryPoint = "alco_sampler_create")]
    public static partial uint SamplerCreate(AlcoHandle device, in AlcoSamplerDesc desc, out AlcoHandle sampler);

    [LibraryImport(LibraryName, EntryPoint = "alco_sampler_destroy")]
    public static partial uint SamplerDestroy(AlcoHandle device, AlcoHandle sampler);

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_release")]
    public static partial uint TextureRelease(AlcoHandle device, AlcoHandle texture);

    // ------------------------------------------------------------------
    // Shader modules
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_shader_module_create")]
    public static partial uint ShaderModuleCreate(AlcoHandle device, in AlcoShaderModuleDesc desc, out AlcoHandle module);

    [LibraryImport(LibraryName, EntryPoint = "alco_shader_module_destroy")]
    public static partial uint ShaderModuleDestroy(AlcoHandle device, AlcoHandle module);

    // ------------------------------------------------------------------
    // Bind group layouts / bind groups
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_layout_create")]
    public static partial uint BindGroupLayoutCreate(AlcoHandle device, in AlcoBindGroupLayoutDesc desc, out AlcoHandle layout);

    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_layout_destroy")]
    public static partial uint BindGroupLayoutDestroy(AlcoHandle device, AlcoHandle layout);

    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_create")]
    public static partial uint BindGroupCreate(AlcoHandle device, in AlcoBindGroupDesc desc, out AlcoHandle group);

    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_destroy")]
    public static partial uint BindGroupDestroy(AlcoHandle device, AlcoHandle group);

    // ------------------------------------------------------------------
    // Pipelines
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_graphics_pipeline_create")]
    public static partial uint GraphicsPipelineCreate(AlcoHandle device, in AlcoGraphicsPipelineDesc desc, out AlcoHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pipeline_create")]
    public static partial uint ComputePipelineCreate(AlcoHandle device, in AlcoComputePipelineDesc desc, out AlcoHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "alco_pipeline_destroy")]
    public static partial uint PipelineDestroy(AlcoHandle device, AlcoHandle pipeline);

    // ------------------------------------------------------------------
    // Command encoding
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_create")]
    public static partial uint EncoderCreate(AlcoHandle device, byte* name, out AlcoHandle encoder);

    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_finish")]
    public static partial uint EncoderFinish(AlcoHandle device, AlcoHandle encoder, out AlcoHandle commandBuffer);

    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_destroy")]
    public static partial uint EncoderDestroy(AlcoHandle device, AlcoHandle encoder);

    [LibraryImport(LibraryName, EntryPoint = "alco_command_buffer_destroy")]
    public static partial uint CommandBufferDestroy(AlcoHandle device, AlcoHandle commandBuffer);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_begin")]
    public static partial uint RenderPassBegin(AlcoHandle device, AlcoHandle encoder, in AlcoRenderPassDesc desc, out AlcoHandle renderPass);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_end")]
    public static partial uint RenderPassEnd(AlcoHandle device, AlcoHandle renderPass);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_pipeline")]
    public static partial uint RenderPassSetPipeline(AlcoHandle device, AlcoHandle renderPass, AlcoHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_bind_group")]
    public static partial uint RenderPassSetBindGroup(AlcoHandle device, AlcoHandle renderPass, uint slot, AlcoHandle group);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_vertex_buffer")]
    public static partial uint RenderPassSetVertexBuffer(AlcoHandle device, AlcoHandle renderPass, uint slot, AlcoHandle buffer, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_index_buffer")]
    public static partial uint RenderPassSetIndexBuffer(AlcoHandle device, AlcoHandle renderPass, AlcoHandle buffer, uint format, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_scissor_rect")]
    public static partial uint RenderPassSetScissorRect(AlcoHandle device, AlcoHandle renderPass, uint x, uint y, uint width, uint height);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_stencil_reference")]
    public static partial uint RenderPassSetStencilReference(AlcoHandle device, AlcoHandle renderPass, uint reference);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_immediates")]
    public static partial uint RenderPassSetImmediates(AlcoHandle device, AlcoHandle renderPass, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw")]
    public static partial uint RenderPassDraw(AlcoHandle device, AlcoHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indexed")]
    public static partial uint RenderPassDrawIndexed(AlcoHandle device, AlcoHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indirect")]
    public static partial uint RenderPassDrawIndirect(AlcoHandle device, AlcoHandle renderPass, AlcoHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indexed_indirect")]
    public static partial uint RenderPassDrawIndexedIndirect(AlcoHandle device, AlcoHandle renderPass, AlcoHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_multi_draw_indexed_indirect")]
    public static partial uint RenderPassMultiDrawIndexedIndirect(AlcoHandle device, AlcoHandle renderPass, AlcoHandle buffer, ulong offset, uint count);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_write_timestamp")]
    public static partial uint RenderPassWriteTimestamp(AlcoHandle device, AlcoHandle renderPass, AlcoHandle querySet, uint queryIndex);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_execute_bundles")]
    public static partial uint RenderPassExecuteBundles(AlcoHandle device, AlcoHandle renderPass, AlcoHandle* bundles, uint count);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_begin")]
    public static partial uint ComputePassBegin(AlcoHandle device, AlcoHandle encoder, AlcoTimestampWrites* timestampWrites, out AlcoHandle computePass);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_end")]
    public static partial uint ComputePassEnd(AlcoHandle device, AlcoHandle computePass);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_pipeline")]
    public static partial uint ComputePassSetPipeline(AlcoHandle device, AlcoHandle computePass, AlcoHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_bind_group")]
    public static partial uint ComputePassSetBindGroup(AlcoHandle device, AlcoHandle computePass, uint slot, AlcoHandle group);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_immediates")]
    public static partial uint ComputePassSetImmediates(AlcoHandle device, AlcoHandle computePass, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_dispatch_workgroups")]
    public static partial uint ComputePassDispatchWorkgroups(AlcoHandle device, AlcoHandle computePass, uint x, uint y, uint z);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_dispatch_workgroups_indirect")]
    public static partial uint ComputePassDispatchWorkgroupsIndirect(AlcoHandle device, AlcoHandle computePass, AlcoHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_write_timestamp")]
    public static partial uint ComputePassWriteTimestamp(AlcoHandle device, AlcoHandle computePass, AlcoHandle querySet, uint queryIndex);

    // ------------------------------------------------------------------
    // Copies / queries
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_copy_buffer_to_buffer")]
    public static partial uint CopyBufferToBuffer(AlcoHandle device, AlcoHandle encoder, AlcoHandle source, ulong sourceOffset, AlcoHandle destination, ulong destinationOffset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_copy_buffer_to_texture")]
    public static partial uint CopyBufferToTexture(AlcoHandle device, AlcoHandle encoder, AlcoHandle source, in AlcoCopyLayout sourceLayout, AlcoHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoExtent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "alco_copy_texture_to_buffer")]
    public static partial uint CopyTextureToBuffer(AlcoHandle device, AlcoHandle encoder, AlcoHandle source, uint sourceMipLevel, uint sourceAspect, AlcoHandle destination, in AlcoCopyLayout destinationLayout, AlcoExtent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "alco_copy_texture_to_texture")]
    public static partial uint CopyTextureToTexture(AlcoHandle device, AlcoHandle encoder, AlcoHandle source, uint sourceMipLevel, AlcoHandle destination, uint destinationMipLevel, uint aspect, AlcoExtent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "alco_resolve_query_set")]
    public static partial uint ResolveQuerySet(AlcoHandle device, AlcoHandle encoder, AlcoHandle querySet, uint firstQuery, uint queryCount, AlcoHandle destination, ulong destinationOffset);

    [LibraryImport(LibraryName, EntryPoint = "alco_query_set_create")]
    public static partial uint QuerySetCreate(AlcoHandle device, uint count, byte* name, out AlcoHandle querySet);

    [LibraryImport(LibraryName, EntryPoint = "alco_query_set_destroy")]
    public static partial uint QuerySetDestroy(AlcoHandle device, AlcoHandle querySet);

    // ------------------------------------------------------------------
    // Queue
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_queue_write_buffer")]
    public static partial uint QueueWriteBuffer(AlcoHandle device, AlcoHandle buffer, ulong offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "alco_queue_write_texture")]
    public static partial uint QueueWriteTexture(AlcoHandle device, AlcoHandle texture, uint mipLevel, AlcoOrigin3D origin, uint aspect, byte* data, uint dataSize, in AlcoCopyLayout layout, AlcoExtent3D size);

    /// <summary>Submits a command buffer; the native side consumes the handle.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_queue_submit")]
    public static partial uint QueueSubmit(AlcoHandle device, AlcoHandle commandBuffer, ulong* outIndex);

    // ------------------------------------------------------------------
    // Render bundles
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_create")]
    public static partial uint BundleEncoderCreate(AlcoHandle device, in AlcoBundleEncoderDesc desc, out AlcoHandle bundleEncoder);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_finish")]
    public static partial uint BundleEncoderFinish(AlcoHandle device, AlcoHandle bundleEncoder, out AlcoHandle bundle);

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_destroy")]
    public static partial uint BundleEncoderDestroy(AlcoHandle device, AlcoHandle bundleEncoder);

    [LibraryImport(LibraryName, EntryPoint = "alco_render_bundle_destroy")]
    public static partial uint RenderBundleDestroy(AlcoHandle device, AlcoHandle bundle);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_pipeline")]
    public static partial uint BundleSetPipeline(AlcoHandle device, AlcoHandle bundleEncoder, AlcoHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_bind_group")]
    public static partial uint BundleSetBindGroup(AlcoHandle device, AlcoHandle bundleEncoder, uint slot, AlcoHandle group);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_vertex_buffer")]
    public static partial uint BundleSetVertexBuffer(AlcoHandle device, AlcoHandle bundleEncoder, uint slot, AlcoHandle buffer, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_index_buffer")]
    public static partial uint BundleSetIndexBuffer(AlcoHandle device, AlcoHandle bundleEncoder, AlcoHandle buffer, uint format, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_immediates")]
    public static partial uint BundleSetImmediates(AlcoHandle device, AlcoHandle bundleEncoder, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw")]
    public static partial uint BundleDraw(AlcoHandle device, AlcoHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indexed")]
    public static partial uint BundleDrawIndexed(AlcoHandle device, AlcoHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indirect")]
    public static partial uint BundleDrawIndirect(AlcoHandle device, AlcoHandle bundleEncoder, AlcoHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indexed_indirect")]
    public static partial uint BundleDrawIndexedIndirect(AlcoHandle device, AlcoHandle bundleEncoder, AlcoHandle buffer, ulong offset);

    // ------------------------------------------------------------------
    // Surfaces
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_create")]
    public static partial uint SurfaceCreate(AlcoHandle device, in AlcoSurfaceDesc desc, out AlcoHandle surface);

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_get_capabilities")]
    public static partial uint SurfaceGetCapabilities(AlcoHandle device, AlcoHandle surface, ref AlcoSurfaceCaps caps);

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_configure")]
    public static partial uint SurfaceConfigure(AlcoHandle device, AlcoHandle surface, in AlcoSurfaceConfig config);

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_get_current_texture")]
    public static partial uint SurfaceGetCurrentTexture(AlcoHandle device, AlcoHandle surface, out AlcoHandle texture, uint* status);

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_present")]
    public static partial uint SurfacePresent(AlcoHandle device, AlcoHandle surface, uint* status);

    [LibraryImport(LibraryName, EntryPoint = "alco_surface_destroy")]
    public static partial uint SurfaceDestroy(AlcoHandle device, AlcoHandle surface);
}
