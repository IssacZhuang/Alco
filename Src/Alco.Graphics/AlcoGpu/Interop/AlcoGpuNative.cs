using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// P/Invoke surface of the alco-gpu native library. Fallible entries return a
/// status code from <see cref="AlcoGPU.Status"/>, but the process-wide
/// error callback registered at load time throws a
/// <see cref="GraphicsException"/> from the native call site on every
/// failure, so managed callers never observe failure status returns
/// (<see cref="AlcoGPU.Status.NotReady"/> is control flow and never fires).
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
        if (major != AlcoGPU.AbiMajor)
        {
            throw new GraphicsException(
                $"alco-gpu ABI major {major} is incompatible with required ABI {AlcoGPU.AbiMajor}.{AlcoGPU.AbiMinor}; rebuild the native library before using this backend.");
        }
        SetErrorCallback(&AlcoGpuMarshal.OnNativeError, null);
    }

    [LibraryImport(LibraryName, EntryPoint = "abi_version")]
    public static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "build_info")]
    public static partial void BuildInfo(ref AlcoGPU.BuildInfo info);

    [LibraryImport(LibraryName, EntryPoint = "get_last_error")]
    public static partial void GetLastError(ref AlcoGPU.ErrorInfo info);

    /// <summary>
    /// Registers the process-wide error callback; null unregisters. The
    /// callback fires synchronously on the calling thread for every failure
    /// status, with the message borrowed until the next failure on the same thread.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "set_error_callback")]
    public static partial void SetErrorCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    /// <summary>
    /// Registers the process-wide native log callback (null unregisters) and
    /// installs the native log forwarder on first use. Records fire
    /// synchronously from inside wgpu-core, so the callback must never throw;
    /// the message is borrowed for the duration of the call only. Fails with
    /// <see cref="AlcoGPU.Status.Unsupported"/> (thrown as
    /// <see cref="GraphicsException"/>) when another library already owns the
    /// process-wide logger.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "set_log_callback")]
    public static partial uint SetLogCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    /// <summary>
    /// Sets the maximum level forwarded to the native log callback; invalid
    /// values throw <see cref="GraphicsException"/> through the error
    /// callback.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "set_log_level")]
    public static partial uint SetLogLevel(uint level);

    [LibraryImport(LibraryName, EntryPoint = "device_create")]
    public static partial uint DeviceCreate(in AlcoGPU.DeviceDesc desc, out AlcoGPU.DeviceHandle device);

    [LibraryImport(LibraryName, EntryPoint = "device_destroy")]
    public static partial uint DeviceDestroy(AlcoGPU.DeviceHandle device);

    [LibraryImport(LibraryName, EntryPoint = "device_get_info")]
    public static partial uint DeviceGetInfo(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceInfo info);

    /// <summary>
    /// Polls the device. With <c>wait</c> set, blocks until the given submit
    /// index (<see cref="ulong.MaxValue"/> = latest submission) completes;
    /// otherwise performs a single non-blocking pump. A non-null
    /// <c>queueEmpty</c> receives whether the queue drained.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "device_poll")]
    public static partial uint DevicePoll(AlcoGPU.DeviceHandle device, uint wait, ulong submitIndex, uint* queueEmpty);

    [LibraryImport(LibraryName, EntryPoint = "device_pop_message")]
    public static partial uint DevicePopMessage(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceMessage message);

    // ------------------------------------------------------------------
    // Buffers
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_buffer")]
    public static partial uint BufferCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BufferDesc desc, out AlcoGPU.BufferHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "buffer_destroy")]
    public static partial uint BufferDestroy(AlcoGPU.BufferHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "buffer_map_read")]
    public static partial uint BufferMapRead(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Initiates a write map; the mapped range is writable until unmap.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_write")]
    public static partial uint BufferMapWrite(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "buffer_map_poll")]
    public static partial uint BufferMapPoll(AlcoGPU.BufferHandle buffer);

    [LibraryImport(LibraryName, EntryPoint = "buffer_get_mapped_range")]
    public static partial uint BufferGetMappedRange(AlcoGPU.BufferHandle buffer, ulong offset, ulong size, void** mapped);

    [LibraryImport(LibraryName, EntryPoint = "buffer_unmap")]
    public static partial uint BufferUnmap(AlcoGPU.BufferHandle buffer);

    // ------------------------------------------------------------------
    // Textures / views / samplers
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_texture")]
    public static partial uint TextureCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.TextureDesc desc, out AlcoGPU.TextureHandle texture);

    [LibraryImport(LibraryName, EntryPoint = "texture_destroy")]
    public static partial uint TextureDestroy(AlcoGPU.TextureHandle texture);

    [LibraryImport(LibraryName, EntryPoint = "texture_get_info")]
    public static partial uint TextureGetInfo(AlcoGPU.TextureHandle texture, ref AlcoGPU.TextureInfo info);

    /// <summary>Creates a texture view; a null <c>desc</c> creates the default view of the whole texture.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_create_view")]
    public static partial uint TextureCreateView(AlcoGPU.TextureHandle texture, AlcoGPU.TextureViewDesc* desc, out AlcoGPU.TextureViewHandle view);

    [LibraryImport(LibraryName, EntryPoint = "texture_view_destroy")]
    public static partial uint TextureViewDestroy(AlcoGPU.TextureViewHandle view);

    [LibraryImport(LibraryName, EntryPoint = "device_create_sampler")]
    public static partial uint SamplerCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SamplerDesc desc, out AlcoGPU.SamplerHandle sampler);

    [LibraryImport(LibraryName, EntryPoint = "sampler_destroy")]
    public static partial uint SamplerDestroy(AlcoGPU.SamplerHandle sampler);

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_release")]
    public static partial uint TextureRelease(AlcoGPU.TextureHandle texture);

    // ------------------------------------------------------------------
    // Shader modules
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_shader_module")]
    public static partial uint ShaderModuleCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ShaderModuleDesc desc, out AlcoGPU.ShaderModuleHandle module);

    [LibraryImport(LibraryName, EntryPoint = "shader_module_destroy")]
    public static partial uint ShaderModuleDestroy(AlcoGPU.ShaderModuleHandle module);

    // ------------------------------------------------------------------
    // Bind group layouts / bind groups
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group_layout")]
    public static partial uint BindGroupLayoutCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupLayoutDesc desc, out AlcoGPU.BindGroupLayoutHandle layout);

    [LibraryImport(LibraryName, EntryPoint = "bind_group_layout_destroy")]
    public static partial uint BindGroupLayoutDestroy(AlcoGPU.BindGroupLayoutHandle layout);

    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group")]
    public static partial uint BindGroupCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupDesc desc, out AlcoGPU.BindGroupHandle group);

    [LibraryImport(LibraryName, EntryPoint = "bind_group_destroy")]
    public static partial uint BindGroupDestroy(AlcoGPU.BindGroupHandle group);

    // ------------------------------------------------------------------
    // Pipelines
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_graphics_pipeline")]
    public static partial uint GraphicsPipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.GraphicsPipelineDesc desc, out AlcoGPU.GraphicsPipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "device_create_compute_pipeline")]
    public static partial uint ComputePipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ComputePipelineDesc desc, out AlcoGPU.ComputePipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "graphics_pipeline_destroy")]
    public static partial uint GraphicsPipelineDestroy(AlcoGPU.GraphicsPipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "compute_pipeline_destroy")]
    public static partial uint ComputePipelineDestroy(AlcoGPU.ComputePipelineHandle pipeline);

    // ------------------------------------------------------------------
    // Command encoding
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_encoder")]
    public static partial uint EncoderCreate(AlcoGPU.DeviceHandle device, byte* name, out AlcoGPU.EncoderHandle encoder);

    [LibraryImport(LibraryName, EntryPoint = "encoder_finish")]
    public static partial uint EncoderFinish(AlcoGPU.EncoderHandle encoder, out AlcoGPU.CommandBufferHandle commandBuffer);

    [LibraryImport(LibraryName, EntryPoint = "encoder_destroy")]
    public static partial uint EncoderDestroy(AlcoGPU.EncoderHandle encoder);

    [LibraryImport(LibraryName, EntryPoint = "command_buffer_destroy")]
    public static partial uint CommandBufferDestroy(AlcoGPU.CommandBufferHandle commandBuffer);

    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_render_pass")]
    public static partial uint RenderPassBegin(AlcoGPU.EncoderHandle encoder, in AlcoGPU.RenderPassDesc desc, out AlcoGPU.RenderPassHandle renderPass);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_end")]
    public static partial uint RenderPassEnd(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_release")]
    public static partial uint RenderPassRelease(AlcoGPU.RenderPassHandle renderPass);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_pipeline")]
    public static partial uint RenderPassSetPipeline(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.GraphicsPipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_bind_group")]
    public static partial uint RenderPassSetBindGroup(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_vertex_buffer")]
    public static partial uint RenderPassSetVertexBuffer(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_index_buffer")]
    public static partial uint RenderPassSetIndexBuffer(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_scissor_rect")]
    public static partial uint RenderPassSetScissorRect(AlcoGPU.RenderPassHandle renderPass, uint x, uint y, uint width, uint height);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_viewport")]
    public static partial uint RenderPassSetViewport(AlcoGPU.RenderPassHandle renderPass, float x, float y, float width, float height, float depthMin, float depthMax);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_blend_constant")]
    public static partial uint RenderPassSetBlendConstant(AlcoGPU.RenderPassHandle renderPass, float r, float g, float b, float a);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_stencil_reference")]
    public static partial uint RenderPassSetStencilReference(AlcoGPU.RenderPassHandle renderPass, uint reference);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_immediates")]
    public static partial uint RenderPassSetImmediates(AlcoGPU.RenderPassHandle renderPass, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw")]
    public static partial uint RenderPassDraw(AlcoGPU.RenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed")]
    public static partial uint RenderPassDrawIndexed(AlcoGPU.RenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indirect")]
    public static partial uint RenderPassDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed_indirect")]
    public static partial uint RenderPassDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect")]
    public static partial uint RenderPassMultiDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect")]
    public static partial uint RenderPassMultiDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect_count")]
    public static partial uint RenderPassMultiDrawIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect_count")]
    public static partial uint RenderPassMultiDrawIndexedIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_insert_debug_marker")]
    public static partial uint RenderPassInsertDebugMarker(AlcoGPU.RenderPassHandle renderPass, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_push_debug_group")]
    public static partial uint RenderPassPushDebugGroup(AlcoGPU.RenderPassHandle renderPass, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_pop_debug_group")]
    public static partial uint RenderPassPopDebugGroup(AlcoGPU.RenderPassHandle renderPass);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_write_timestamp")]
    public static partial uint RenderPassWriteTimestamp(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    [LibraryImport(LibraryName, EntryPoint = "render_pass_execute_bundles")]
    public static partial uint RenderPassExecuteBundles(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.RenderBundleHandle* bundles, uint count);

    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_compute_pass")]
    public static partial uint ComputePassBegin(AlcoGPU.EncoderHandle encoder, AlcoGPU.TimestampWrites* timestampWrites, out AlcoGPU.ComputePassHandle computePass);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_end")]
    public static partial uint ComputePassEnd(AlcoGPU.ComputePassHandle computePass);

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_release")]
    public static partial uint ComputePassRelease(AlcoGPU.ComputePassHandle computePass);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_pipeline")]
    public static partial uint ComputePassSetPipeline(AlcoGPU.ComputePassHandle computePass, AlcoGPU.ComputePipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_bind_group")]
    public static partial uint ComputePassSetBindGroup(AlcoGPU.ComputePassHandle computePass, uint slot, AlcoGPU.BindGroupHandle group);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_immediates")]
    public static partial uint ComputePassSetImmediates(AlcoGPU.ComputePassHandle computePass, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups")]
    public static partial uint ComputePassDispatchWorkgroups(AlcoGPU.ComputePassHandle computePass, uint x, uint y, uint z);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups_indirect")]
    public static partial uint ComputePassDispatchWorkgroupsIndirect(AlcoGPU.ComputePassHandle computePass, AlcoGPU.BufferHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_write_timestamp")]
    public static partial uint ComputePassWriteTimestamp(AlcoGPU.ComputePassHandle computePass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_insert_debug_marker")]
    public static partial uint ComputePassInsertDebugMarker(AlcoGPU.ComputePassHandle computePass, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_push_debug_group")]
    public static partial uint ComputePassPushDebugGroup(AlcoGPU.ComputePassHandle computePass, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "compute_pass_pop_debug_group")]
    public static partial uint ComputePassPopDebugGroup(AlcoGPU.ComputePassHandle computePass);

    // ------------------------------------------------------------------
    // Copies / queries
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_buffer")]
    public static partial uint CopyBufferToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, ulong sourceOffset, AlcoGPU.BufferHandle destination, ulong destinationOffset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_texture")]
    public static partial uint CopyBufferToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, in AlcoGPU.CopyLayout sourceLayout, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoGPU.Extent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_buffer")]
    public static partial uint CopyTextureToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoGPU.BufferHandle destination, in AlcoGPU.CopyLayout destinationLayout, AlcoGPU.Extent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_texture")]
    public static partial uint CopyTextureToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint aspect, AlcoGPU.Extent3D copySize);

    [LibraryImport(LibraryName, EntryPoint = "encoder_resolve_query_set")]
    public static partial uint ResolveQuerySet(AlcoGPU.EncoderHandle encoder, AlcoGPU.QuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoGPU.BufferHandle destination, ulong destinationOffset);

    /// <summary>Clears a buffer range to zero; a size of zero clears to the end of the buffer.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_buffer")]
    public static partial uint EncoderClearBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_texture")]
    public static partial uint EncoderClearTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle texture, AlcoGPU.SubresourceRange range);

    [LibraryImport(LibraryName, EntryPoint = "encoder_insert_debug_marker")]
    public static partial uint EncoderInsertDebugMarker(AlcoGPU.EncoderHandle encoder, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "encoder_push_debug_group")]
    public static partial uint EncoderPushDebugGroup(AlcoGPU.EncoderHandle encoder, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "encoder_pop_debug_group")]
    public static partial uint EncoderPopDebugGroup(AlcoGPU.EncoderHandle encoder);

    [LibraryImport(LibraryName, EntryPoint = "device_create_query_set")]
    public static partial uint QuerySetCreate(AlcoGPU.DeviceHandle device, uint count, byte* name, out AlcoGPU.QuerySetHandle querySet);

    [LibraryImport(LibraryName, EntryPoint = "query_set_destroy")]
    public static partial uint QuerySetDestroy(AlcoGPU.QuerySetHandle querySet);

    // ------------------------------------------------------------------
    // Queue
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "queue_write_buffer")]
    public static partial uint QueueWriteBuffer(AlcoGPU.DeviceHandle device, AlcoGPU.BufferHandle buffer, ulong offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "queue_write_texture")]
    public static partial uint QueueWriteTexture(AlcoGPU.DeviceHandle device, AlcoGPU.TextureHandle texture, uint mipLevel, AlcoGPU.Origin3D origin, uint aspect, byte* data, uint dataSize, in AlcoGPU.CopyLayout layout, AlcoGPU.Extent3D size);

    /// <summary>Submits a command buffer; the native side consumes the handle.</summary>
    [LibraryImport(LibraryName, EntryPoint = "queue_submit")]
    public static partial uint QueueSubmit(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle commandBuffer, ulong* outIndex);

    /// <summary>Submits an array of command buffers as one submission; the native side consumes every handle.</summary>
    [LibraryImport(LibraryName, EntryPoint = "queue_submit_batch")]
    public static partial uint QueueSubmitBatch(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle* commandBuffers, uint count, ulong* outIndex);

    // ------------------------------------------------------------------
    // Render bundles
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_bundle_encoder")]
    public static partial uint BundleEncoderCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BundleEncoderDesc desc, out AlcoGPU.BundleEncoderHandle bundleEncoder);

    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_finish")]
    public static partial uint BundleEncoderFinish(AlcoGPU.BundleEncoderHandle bundleEncoder, out AlcoGPU.RenderBundleHandle bundle);

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_destroy")]
    public static partial uint BundleEncoderDestroy(AlcoGPU.BundleEncoderHandle bundleEncoder);

    [LibraryImport(LibraryName, EntryPoint = "render_bundle_destroy")]
    public static partial uint RenderBundleDestroy(AlcoGPU.RenderBundleHandle bundle);

    [LibraryImport(LibraryName, EntryPoint = "bundle_set_pipeline")]
    public static partial uint BundleSetPipeline(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.GraphicsPipelineHandle pipeline);

    [LibraryImport(LibraryName, EntryPoint = "bundle_set_bind_group")]
    public static partial uint BundleSetBindGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_vertex_buffer")]
    public static partial uint BundleSetVertexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_index_buffer")]
    public static partial uint BundleSetIndexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "bundle_set_immediates")]
    public static partial uint BundleSetImmediates(AlcoGPU.BundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size);

    [LibraryImport(LibraryName, EntryPoint = "bundle_draw")]
    public static partial uint BundleDraw(AlcoGPU.BundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed")]
    public static partial uint BundleDrawIndexed(AlcoGPU.BundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indirect")]
    public static partial uint BundleDrawIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed_indirect")]
    public static partial uint BundleDrawIndexedIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    [LibraryImport(LibraryName, EntryPoint = "bundle_insert_debug_marker")]
    public static partial uint BundleInsertDebugMarker(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "bundle_push_debug_group")]
    public static partial uint BundlePushDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    [LibraryImport(LibraryName, EntryPoint = "bundle_pop_debug_group")]
    public static partial uint BundlePopDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder);

    // ------------------------------------------------------------------
    // Surfaces
    // ------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "device_create_surface")]
    public static partial uint SurfaceCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SurfaceDesc desc, out AlcoGPU.SurfaceHandle surface);

    [LibraryImport(LibraryName, EntryPoint = "surface_get_capabilities")]
    public static partial uint SurfaceGetCapabilities(AlcoGPU.SurfaceHandle surface, ref AlcoGPU.SurfaceCapabilities caps);

    [LibraryImport(LibraryName, EntryPoint = "surface_configure")]
    public static partial uint SurfaceConfigure(AlcoGPU.SurfaceHandle surface, in AlcoGPU.SurfaceConfig config);

    /// <summary>
    /// Acquires the current frame's texture; <c>status</c> receives an
    /// <see cref="AlcoGPU.AcquireStatus"/> value and a non-success acquire
    /// returns a null texture without raising an ABI error.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_get_current_texture")]
    public static partial uint SurfaceGetCurrentTexture(AlcoGPU.SurfaceHandle surface, out AlcoGPU.TextureHandle texture, uint* status);

    /// <summary>Presents the current surface texture; the optional status out receives an <see cref="AlcoGPU.AcquireStatus"/> value.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_present")]
    public static partial uint SurfacePresent(AlcoGPU.SurfaceHandle surface, uint* status);

    [LibraryImport(LibraryName, EntryPoint = "surface_destroy")]
    public static partial uint SurfaceDestroy(AlcoGPU.SurfaceHandle surface);
}
