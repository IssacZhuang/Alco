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
        uint version = AbiVersion();
        uint major = version >> 16;
        if (major != AlcoGpuAbi.AbiMajor)
        {
            throw new GraphicsException(
                $"alco-gpu ABI major {major} is incompatible with required ABI {AlcoGpuAbi.AbiMajor}.{AlcoGpuAbi.AbiMinor}; rebuild the native library before using this backend.");
        }
        SetErrorCallback(&AlcoGpuMarshal.OnNativeError, null);
    }

    /// <summary>Provides the AbiVersion operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_abi_version")]
    public static partial uint AbiVersion();

    /// <summary>Provides the BuildInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_build_info")]
    public static partial void BuildInfo(ref AlcoBuildInfo info);

    /// <summary>Provides the GetLastError operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_get_last_error")]
    public static partial void GetLastError(ref AlcoErrorInfo info);

    /// <summary>
    /// Registers the process-wide error callback; null unregisters. The
    /// callback fires synchronously on the calling thread for every failure
    /// status, with the message borrowed until the next failure on the same thread.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_set_error_callback")]
    public static partial void SetErrorCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    /// <summary>
    /// Registers the process-wide native log callback (null unregisters) and
    /// installs the native log forwarder on first use. Records fire
    /// synchronously from inside wgpu-core, so the callback must never throw;
    /// the message is borrowed for the duration of the call only. Fails with
    /// <see cref="AlcoGpuAbi.Status.Unsupported"/> (thrown as
    /// <see cref="GraphicsException"/>) when another library already owns the
    /// process-wide logger.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_set_log_callback")]
    public static partial uint SetLogCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    /// <summary>
    /// Sets the maximum level forwarded to the native log callback; invalid
    /// values throw <see cref="GraphicsException"/> through the error
    /// callback.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_set_log_level")]
    public static partial uint SetLogLevel(uint level);

    /// <summary>Provides the DeviceCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_device_create")]
    public static partial uint DeviceCreate(in AlcoDeviceDesc desc, out AlcoDeviceHandle device);

    /// <summary>Provides the DeviceDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_device_destroy")]
    public static partial uint DeviceDestroy(AlcoDeviceHandle device);

    /// <summary>Provides the DeviceGetInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_device_get_info")]
    public static partial uint DeviceGetInfo(AlcoDeviceHandle device, ref AlcoDeviceInfo info);

    /// <summary>Provides the DevicePoll operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_device_poll")]
    public static partial uint DevicePoll(AlcoDeviceHandle device, uint wait, ulong submitIndex, uint* queueEmpty);

    /// <summary>Provides the DevicePopMessage operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_device_pop_message")]
    public static partial uint DevicePopMessage(AlcoDeviceHandle device, ref AlcoDeviceMessage message);

    // ------------------------------------------------------------------
    // Buffers
    // ------------------------------------------------------------------

    /// <summary>Provides the BufferCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_create")]
    public static partial uint BufferCreate(AlcoDeviceHandle device, in AlcoBufferDesc desc, out AlcoBufferHandle buffer);

    /// <summary>Provides the BufferDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_destroy")]
    public static partial uint BufferDestroy(AlcoBufferHandle buffer);

    /// <summary>Provides the BufferMapRead operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_map_read")]
    public static partial uint BufferMapRead(AlcoBufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the BufferMapPoll operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_map_poll")]
    public static partial uint BufferMapPoll(AlcoBufferHandle buffer);

    /// <summary>Provides the BufferGetMappedRange operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_get_mapped_range")]
    public static partial uint BufferGetMappedRange(AlcoBufferHandle buffer, ulong offset, ulong size, void** mapped);

    /// <summary>Provides the BufferUnmap operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_buffer_unmap")]
    public static partial uint BufferUnmap(AlcoBufferHandle buffer);

    // ------------------------------------------------------------------
    // Textures / views / samplers
    // ------------------------------------------------------------------

    /// <summary>Provides the TextureCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_create")]
    public static partial uint TextureCreate(AlcoDeviceHandle device, in AlcoTextureDesc desc, out AlcoTextureHandle texture);

    /// <summary>Provides the TextureDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_destroy")]
    public static partial uint TextureDestroy(AlcoTextureHandle texture);

    /// <summary>Provides the TextureGetInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_get_info")]
    public static partial uint TextureGetInfo(AlcoTextureHandle texture, ref AlcoTextureInfo info);

    /// <summary>Provides the TextureCreateView operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_create_view")]
    public static partial uint TextureCreateView(AlcoTextureHandle texture, AlcoTextureViewDesc* desc, out AlcoTextureViewHandle view);

    /// <summary>Provides the TextureViewDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_view_destroy")]
    public static partial uint TextureViewDestroy(AlcoTextureViewHandle view);

    /// <summary>Provides the SamplerCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_sampler_create")]
    public static partial uint SamplerCreate(AlcoDeviceHandle device, in AlcoSamplerDesc desc, out AlcoSamplerHandle sampler);

    /// <summary>Provides the SamplerDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_sampler_destroy")]
    public static partial uint SamplerDestroy(AlcoSamplerHandle sampler);

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_texture_release")]
    public static partial uint TextureRelease(AlcoTextureHandle texture);

    // ------------------------------------------------------------------
    // Shader modules
    // ------------------------------------------------------------------

    /// <summary>Provides the ShaderModuleCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_shader_module_create")]
    public static partial uint ShaderModuleCreate(AlcoDeviceHandle device, in AlcoShaderModuleDesc desc, out AlcoShaderModuleHandle module);

    /// <summary>Provides the ShaderModuleDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_shader_module_destroy")]
    public static partial uint ShaderModuleDestroy(AlcoShaderModuleHandle module);

    // ------------------------------------------------------------------
    // Bind group layouts / bind groups
    // ------------------------------------------------------------------

    /// <summary>Provides the BindGroupLayoutCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_layout_create")]
    public static partial uint BindGroupLayoutCreate(AlcoDeviceHandle device, in AlcoBindGroupLayoutDesc desc, out AlcoBindGroupLayoutHandle layout);

    /// <summary>Provides the BindGroupLayoutDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_layout_destroy")]
    public static partial uint BindGroupLayoutDestroy(AlcoBindGroupLayoutHandle layout);

    /// <summary>Provides the BindGroupCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_create")]
    public static partial uint BindGroupCreate(AlcoDeviceHandle device, in AlcoBindGroupDesc desc, out AlcoBindGroupHandle group);

    /// <summary>Provides the BindGroupDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bind_group_destroy")]
    public static partial uint BindGroupDestroy(AlcoBindGroupHandle group);

    // ------------------------------------------------------------------
    // Pipelines
    // ------------------------------------------------------------------

    /// <summary>Provides the GraphicsPipelineCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_graphics_pipeline_create")]
    public static partial uint GraphicsPipelineCreate(AlcoDeviceHandle device, in AlcoGraphicsPipelineDesc desc, out AlcoGraphicsPipelineHandle pipeline);

    /// <summary>Provides the ComputePipelineCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pipeline_create")]
    public static partial uint ComputePipelineCreate(AlcoDeviceHandle device, in AlcoComputePipelineDesc desc, out AlcoComputePipelineHandle pipeline);

    /// <summary>Provides the GraphicsPipelineDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_graphics_pipeline_destroy")]
    public static partial uint GraphicsPipelineDestroy(AlcoGraphicsPipelineHandle pipeline);

    /// <summary>Provides the ComputePipelineDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pipeline_destroy")]
    public static partial uint ComputePipelineDestroy(AlcoComputePipelineHandle pipeline);

    // ------------------------------------------------------------------
    // Command encoding
    // ------------------------------------------------------------------

    /// <summary>Provides the EncoderCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_create")]
    public static partial uint EncoderCreate(AlcoDeviceHandle device, byte* name, out AlcoEncoderHandle encoder);

    /// <summary>Provides the EncoderFinish operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_finish")]
    public static partial uint EncoderFinish(AlcoEncoderHandle encoder, out AlcoCommandBufferHandle commandBuffer);

    /// <summary>Provides the EncoderDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_encoder_destroy")]
    public static partial uint EncoderDestroy(AlcoEncoderHandle encoder);

    /// <summary>Provides the CommandBufferDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_command_buffer_destroy")]
    public static partial uint CommandBufferDestroy(AlcoCommandBufferHandle commandBuffer);

    /// <summary>Provides the RenderPassBegin operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_begin")]
    public static partial uint RenderPassBegin(AlcoEncoderHandle encoder, in AlcoRenderPassDesc desc, out AlcoRenderPassHandle renderPass);

    /// <summary>Provides the RenderPassEnd operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_end")]
    public static partial uint RenderPassEnd(AlcoRenderPassHandle renderPass);

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_release")]
    public static partial uint RenderPassRelease(AlcoRenderPassHandle renderPass);

    /// <summary>Provides the RenderPassSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_pipeline")]
    public static partial uint RenderPassSetPipeline(AlcoRenderPassHandle renderPass, AlcoGraphicsPipelineHandle pipeline);

    /// <summary>Provides the RenderPassSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_bind_group")]
    public static partial uint RenderPassSetBindGroup(AlcoRenderPassHandle renderPass, uint slot, AlcoBindGroupHandle group);

    /// <summary>Provides the RenderPassSetVertexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_vertex_buffer")]
    public static partial uint RenderPassSetVertexBuffer(AlcoRenderPassHandle renderPass, uint slot, AlcoBufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the RenderPassSetIndexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_index_buffer")]
    public static partial uint RenderPassSetIndexBuffer(AlcoRenderPassHandle renderPass, AlcoBufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Provides the RenderPassSetScissorRect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_scissor_rect")]
    public static partial uint RenderPassSetScissorRect(AlcoRenderPassHandle renderPass, uint x, uint y, uint width, uint height);

    /// <summary>Provides the RenderPassSetStencilReference operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_stencil_reference")]
    public static partial uint RenderPassSetStencilReference(AlcoRenderPassHandle renderPass, uint reference);

    /// <summary>Provides the RenderPassSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_set_immediates")]
    public static partial uint RenderPassSetImmediates(AlcoRenderPassHandle renderPass, uint offset, byte* data, uint size);

    /// <summary>Provides the RenderPassDraw operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw")]
    public static partial uint RenderPassDraw(AlcoRenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Provides the RenderPassDrawIndexed operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indexed")]
    public static partial uint RenderPassDrawIndexed(AlcoRenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Provides the RenderPassDrawIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indirect")]
    public static partial uint RenderPassDrawIndirect(AlcoRenderPassHandle renderPass, AlcoBufferHandle buffer, ulong offset);

    /// <summary>Provides the RenderPassDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_draw_indexed_indirect")]
    public static partial uint RenderPassDrawIndexedIndirect(AlcoRenderPassHandle renderPass, AlcoBufferHandle buffer, ulong offset);

    /// <summary>Provides the RenderPassMultiDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_multi_draw_indexed_indirect")]
    public static partial uint RenderPassMultiDrawIndexedIndirect(AlcoRenderPassHandle renderPass, AlcoBufferHandle buffer, ulong offset, uint count);

    /// <summary>Provides the RenderPassWriteTimestamp operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_write_timestamp")]
    public static partial uint RenderPassWriteTimestamp(AlcoRenderPassHandle renderPass, AlcoQuerySetHandle querySet, uint queryIndex);

    /// <summary>Provides the RenderPassExecuteBundles operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_pass_execute_bundles")]
    public static partial uint RenderPassExecuteBundles(AlcoRenderPassHandle renderPass, AlcoRenderBundleHandle* bundles, uint count);

    /// <summary>Provides the ComputePassBegin operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_begin")]
    public static partial uint ComputePassBegin(AlcoEncoderHandle encoder, AlcoTimestampWrites* timestampWrites, out AlcoComputePassHandle computePass);

    /// <summary>Provides the ComputePassEnd operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_end")]
    public static partial uint ComputePassEnd(AlcoComputePassHandle computePass);

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_release")]
    public static partial uint ComputePassRelease(AlcoComputePassHandle computePass);

    /// <summary>Provides the ComputePassSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_pipeline")]
    public static partial uint ComputePassSetPipeline(AlcoComputePassHandle computePass, AlcoComputePipelineHandle pipeline);

    /// <summary>Provides the ComputePassSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_bind_group")]
    public static partial uint ComputePassSetBindGroup(AlcoComputePassHandle computePass, uint slot, AlcoBindGroupHandle group);

    /// <summary>Provides the ComputePassSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_set_immediates")]
    public static partial uint ComputePassSetImmediates(AlcoComputePassHandle computePass, uint offset, byte* data, uint size);

    /// <summary>Provides the ComputePassDispatchWorkgroups operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_dispatch_workgroups")]
    public static partial uint ComputePassDispatchWorkgroups(AlcoComputePassHandle computePass, uint x, uint y, uint z);

    /// <summary>Provides the ComputePassDispatchWorkgroupsIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_dispatch_workgroups_indirect")]
    public static partial uint ComputePassDispatchWorkgroupsIndirect(AlcoComputePassHandle computePass, AlcoBufferHandle buffer, ulong offset);

    /// <summary>Provides the ComputePassWriteTimestamp operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_compute_pass_write_timestamp")]
    public static partial uint ComputePassWriteTimestamp(AlcoComputePassHandle computePass, AlcoQuerySetHandle querySet, uint queryIndex);

    // ------------------------------------------------------------------
    // Copies / queries
    // ------------------------------------------------------------------

    /// <summary>Provides the CopyBufferToBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_copy_buffer_to_buffer")]
    public static partial uint CopyBufferToBuffer(AlcoEncoderHandle encoder, AlcoBufferHandle source, ulong sourceOffset, AlcoBufferHandle destination, ulong destinationOffset, ulong size);

    /// <summary>Provides the CopyBufferToTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_copy_buffer_to_texture")]
    public static partial uint CopyBufferToTexture(AlcoEncoderHandle encoder, AlcoBufferHandle source, in AlcoCopyLayout sourceLayout, AlcoTextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoExtent3D copySize);

    /// <summary>Provides the CopyTextureToBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_copy_texture_to_buffer")]
    public static partial uint CopyTextureToBuffer(AlcoEncoderHandle encoder, AlcoTextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoBufferHandle destination, in AlcoCopyLayout destinationLayout, AlcoExtent3D copySize);

    /// <summary>Provides the CopyTextureToTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_copy_texture_to_texture")]
    public static partial uint CopyTextureToTexture(AlcoEncoderHandle encoder, AlcoTextureHandle source, uint sourceMipLevel, AlcoTextureHandle destination, uint destinationMipLevel, uint aspect, AlcoExtent3D copySize);

    /// <summary>Provides the ResolveQuerySet operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_resolve_query_set")]
    public static partial uint ResolveQuerySet(AlcoEncoderHandle encoder, AlcoQuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoBufferHandle destination, ulong destinationOffset);

    /// <summary>Provides the QuerySetCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_query_set_create")]
    public static partial uint QuerySetCreate(AlcoDeviceHandle device, uint count, byte* name, out AlcoQuerySetHandle querySet);

    /// <summary>Provides the QuerySetDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_query_set_destroy")]
    public static partial uint QuerySetDestroy(AlcoQuerySetHandle querySet);

    // ------------------------------------------------------------------
    // Queue
    // ------------------------------------------------------------------

    /// <summary>Provides the QueueWriteBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_queue_write_buffer")]
    public static partial uint QueueWriteBuffer(AlcoDeviceHandle device, AlcoBufferHandle buffer, ulong offset, byte* data, uint size);

    /// <summary>Provides the QueueWriteTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_queue_write_texture")]
    public static partial uint QueueWriteTexture(AlcoDeviceHandle device, AlcoTextureHandle texture, uint mipLevel, AlcoOrigin3D origin, uint aspect, byte* data, uint dataSize, in AlcoCopyLayout layout, AlcoExtent3D size);

    /// <summary>Submits a command buffer; the native side consumes the handle.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_queue_submit")]
    public static partial uint QueueSubmit(AlcoDeviceHandle device, AlcoCommandBufferHandle commandBuffer, ulong* outIndex);

    // ------------------------------------------------------------------
    // Render bundles
    // ------------------------------------------------------------------

    /// <summary>Provides the BundleEncoderCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_create")]
    public static partial uint BundleEncoderCreate(AlcoDeviceHandle device, in AlcoBundleEncoderDesc desc, out AlcoBundleEncoderHandle bundleEncoder);

    /// <summary>Provides the BundleEncoderFinish operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_finish")]
    public static partial uint BundleEncoderFinish(AlcoBundleEncoderHandle bundleEncoder, out AlcoRenderBundleHandle bundle);

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_encoder_destroy")]
    public static partial uint BundleEncoderDestroy(AlcoBundleEncoderHandle bundleEncoder);

    /// <summary>Provides the RenderBundleDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_render_bundle_destroy")]
    public static partial uint RenderBundleDestroy(AlcoRenderBundleHandle bundle);

    /// <summary>Provides the BundleSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_pipeline")]
    public static partial uint BundleSetPipeline(AlcoBundleEncoderHandle bundleEncoder, AlcoGraphicsPipelineHandle pipeline);

    /// <summary>Provides the BundleSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_bind_group")]
    public static partial uint BundleSetBindGroup(AlcoBundleEncoderHandle bundleEncoder, uint slot, AlcoBindGroupHandle group);

    /// <summary>Provides the BundleSetVertexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_vertex_buffer")]
    public static partial uint BundleSetVertexBuffer(AlcoBundleEncoderHandle bundleEncoder, uint slot, AlcoBufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the BundleSetIndexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_index_buffer")]
    public static partial uint BundleSetIndexBuffer(AlcoBundleEncoderHandle bundleEncoder, AlcoBufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Provides the BundleSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_set_immediates")]
    public static partial uint BundleSetImmediates(AlcoBundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size);

    /// <summary>Provides the BundleDraw operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw")]
    public static partial uint BundleDraw(AlcoBundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Provides the BundleDrawIndexed operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indexed")]
    public static partial uint BundleDrawIndexed(AlcoBundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Provides the BundleDrawIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indirect")]
    public static partial uint BundleDrawIndirect(AlcoBundleEncoderHandle bundleEncoder, AlcoBufferHandle buffer, ulong offset);

    /// <summary>Provides the BundleDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_bundle_draw_indexed_indirect")]
    public static partial uint BundleDrawIndexedIndirect(AlcoBundleEncoderHandle bundleEncoder, AlcoBufferHandle buffer, ulong offset);

    // ------------------------------------------------------------------
    // Surfaces
    // ------------------------------------------------------------------

    /// <summary>Provides the SurfaceCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_create")]
    public static partial uint SurfaceCreate(AlcoDeviceHandle device, in AlcoSurfaceDesc desc, out AlcoSurfaceHandle surface);

    /// <summary>Provides the SurfaceGetCapabilities operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_get_capabilities")]
    public static partial uint SurfaceGetCapabilities(AlcoSurfaceHandle surface, ref AlcoSurfaceCaps caps);

    /// <summary>Provides the SurfaceConfigure operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_configure")]
    public static partial uint SurfaceConfigure(AlcoSurfaceHandle surface, in AlcoSurfaceConfig config);

    /// <summary>Provides the SurfaceGetCurrentTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_get_current_texture")]
    public static partial uint SurfaceGetCurrentTexture(AlcoSurfaceHandle surface, out AlcoTextureHandle texture, uint* status);

    /// <summary>Provides the SurfacePresent operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_present")]
    public static partial uint SurfacePresent(AlcoSurfaceHandle surface, uint* status);

    /// <summary>Provides the SurfaceDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "alco_surface_destroy")]
    public static partial uint SurfaceDestroy(AlcoSurfaceHandle surface);
}
