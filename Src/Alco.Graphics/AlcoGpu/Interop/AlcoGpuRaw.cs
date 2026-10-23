using System.Runtime.InteropServices;

namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Raw P/Invoke surface of the alco-gpu native library. Fallible entries return
/// a status code from <see cref="AlcoGPU.Status"/>; failures are rethrown as
/// <see cref="GraphicsException"/> by the <see cref="AlcoGpuNative"/> facade,
/// never from inside the native calls.
/// </summary>
internal static unsafe partial class AlcoGpuRaw
{
    private const string LibraryName = "alco_gpu";

    /// <summary>
    /// Ensures the native library is resolvable (probes the app directory
    /// first) and checks the ABI major version before first use.
    /// </summary>
    static AlcoGpuRaw()
    {
        AlcoGpuNativeLibrary.EnsureLoaded();
        uint version = AbiVersion();
        uint major = version >> 16;
        if (major != AlcoGPU.AbiMajor)
        {
            throw new GraphicsException(
                $"alco-gpu ABI major {major} is incompatible with required ABI {AlcoGPU.AbiMajor}.{AlcoGPU.AbiMinor}; rebuild the native library before using this backend.");
        }
    }

    /// <summary>Returns the packed native library ABI version.</summary>
    /// <returns>The packed ABI version.</returns>
    [LibraryImport(LibraryName, EntryPoint = "abi_version")]
    public static partial uint AbiVersion();

    /// <summary>Fills the supplied structure with native build information.</summary>
    /// <param name="info">Structure that receives the information.</param>
    [LibraryImport(LibraryName, EntryPoint = "build_info")]
    public static partial void BuildInfo(ref AlcoGPU.BuildInfo info);

    /// <summary>Fills the supplied structure with the calling thread's last native error.</summary>
    /// <param name="info">Structure that receives the information.</param>
    [LibraryImport(LibraryName, EntryPoint = "get_last_error")]
    public static partial void GetLastError(ref AlcoGPU.ErrorInfo info);

    /// <summary>
    /// Registers the process-wide native log callback (null unregisters). The callback
    /// must never throw, and the message is borrowed for the duration of the call
    /// only. Fails with <see cref="AlcoGPU.Status.Unsupported"/> when another library
    /// already owns the process-wide logger.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "set_log_callback")]
    public static partial uint SetLogCallback(
        delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback,
        void* userdata);

    /// <summary>
    /// Sets the maximum level forwarded to the native log callback; invalid
    /// values throw <see cref="GraphicsException"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "set_log_level")]
    public static partial uint SetLogLevel(uint level);

    /// <summary>Creates a device from the supplied descriptor.</summary>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="device">Receives the device handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create")]
    public static partial uint DeviceCreate(in AlcoGPU.DeviceDesc desc, out AlcoGPU.DeviceHandle device);

    /// <summary>Destroys a device and releases its resources.</summary>
    /// <param name="device">Device handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_destroy")]
    public static partial uint DeviceDestroy(AlcoGPU.DeviceHandle device);

    /// <summary>Retrieves information about a device.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="info">Structure that receives the information.</param>
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

    /// <summary>Pops the next pending device message.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="message">Structure that receives the message.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_pop_message")]
    public static partial uint DevicePopMessage(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceMessage message);

    // ------------------------------------------------------------------
    // Buffers
    // ------------------------------------------------------------------

