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

    /// <summary>Provides the AbiVersion operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "abi_version")]
    public static partial uint AbiVersion();

    /// <summary>Provides the BuildInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "build_info")]
    public static partial void BuildInfo(ref AlcoGPU.BuildInfo info);

    /// <summary>Provides the GetLastError operation.</summary>
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

    /// <summary>Provides the DeviceCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create")]
    public static partial uint DeviceCreate(in AlcoGPU.DeviceDesc desc, out AlcoGPU.DeviceHandle device);

    /// <summary>Provides the DeviceDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_destroy")]
    public static partial uint DeviceDestroy(AlcoGPU.DeviceHandle device);

    /// <summary>Provides the DeviceGetInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_get_info")]
    public static partial uint DeviceGetInfo(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceInfo info);

    /// <summary>Provides the DevicePoll operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_poll")]
    public static partial uint DevicePoll(AlcoGPU.DeviceHandle device, uint wait, ulong submitIndex, uint* queueEmpty);

    /// <summary>Provides the DevicePopMessage operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_pop_message")]
    public static partial uint DevicePopMessage(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceMessage message);

    // ------------------------------------------------------------------
    // Buffers
    // ------------------------------------------------------------------

    /// <summary>Provides the BufferCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_buffer")]
    public static partial uint BufferCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BufferDesc desc, out AlcoGPU.BufferHandle buffer);

    /// <summary>Provides the BufferDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_destroy")]
    public static partial uint BufferDestroy(AlcoGPU.BufferHandle buffer);

    /// <summary>Provides the BufferMapRead operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_read")]
    public static partial uint BufferMapRead(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the BufferMapWrite operation; the mapped range is writable until unmap.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_write")]
    public static partial uint BufferMapWrite(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the BufferMapPoll operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_poll")]
    public static partial uint BufferMapPoll(AlcoGPU.BufferHandle buffer);

    /// <summary>Provides the BufferGetMappedRange operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_get_mapped_range")]
    public static partial uint BufferGetMappedRange(AlcoGPU.BufferHandle buffer, ulong offset, ulong size, void** mapped);

    /// <summary>Provides the BufferUnmap operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_unmap")]
    public static partial uint BufferUnmap(AlcoGPU.BufferHandle buffer);

    // ------------------------------------------------------------------
    // Textures / views / samplers
    // ------------------------------------------------------------------

    /// <summary>Provides the TextureCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_texture")]
    public static partial uint TextureCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.TextureDesc desc, out AlcoGPU.TextureHandle texture);

    /// <summary>Provides the TextureDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_destroy")]
    public static partial uint TextureDestroy(AlcoGPU.TextureHandle texture);

    /// <summary>Provides the TextureGetInfo operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_get_info")]
    public static partial uint TextureGetInfo(AlcoGPU.TextureHandle texture, ref AlcoGPU.TextureInfo info);

    /// <summary>Provides the TextureCreateView operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_create_view")]
    public static partial uint TextureCreateView(AlcoGPU.TextureHandle texture, AlcoGPU.TextureViewDesc* desc, out AlcoGPU.TextureViewHandle view);

    /// <summary>Provides the TextureViewDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_view_destroy")]
    public static partial uint TextureViewDestroy(AlcoGPU.TextureViewHandle view);

    /// <summary>Provides the SamplerCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_sampler")]
    public static partial uint SamplerCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SamplerDesc desc, out AlcoGPU.SamplerHandle sampler);

    /// <summary>Provides the SamplerDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "sampler_destroy")]
    public static partial uint SamplerDestroy(AlcoGPU.SamplerHandle sampler);

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_release")]
    public static partial uint TextureRelease(AlcoGPU.TextureHandle texture);

    // ------------------------------------------------------------------
    // Shader modules
    // ------------------------------------------------------------------

    /// <summary>Provides the ShaderModuleCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_shader_module")]
    public static partial uint ShaderModuleCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ShaderModuleDesc desc, out AlcoGPU.ShaderModuleHandle module);

    /// <summary>Provides the ShaderModuleDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "shader_module_destroy")]
    public static partial uint ShaderModuleDestroy(AlcoGPU.ShaderModuleHandle module);

    // ------------------------------------------------------------------
    // Bind group layouts / bind groups
    // ------------------------------------------------------------------

    /// <summary>Provides the BindGroupLayoutCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group_layout")]
    public static partial uint BindGroupLayoutCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupLayoutDesc desc, out AlcoGPU.BindGroupLayoutHandle layout);

    /// <summary>Provides the BindGroupLayoutDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bind_group_layout_destroy")]
    public static partial uint BindGroupLayoutDestroy(AlcoGPU.BindGroupLayoutHandle layout);

    /// <summary>Provides the BindGroupCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group")]
    public static partial uint BindGroupCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupDesc desc, out AlcoGPU.BindGroupHandle group);

    /// <summary>Provides the BindGroupDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bind_group_destroy")]
    public static partial uint BindGroupDestroy(AlcoGPU.BindGroupHandle group);

    // ------------------------------------------------------------------
    // Pipelines
    // ------------------------------------------------------------------

    /// <summary>Provides the GraphicsPipelineCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_graphics_pipeline")]
    public static partial uint GraphicsPipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.GraphicsPipelineDesc desc, out AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Provides the ComputePipelineCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_compute_pipeline")]
    public static partial uint ComputePipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ComputePipelineDesc desc, out AlcoGPU.ComputePipelineHandle pipeline);

    /// <summary>Provides the GraphicsPipelineDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "graphics_pipeline_destroy")]
    public static partial uint GraphicsPipelineDestroy(AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Provides the ComputePipelineDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pipeline_destroy")]
    public static partial uint ComputePipelineDestroy(AlcoGPU.ComputePipelineHandle pipeline);

    // ------------------------------------------------------------------
    // Command encoding
    // ------------------------------------------------------------------

    /// <summary>Provides the EncoderCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_encoder")]
    public static partial uint EncoderCreate(AlcoGPU.DeviceHandle device, byte* name, out AlcoGPU.EncoderHandle encoder);

    /// <summary>Provides the EncoderFinish operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_finish")]
    public static partial uint EncoderFinish(AlcoGPU.EncoderHandle encoder, out AlcoGPU.CommandBufferHandle commandBuffer);

    /// <summary>Provides the EncoderDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_destroy")]
    public static partial uint EncoderDestroy(AlcoGPU.EncoderHandle encoder);

    /// <summary>Provides the CommandBufferDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "command_buffer_destroy")]
    public static partial uint CommandBufferDestroy(AlcoGPU.CommandBufferHandle commandBuffer);

    /// <summary>Provides the RenderPassBegin operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_render_pass")]
    public static partial uint RenderPassBegin(AlcoGPU.EncoderHandle encoder, in AlcoGPU.RenderPassDesc desc, out AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Provides the RenderPassEnd operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_end")]
    public static partial uint RenderPassEnd(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_release")]
    public static partial uint RenderPassRelease(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Provides the RenderPassSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_pipeline")]
    public static partial uint RenderPassSetPipeline(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Provides the RenderPassSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_bind_group")]
    public static partial uint RenderPassSetBindGroup(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Provides the RenderPassSetVertexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_vertex_buffer")]
    public static partial uint RenderPassSetVertexBuffer(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the RenderPassSetIndexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_index_buffer")]
    public static partial uint RenderPassSetIndexBuffer(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Provides the RenderPassSetScissorRect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_scissor_rect")]
    public static partial uint RenderPassSetScissorRect(AlcoGPU.RenderPassHandle renderPass, uint x, uint y, uint width, uint height);

    /// <summary>Provides the RenderPassSetViewport operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_viewport")]
    public static partial uint RenderPassSetViewport(AlcoGPU.RenderPassHandle renderPass, float x, float y, float width, float height, float depthMin, float depthMax);

    /// <summary>Provides the RenderPassSetBlendConstant operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_blend_constant")]
    public static partial uint RenderPassSetBlendConstant(AlcoGPU.RenderPassHandle renderPass, float r, float g, float b, float a);

    /// <summary>Provides the RenderPassSetStencilReference operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_stencil_reference")]
    public static partial uint RenderPassSetStencilReference(AlcoGPU.RenderPassHandle renderPass, uint reference);

    /// <summary>Provides the RenderPassSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_immediates")]
    public static partial uint RenderPassSetImmediates(AlcoGPU.RenderPassHandle renderPass, uint offset, byte* data, uint size);

    /// <summary>Provides the RenderPassDraw operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw")]
    public static partial uint RenderPassDraw(AlcoGPU.RenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Provides the RenderPassDrawIndexed operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed")]
    public static partial uint RenderPassDrawIndexed(AlcoGPU.RenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Provides the RenderPassDrawIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indirect")]
    public static partial uint RenderPassDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Provides the RenderPassDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed_indirect")]
    public static partial uint RenderPassDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Provides the RenderPassMultiDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect")]
    public static partial uint RenderPassMultiDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    /// <summary>Provides the RenderPassMultiDrawIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect")]
    public static partial uint RenderPassMultiDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    /// <summary>Provides the RenderPassMultiDrawIndirectCount operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect_count")]
    public static partial uint RenderPassMultiDrawIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    /// <summary>Provides the RenderPassMultiDrawIndexedIndirectCount operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect_count")]
    public static partial uint RenderPassMultiDrawIndexedIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    /// <summary>Provides the RenderPassInsertDebugMarker operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_insert_debug_marker")]
    public static partial uint RenderPassInsertDebugMarker(AlcoGPU.RenderPassHandle renderPass, byte* label);

    /// <summary>Provides the RenderPassPushDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_push_debug_group")]
    public static partial uint RenderPassPushDebugGroup(AlcoGPU.RenderPassHandle renderPass, byte* label);

    /// <summary>Provides the RenderPassPopDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_pop_debug_group")]
    public static partial uint RenderPassPopDebugGroup(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Provides the RenderPassWriteTimestamp operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_write_timestamp")]
    public static partial uint RenderPassWriteTimestamp(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    /// <summary>Provides the RenderPassExecuteBundles operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_execute_bundles")]
    public static partial uint RenderPassExecuteBundles(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.RenderBundleHandle* bundles, uint count);

    /// <summary>Provides the ComputePassBegin operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_compute_pass")]
    public static partial uint ComputePassBegin(AlcoGPU.EncoderHandle encoder, AlcoGPU.TimestampWrites* timestampWrites, out AlcoGPU.ComputePassHandle computePass);

    /// <summary>Provides the ComputePassEnd operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_end")]
    public static partial uint ComputePassEnd(AlcoGPU.ComputePassHandle computePass);

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_release")]
    public static partial uint ComputePassRelease(AlcoGPU.ComputePassHandle computePass);

    /// <summary>Provides the ComputePassSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_pipeline")]
    public static partial uint ComputePassSetPipeline(AlcoGPU.ComputePassHandle computePass, AlcoGPU.ComputePipelineHandle pipeline);

    /// <summary>Provides the ComputePassSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_bind_group")]
    public static partial uint ComputePassSetBindGroup(AlcoGPU.ComputePassHandle computePass, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Provides the ComputePassSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_immediates")]
    public static partial uint ComputePassSetImmediates(AlcoGPU.ComputePassHandle computePass, uint offset, byte* data, uint size);

    /// <summary>Provides the ComputePassDispatchWorkgroups operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups")]
    public static partial uint ComputePassDispatchWorkgroups(AlcoGPU.ComputePassHandle computePass, uint x, uint y, uint z);

    /// <summary>Provides the ComputePassDispatchWorkgroupsIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups_indirect")]
    public static partial uint ComputePassDispatchWorkgroupsIndirect(AlcoGPU.ComputePassHandle computePass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Provides the ComputePassWriteTimestamp operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_write_timestamp")]
    public static partial uint ComputePassWriteTimestamp(AlcoGPU.ComputePassHandle computePass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    /// <summary>Provides the ComputePassInsertDebugMarker operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_insert_debug_marker")]
    public static partial uint ComputePassInsertDebugMarker(AlcoGPU.ComputePassHandle computePass, byte* label);

    /// <summary>Provides the ComputePassPushDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_push_debug_group")]
    public static partial uint ComputePassPushDebugGroup(AlcoGPU.ComputePassHandle computePass, byte* label);

    /// <summary>Provides the ComputePassPopDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_pop_debug_group")]
    public static partial uint ComputePassPopDebugGroup(AlcoGPU.ComputePassHandle computePass);

    // ------------------------------------------------------------------
    // Copies / queries
    // ------------------------------------------------------------------

    /// <summary>Provides the CopyBufferToBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_buffer")]
    public static partial uint CopyBufferToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, ulong sourceOffset, AlcoGPU.BufferHandle destination, ulong destinationOffset, ulong size);

    /// <summary>Provides the CopyBufferToTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_texture")]
    public static partial uint CopyBufferToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, in AlcoGPU.CopyLayout sourceLayout, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoGPU.Extent3D copySize);

    /// <summary>Provides the CopyTextureToBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_buffer")]
    public static partial uint CopyTextureToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoGPU.BufferHandle destination, in AlcoGPU.CopyLayout destinationLayout, AlcoGPU.Extent3D copySize);

    /// <summary>Provides the CopyTextureToTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_texture")]
    public static partial uint CopyTextureToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint aspect, AlcoGPU.Extent3D copySize);

    /// <summary>Provides the ResolveQuerySet operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_resolve_query_set")]
    public static partial uint ResolveQuerySet(AlcoGPU.EncoderHandle encoder, AlcoGPU.QuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoGPU.BufferHandle destination, ulong destinationOffset);

    /// <summary>Provides the EncoderClearBuffer operation; a size of zero clears to the end of the buffer.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_buffer")]
    public static partial uint EncoderClearBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the EncoderClearTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_texture")]
    public static partial uint EncoderClearTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle texture, AlcoGPU.SubresourceRange range);

    /// <summary>Provides the EncoderInsertDebugMarker operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_insert_debug_marker")]
    public static partial uint EncoderInsertDebugMarker(AlcoGPU.EncoderHandle encoder, byte* label);

    /// <summary>Provides the EncoderPushDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_push_debug_group")]
    public static partial uint EncoderPushDebugGroup(AlcoGPU.EncoderHandle encoder, byte* label);

    /// <summary>Provides the EncoderPopDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_pop_debug_group")]
    public static partial uint EncoderPopDebugGroup(AlcoGPU.EncoderHandle encoder);

    /// <summary>Provides the QuerySetCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_query_set")]
    public static partial uint QuerySetCreate(AlcoGPU.DeviceHandle device, uint count, byte* name, out AlcoGPU.QuerySetHandle querySet);

    /// <summary>Provides the QuerySetDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "query_set_destroy")]
    public static partial uint QuerySetDestroy(AlcoGPU.QuerySetHandle querySet);

    // ------------------------------------------------------------------
    // Queue
    // ------------------------------------------------------------------

    /// <summary>Provides the QueueWriteBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "queue_write_buffer")]
    public static partial uint QueueWriteBuffer(AlcoGPU.DeviceHandle device, AlcoGPU.BufferHandle buffer, ulong offset, byte* data, uint size);

    /// <summary>Provides the QueueWriteTexture operation.</summary>
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

    /// <summary>Provides the BundleEncoderCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bundle_encoder")]
    public static partial uint BundleEncoderCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BundleEncoderDesc desc, out AlcoGPU.BundleEncoderHandle bundleEncoder);

    /// <summary>Provides the BundleEncoderFinish operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_finish")]
    public static partial uint BundleEncoderFinish(AlcoGPU.BundleEncoderHandle bundleEncoder, out AlcoGPU.RenderBundleHandle bundle);

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_destroy")]
    public static partial uint BundleEncoderDestroy(AlcoGPU.BundleEncoderHandle bundleEncoder);

    /// <summary>Provides the RenderBundleDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_bundle_destroy")]
    public static partial uint RenderBundleDestroy(AlcoGPU.RenderBundleHandle bundle);

    /// <summary>Provides the BundleSetPipeline operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_pipeline")]
    public static partial uint BundleSetPipeline(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Provides the BundleSetBindGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_bind_group")]
    public static partial uint BundleSetBindGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Provides the BundleSetVertexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_vertex_buffer")]
    public static partial uint BundleSetVertexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Provides the BundleSetIndexBuffer operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_index_buffer")]
    public static partial uint BundleSetIndexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Provides the BundleSetImmediates operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_immediates")]
    public static partial uint BundleSetImmediates(AlcoGPU.BundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size);

    /// <summary>Provides the BundleDraw operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw")]
    public static partial uint BundleDraw(AlcoGPU.BundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Provides the BundleDrawIndexed operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed")]
    public static partial uint BundleDrawIndexed(AlcoGPU.BundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Provides the BundleDrawIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indirect")]
    public static partial uint BundleDrawIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Provides the BundleDrawIndexedIndirect operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed_indirect")]
    public static partial uint BundleDrawIndexedIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Provides the BundleInsertDebugMarker operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_insert_debug_marker")]
    public static partial uint BundleInsertDebugMarker(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    /// <summary>Provides the BundlePushDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_push_debug_group")]
    public static partial uint BundlePushDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    /// <summary>Provides the BundlePopDebugGroup operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_pop_debug_group")]
    public static partial uint BundlePopDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder);

    // ------------------------------------------------------------------
    // Surfaces
    // ------------------------------------------------------------------

    /// <summary>Provides the SurfaceCreate operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "device_create_surface")]
    public static partial uint SurfaceCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SurfaceDesc desc, out AlcoGPU.SurfaceHandle surface);

    /// <summary>Provides the SurfaceGetCapabilities operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_get_capabilities")]
    public static partial uint SurfaceGetCapabilities(AlcoGPU.SurfaceHandle surface, ref AlcoGPU.SurfaceCaps caps);

    /// <summary>Provides the SurfaceConfigure operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_configure")]
    public static partial uint SurfaceConfigure(AlcoGPU.SurfaceHandle surface, in AlcoGPU.SurfaceConfig config);

    /// <summary>Provides the SurfaceGetCurrentTexture operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_get_current_texture")]
    public static partial uint SurfaceGetCurrentTexture(AlcoGPU.SurfaceHandle surface, out AlcoGPU.TextureHandle texture, uint* status);

    /// <summary>Provides the SurfacePresent operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_present")]
    public static partial uint SurfacePresent(AlcoGPU.SurfaceHandle surface, uint* status);

    /// <summary>Provides the SurfaceDestroy operation.</summary>
    [LibraryImport(LibraryName, EntryPoint = "surface_destroy")]
    public static partial uint SurfaceDestroy(AlcoGPU.SurfaceHandle surface);
}