    /// <summary>Creates a buffer from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="buffer">Receives the buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_buffer")]
    public static partial uint BufferCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BufferDesc desc, out AlcoGPU.BufferHandle buffer);

    /// <summary>Destroys a buffer and releases its memory.</summary>
    /// <param name="buffer">Buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "buffer_destroy")]
    public static partial uint BufferDestroy(AlcoGPU.BufferHandle buffer);

    /// <summary>Starts an asynchronous read mapping of a buffer range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset into the buffer.</param>
    /// <param name="size">Number of bytes to map.</param>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_read")]
    public static partial uint BufferMapRead(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Initiates a write map; the mapped range is writable until unmap.</summary>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_write")]
    public static partial uint BufferMapWrite(AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Polls a pending buffer map, returning <see cref="AlcoGPU.Status.NotReady"/> until it completes.</summary>
    /// <param name="buffer">Buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "buffer_map_poll")]
    public static partial uint BufferMapPoll(AlcoGPU.BufferHandle buffer);

    /// <summary>Retrieves the pointer for a mapped buffer range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset into the mapped range.</param>
    /// <param name="size">Size of the mapped range in bytes.</param>
    /// <param name="mapped">Receives the mapped memory pointer.</param>
    [LibraryImport(LibraryName, EntryPoint = "buffer_get_mapped_range")]
    public static partial uint BufferGetMappedRange(AlcoGPU.BufferHandle buffer, ulong offset, ulong size, void** mapped);

    /// <summary>Unmaps a buffer and releases its mapped range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "buffer_unmap")]
    public static partial uint BufferUnmap(AlcoGPU.BufferHandle buffer);

    // ------------------------------------------------------------------
    // Textures / views / samplers
    // ------------------------------------------------------------------

    /// <summary>Creates a texture from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="texture">Receives the texture handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_texture")]
    public static partial uint TextureCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.TextureDesc desc, out AlcoGPU.TextureHandle texture);

    /// <summary>Destroys a texture and releases its memory.</summary>
    /// <param name="texture">Texture handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "texture_destroy")]
    public static partial uint TextureDestroy(AlcoGPU.TextureHandle texture);

    /// <summary>Retrieves information about a texture.</summary>
    /// <param name="texture">Texture handle.</param>
    /// <param name="info">Structure that receives the information.</param>
    [LibraryImport(LibraryName, EntryPoint = "texture_get_info")]
    public static partial uint TextureGetInfo(AlcoGPU.TextureHandle texture, ref AlcoGPU.TextureInfo info);

    /// <summary>Creates a texture view; a null <c>desc</c> creates the default view of the whole texture.</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_create_view")]
    public static partial uint TextureCreateView(AlcoGPU.TextureHandle texture, AlcoGPU.TextureViewDesc* desc, out AlcoGPU.TextureViewHandle view);

    /// <summary>Destroys a texture view.</summary>
    /// <param name="view">Texture view handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "texture_view_destroy")]
    public static partial uint TextureViewDestroy(AlcoGPU.TextureViewHandle view);

    /// <summary>Creates a sampler from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="sampler">Receives the sampler handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_sampler")]
    public static partial uint SamplerCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SamplerDesc desc, out AlcoGPU.SamplerHandle sampler);

    /// <summary>Destroys a sampler.</summary>
    /// <param name="sampler">Sampler handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "sampler_destroy")]
    public static partial uint SamplerDestroy(AlcoGPU.SamplerHandle sampler);

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    [LibraryImport(LibraryName, EntryPoint = "texture_release")]
    public static partial uint TextureRelease(AlcoGPU.TextureHandle texture);

    // ------------------------------------------------------------------
    // Shader modules
    // ------------------------------------------------------------------

    /// <summary>Creates a shader module from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="module">Receives the shader module handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_shader_module")]
    public static partial uint ShaderModuleCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ShaderModuleDesc desc, out AlcoGPU.ShaderModuleHandle module);

    /// <summary>Destroys a shader module.</summary>
    /// <param name="module">Shader module handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "shader_module_destroy")]
    public static partial uint ShaderModuleDestroy(AlcoGPU.ShaderModuleHandle module);

    // ------------------------------------------------------------------
    // Bind group layouts / bind groups
    // ------------------------------------------------------------------

    /// <summary>Creates a bind group layout from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="layout">Receives the bind group layout handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group_layout")]
    public static partial uint BindGroupLayoutCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupLayoutDesc desc, out AlcoGPU.BindGroupLayoutHandle layout);

    /// <summary>Destroys a bind group layout.</summary>
    /// <param name="layout">Bind group layout handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bind_group_layout_destroy")]
    public static partial uint BindGroupLayoutDestroy(AlcoGPU.BindGroupLayoutHandle layout);

    /// <summary>Creates a bind group from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="group">Receives the bind group handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bind_group")]
    public static partial uint BindGroupCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupDesc desc, out AlcoGPU.BindGroupHandle group);

    /// <summary>Destroys a bind group.</summary>
    /// <param name="group">Bind group handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bind_group_destroy")]
    public static partial uint BindGroupDestroy(AlcoGPU.BindGroupHandle group);

    // ------------------------------------------------------------------
    // Pipelines
    // ------------------------------------------------------------------

    /// <summary>Creates a graphics pipeline from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="pipeline">Receives the pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_graphics_pipeline")]
    public static partial uint GraphicsPipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.GraphicsPipelineDesc desc, out AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Creates a compute pipeline from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="pipeline">Receives the pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_compute_pipeline")]
    public static partial uint ComputePipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ComputePipelineDesc desc, out AlcoGPU.ComputePipelineHandle pipeline);

    /// <summary>Destroys a graphics pipeline.</summary>
    /// <param name="pipeline">Pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "graphics_pipeline_destroy")]
    public static partial uint GraphicsPipelineDestroy(AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Destroys a compute pipeline.</summary>
    /// <param name="pipeline">Pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pipeline_destroy")]
    public static partial uint ComputePipelineDestroy(AlcoGPU.ComputePipelineHandle pipeline);

    // ------------------------------------------------------------------
    // Command encoding
    // ------------------------------------------------------------------

    /// <summary>Creates a command encoder for recording GPU commands.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="name">Optional NUL-terminated UTF-8 debug name.</param>
    /// <param name="encoder">Receives the encoder handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_encoder")]
    public static partial uint EncoderCreate(AlcoGPU.DeviceHandle device, byte* name, out AlcoGPU.EncoderHandle encoder);

    /// <summary>Finishes encoding and returns the recorded command buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="commandBuffer">Receives the command buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_finish")]
    public static partial uint EncoderFinish(AlcoGPU.EncoderHandle encoder, out AlcoGPU.CommandBufferHandle commandBuffer);

    /// <summary>Destroys an encoder that was not finished.</summary>
    /// <param name="encoder">Encoder handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_destroy")]
    public static partial uint EncoderDestroy(AlcoGPU.EncoderHandle encoder);

    /// <summary>Destroys a command buffer without submitting it.</summary>
    /// <param name="commandBuffer">Command buffer handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "command_buffer_destroy")]
    public static partial uint CommandBufferDestroy(AlcoGPU.CommandBufferHandle commandBuffer);

    /// <summary>Begins a render pass on the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="renderPass">Receives the render pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_render_pass")]
    public static partial uint RenderPassBegin(AlcoGPU.EncoderHandle encoder, in AlcoGPU.RenderPassDesc desc, out AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Ends the open render pass and validates its recorded commands.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_end")]
    public static partial uint RenderPassEnd(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_release")]
    public static partial uint RenderPassRelease(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Sets the graphics pipeline for subsequent draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_pipeline")]
    public static partial uint RenderPassSetPipeline(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_bind_group")]
    public static partial uint RenderPassSetBindGroup(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_vertex_buffer")]
    public static partial uint RenderPassSetVertexBuffer(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_index_buffer")]
    public static partial uint RenderPassSetIndexBuffer(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Sets the scissor rectangle in framebuffer coordinates.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="x">X coordinate of the scissor rectangle.</param>
    /// <param name="y">Y coordinate of the scissor rectangle.</param>
    /// <param name="width">Width of the scissor rectangle.</param>
    /// <param name="height">Height of the scissor rectangle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_scissor_rect")]
    public static partial uint RenderPassSetScissorRect(AlcoGPU.RenderPassHandle renderPass, uint x, uint y, uint width, uint height);

    /// <summary>Sets the viewport in framebuffer coordinates.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="x">X coordinate of the viewport.</param>
    /// <param name="y">Y coordinate of the viewport.</param>
    /// <param name="width">Width of the viewport.</param>
    /// <param name="height">Height of the viewport.</param>
    /// <param name="depthMin">Minimum depth value.</param>
    /// <param name="depthMax">Maximum depth value.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_viewport")]
    public static partial uint RenderPassSetViewport(AlcoGPU.RenderPassHandle renderPass, float x, float y, float width, float height, float depthMin, float depthMax);

    /// <summary>Sets the blend constant color.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="r">Red component of the blend constant.</param>
    /// <param name="g">Green component of the blend constant.</param>
    /// <param name="b">Blue component of the blend constant.</param>
    /// <param name="a">Alpha component of the blend constant.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_blend_constant")]
    public static partial uint RenderPassSetBlendConstant(AlcoGPU.RenderPassHandle renderPass, float r, float g, float b, float a);

    /// <summary>Sets the stencil reference value.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="reference">Stencil reference value.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_stencil_reference")]
    public static partial uint RenderPassSetStencilReference(AlcoGPU.RenderPassHandle renderPass, uint reference);

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_set_immediates")]
    public static partial uint RenderPassSetImmediates(AlcoGPU.RenderPassHandle renderPass, uint offset, byte* data, uint size);

    /// <summary>Draws non-indexed primitives.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="vertexCount">Number of vertices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw")]
    public static partial uint RenderPassDraw(AlcoGPU.RenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Draws indexed primitives.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="indexCount">Number of indices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstIndex">Index of the first index.</param>
    /// <param name="vertexOffset">Signed offset added to each index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed")]
    public static partial uint RenderPassDrawIndexed(AlcoGPU.RenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Draws non-indexed primitives with parameters read from a buffer.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indirect")]
    public static partial uint RenderPassDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Draws indexed primitives with parameters read from a buffer.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_draw_indexed_indirect")]
    public static partial uint RenderPassDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Issues multiple indexed indirect draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="count">Number of draws.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect")]
    public static partial uint RenderPassMultiDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    /// <summary>Issues multiple non-indexed indirect draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="count">Number of draws.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect")]
    public static partial uint RenderPassMultiDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count);

    /// <summary>Issues non-indexed indirect draws using a GPU-side count, capped by maxCount.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="countBuffer">Buffer containing the draw count.</param>
    /// <param name="countBufferOffset">Byte offset of the draw count in the count buffer.</param>
    /// <param name="maxCount">Maximum number of draws.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indirect_count")]
    public static partial uint RenderPassMultiDrawIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    /// <summary>Issues indexed indirect draws using a GPU-side count, capped by maxCount.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="countBuffer">Buffer containing the draw count.</param>
    /// <param name="countBufferOffset">Byte offset of the draw count in the count buffer.</param>
    /// <param name="maxCount">Maximum number of draws.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_multi_draw_indexed_indirect_count")]
    public static partial uint RenderPassMultiDrawIndexedIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount);

    /// <summary>Inserts a debug marker into the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_insert_debug_marker")]
    public static partial uint RenderPassInsertDebugMarker(AlcoGPU.RenderPassHandle renderPass, byte* label);

    /// <summary>Opens a named debug group in the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_push_debug_group")]
    public static partial uint RenderPassPushDebugGroup(AlcoGPU.RenderPassHandle renderPass, byte* label);

    /// <summary>Closes the innermost debug group in the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_pop_debug_group")]
    public static partial uint RenderPassPopDebugGroup(AlcoGPU.RenderPassHandle renderPass);

    /// <summary>Writes a timestamp into a query set.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="queryIndex">Index of the query within the set.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_write_timestamp")]
    public static partial uint RenderPassWriteTimestamp(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    /// <summary>Executes pre-recorded render bundles.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="bundles">Render bundles to execute.</param>
    /// <param name="count">Number of render bundles.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_pass_execute_bundles")]
    public static partial uint RenderPassExecuteBundles(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.RenderBundleHandle* bundles, uint count);

    /// <summary>Begins a compute pass on the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="timestampWrites">Optional timestamp writes; may be null.</param>
    /// <param name="computePass">Receives the compute pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_begin_compute_pass")]
    public static partial uint ComputePassBegin(AlcoGPU.EncoderHandle encoder, AlcoGPU.TimestampWrites* timestampWrites, out AlcoGPU.ComputePassHandle computePass);

    /// <summary>Ends the open compute pass and validates its recorded commands.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_end")]
    public static partial uint ComputePassEnd(AlcoGPU.ComputePassHandle computePass);

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_release")]
    public static partial uint ComputePassRelease(AlcoGPU.ComputePassHandle computePass);

    /// <summary>Sets the compute pipeline for subsequent dispatches.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_pipeline")]
    public static partial uint ComputePassSetPipeline(AlcoGPU.ComputePassHandle computePass, AlcoGPU.ComputePipelineHandle pipeline);

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_bind_group")]
    public static partial uint ComputePassSetBindGroup(AlcoGPU.ComputePassHandle computePass, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_set_immediates")]
    public static partial uint ComputePassSetImmediates(AlcoGPU.ComputePassHandle computePass, uint offset, byte* data, uint size);

    /// <summary>Dispatches the given number of workgroups.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="x">Number of workgroups on the X axis.</param>
    /// <param name="y">Number of workgroups on the Y axis.</param>
    /// <param name="z">Number of workgroups on the Z axis.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups")]
    public static partial uint ComputePassDispatchWorkgroups(AlcoGPU.ComputePassHandle computePass, uint x, uint y, uint z);

    /// <summary>Dispatches workgroups with parameters read from a buffer.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the dispatch parameters in the buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_dispatch_workgroups_indirect")]
    public static partial uint ComputePassDispatchWorkgroupsIndirect(AlcoGPU.ComputePassHandle computePass, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Writes a timestamp into a query set.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="queryIndex">Index of the query within the set.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_write_timestamp")]
    public static partial uint ComputePassWriteTimestamp(AlcoGPU.ComputePassHandle computePass, AlcoGPU.QuerySetHandle querySet, uint queryIndex);

    /// <summary>Inserts a debug marker into the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_insert_debug_marker")]
    public static partial uint ComputePassInsertDebugMarker(AlcoGPU.ComputePassHandle computePass, byte* label);

    /// <summary>Opens a named debug group in the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_push_debug_group")]
    public static partial uint ComputePassPushDebugGroup(AlcoGPU.ComputePassHandle computePass, byte* label);

    /// <summary>Closes the innermost debug group in the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "compute_pass_pop_debug_group")]
    public static partial uint ComputePassPopDebugGroup(AlcoGPU.ComputePassHandle computePass);

    // ------------------------------------------------------------------
    // Copies / queries
    // ------------------------------------------------------------------

    /// <summary>Copies bytes between buffers.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceOffset">Byte offset in the source buffer.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationOffset">Byte offset in the destination buffer.</param>
    /// <param name="size">Number of bytes to copy.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_buffer")]
    public static partial uint CopyBufferToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, ulong sourceOffset, AlcoGPU.BufferHandle destination, ulong destinationOffset, ulong size);

    /// <summary>Copies a buffer region into a texture.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceLayout">Texel layout of the source buffer.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationMipLevel">Destination mip level.</param>
    /// <param name="destinationAspect">Aspect mask of the destination texture.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_buffer_to_texture")]
    public static partial uint CopyBufferToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, in AlcoGPU.CopyLayout sourceLayout, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoGPU.Extent3D copySize);

    /// <summary>Copies a texture region into a buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceMipLevel">Source mip level.</param>
    /// <param name="sourceAspect">Aspect mask of the source texture.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationLayout">Texel layout of the destination buffer.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_buffer")]
    public static partial uint CopyTextureToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoGPU.BufferHandle destination, in AlcoGPU.CopyLayout destinationLayout, AlcoGPU.Extent3D copySize);

    /// <summary>Copies a texture region into another texture.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceMipLevel">Source mip level.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationMipLevel">Destination mip level.</param>
    /// <param name="aspect">Texture aspect mask.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_copy_texture_to_texture")]
    public static partial uint CopyTextureToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint aspect, AlcoGPU.Extent3D copySize);

    /// <summary>Resolves query results into a buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="firstQuery">Index of the first query to resolve.</param>
    /// <param name="queryCount">Number of queries to resolve.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationOffset">Byte offset in the destination buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_resolve_query_set")]
    public static partial uint ResolveQuerySet(AlcoGPU.EncoderHandle encoder, AlcoGPU.QuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoGPU.BufferHandle destination, ulong destinationOffset);

    /// <summary>Clears a buffer range to zero; a size of zero clears to the end of the buffer.</summary>
    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_buffer")]
    public static partial uint EncoderClearBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Clears a texture subresource to zero.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="texture">Texture handle.</param>
    /// <param name="range">Subresource range to clear.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_clear_texture")]
    public static partial uint EncoderClearTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle texture, AlcoGPU.SubresourceRange range);

    /// <summary>Inserts a debug marker into the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_insert_debug_marker")]
    public static partial uint EncoderInsertDebugMarker(AlcoGPU.EncoderHandle encoder, byte* label);

    /// <summary>Opens a named debug group in the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_push_debug_group")]
    public static partial uint EncoderPushDebugGroup(AlcoGPU.EncoderHandle encoder, byte* label);

    /// <summary>Closes the innermost debug group in the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "encoder_pop_debug_group")]
    public static partial uint EncoderPopDebugGroup(AlcoGPU.EncoderHandle encoder);

    /// <summary>Creates a query set with the given number of queries.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="count">Number of queries in the set.</param>
    /// <param name="name">Optional NUL-terminated UTF-8 debug name.</param>
    /// <param name="querySet">Receives the query set handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_query_set")]
    public static partial uint QuerySetCreate(AlcoGPU.DeviceHandle device, uint count, byte* name, out AlcoGPU.QuerySetHandle querySet);

    /// <summary>Destroys a query set.</summary>
    /// <param name="querySet">Query set handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "query_set_destroy")]
    public static partial uint QuerySetDestroy(AlcoGPU.QuerySetHandle querySet);

    // ------------------------------------------------------------------
    // Queue
    // ------------------------------------------------------------------

    /// <summary>Writes data to a buffer immediately, bypassing command encoding.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset in the buffer.</param>
    /// <param name="data">Pointer to the source data.</param>
    /// <param name="size">Number of bytes to write.</param>
    [LibraryImport(LibraryName, EntryPoint = "queue_write_buffer")]
    public static partial uint QueueWriteBuffer(AlcoGPU.DeviceHandle device, AlcoGPU.BufferHandle buffer, ulong offset, byte* data, uint size);

    /// <summary>Writes data to a texture immediately, bypassing command encoding.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="texture">Texture handle.</param>
    /// <param name="mipLevel">Destination mip level.</param>
    /// <param name="origin">Texel origin of the write.</param>
    /// <param name="aspect">Texture aspect mask.</param>
    /// <param name="data">Pointer to the source data.</param>
    /// <param name="dataSize">Size of the source data in bytes.</param>
    /// <param name="layout">Texel layout of the source data.</param>
    /// <param name="size">Extent of the written region.</param>
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

    /// <summary>Creates a render bundle encoder from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="bundleEncoder">Receives the bundle encoder handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_bundle_encoder")]
    public static partial uint BundleEncoderCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BundleEncoderDesc desc, out AlcoGPU.BundleEncoderHandle bundleEncoder);

    /// <summary>Finishes the bundle encoder and returns the recorded bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="bundle">Receives the render bundle handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_finish")]
    public static partial uint BundleEncoderFinish(AlcoGPU.BundleEncoderHandle bundleEncoder, out AlcoGPU.RenderBundleHandle bundle);

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_encoder_destroy")]
    public static partial uint BundleEncoderDestroy(AlcoGPU.BundleEncoderHandle bundleEncoder);

    /// <summary>Destroys a render bundle.</summary>
    /// <param name="bundle">Render bundle handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "render_bundle_destroy")]
    public static partial uint RenderBundleDestroy(AlcoGPU.RenderBundleHandle bundle);

    /// <summary>Sets the graphics pipeline for subsequent bundle draws.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_pipeline")]
    public static partial uint BundleSetPipeline(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.GraphicsPipelineHandle pipeline);

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_bind_group")]
    public static partial uint BundleSetBindGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BindGroupHandle group);

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_vertex_buffer")]
    public static partial uint BundleSetVertexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size);

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_index_buffer")]
    public static partial uint BundleSetIndexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size);

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_set_immediates")]
    public static partial uint BundleSetImmediates(AlcoGPU.BundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size);

    /// <summary>Records a non-indexed draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="vertexCount">Number of vertices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw")]
    public static partial uint BundleDraw(AlcoGPU.BundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Records an indexed draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="indexCount">Number of indices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstIndex">Index of the first index.</param>
    /// <param name="vertexOffset">Signed offset added to each index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed")]
    public static partial uint BundleDrawIndexed(AlcoGPU.BundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    /// <summary>Records a non-indexed indirect draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indirect")]
    public static partial uint BundleDrawIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Records an indexed indirect draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_draw_indexed_indirect")]
    public static partial uint BundleDrawIndexedIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset);

    /// <summary>Inserts a debug marker into the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_insert_debug_marker")]
    public static partial uint BundleInsertDebugMarker(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    /// <summary>Opens a named debug group in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_push_debug_group")]
    public static partial uint BundlePushDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label);

    /// <summary>Closes the innermost debug group in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "bundle_pop_debug_group")]
    public static partial uint BundlePopDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder);

    // ------------------------------------------------------------------
    // Surfaces
    // ------------------------------------------------------------------

    /// <summary>Creates a surface from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="surface">Receives the surface handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "device_create_surface")]
    public static partial uint SurfaceCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SurfaceDesc desc, out AlcoGPU.SurfaceHandle surface);

    /// <summary>Retrieves the surface's capabilities.</summary>
    /// <param name="surface">Surface handle.</param>
    /// <param name="caps">Structure that receives the surface capabilities.</param>
    [LibraryImport(LibraryName, EntryPoint = "surface_get_capabilities")]
    public static partial uint SurfaceGetCapabilities(AlcoGPU.SurfaceHandle surface, ref AlcoGPU.SurfaceCapabilities caps);

    /// <summary>Applies a configuration to the surface.</summary>
    /// <param name="surface">Surface handle.</param>
    /// <param name="config">Surface configuration.</param>
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

    /// <summary>Destroys a surface.</summary>
    /// <param name="surface">Surface handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "surface_destroy")]
    public static partial uint SurfaceDestroy(AlcoGPU.SurfaceHandle surface);
}
