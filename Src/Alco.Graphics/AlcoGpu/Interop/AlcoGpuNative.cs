namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Managed facade over the raw alco-gpu P/Invoke surface. Fallible entries return a
/// <see cref="AlcoGPU.Status"/> code, which <see cref="AlcoGpuMarshal.ThrowIfFailure"/>
/// converts to a <see cref="GraphicsException"/> after the native call returns.
/// </summary>
internal static unsafe partial class AlcoGpuNative
{
    /// <summary>Returns the packed native library ABI version.</summary>
    /// <returns>The packed ABI version.</returns>
    public static uint AbiVersion() => AlcoGpuRaw.AbiVersion();

    /// <summary>Fills the supplied structure with native build information.</summary>
    /// <param name="info">Structure that receives the information.</param>
    public static void BuildInfo(ref AlcoGPU.BuildInfo info) => AlcoGpuRaw.BuildInfo(ref info);

    /// <summary>Fills the supplied structure with the calling thread's last native error.</summary>
    /// <param name="info">Structure that receives the information.</param>
    public static void GetLastError(ref AlcoGPU.ErrorInfo info) => AlcoGpuRaw.GetLastError(ref info);

    /// <summary>
    /// Registers the process-wide native log callback (null unregisters); records fire synchronously
    /// from inside wgpu-core, so the callback must never throw and the message is borrowed for the call
    /// only. Fails with <see cref="AlcoGPU.Status.Unsupported"/> when another library already owns the
    /// process-wide logger.
    /// </summary>
    public static uint SetLogCallback(delegate* unmanaged[Cdecl]<uint, byte*, void*, void> callback, void* userdata) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SetLogCallback(callback, userdata));

    /// <summary>
    /// Sets the maximum level forwarded to the native log callback; invalid
    /// values throw <see cref="GraphicsException"/>.
    /// </summary>
    public static uint SetLogLevel(uint level) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SetLogLevel(level));

    /// <summary>Creates a device from the supplied descriptor.</summary>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="device">Receives the device handle.</param>
    public static uint DeviceCreate(in AlcoGPU.DeviceDesc desc, out AlcoGPU.DeviceHandle device) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DeviceCreate(in desc, out device));

    /// <summary>Destroys a device and releases its resources.</summary>
    /// <param name="device">Device handle.</param>
    public static uint DeviceDestroy(AlcoGPU.DeviceHandle device) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DeviceDestroy(device));

    /// <summary>Retrieves information about a device.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="info">Structure that receives the information.</param>
    public static uint DeviceGetInfo(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceInfo info) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DeviceGetInfo(device, ref info));

    /// <summary>
    /// Polls the device. With <c>wait</c> set, blocks until the given submit
    /// index (<see cref="ulong.MaxValue"/> = latest submission) completes;
    /// otherwise performs a single non-blocking pump. A non-null
    /// <c>queueEmpty</c> receives whether the queue drained.
    /// </summary>
    public static uint DevicePoll(AlcoGPU.DeviceHandle device, uint wait, ulong submitIndex, uint* queueEmpty) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DevicePoll(device, wait, submitIndex, queueEmpty));

    /// <summary>Pops the next pending device message.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="message">Structure that receives the message.</param>
    public static uint DevicePopMessage(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceMessage message) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DevicePopMessage(device, ref message));

    /// <summary>Creates a buffer from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="buffer">Receives the buffer handle.</param>
    public static uint BufferCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BufferDesc desc, out AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferCreate(device, in desc, out buffer));

    /// <summary>Destroys a buffer and releases its memory.</summary>
    /// <param name="buffer">Buffer handle.</param>
    public static uint BufferDestroy(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferDestroy(buffer));

    /// <summary>Starts an asynchronous read mapping of a buffer range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset into the buffer.</param>
    /// <param name="size">Number of bytes to map.</param>
    public static uint BufferMapRead(AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapRead(buffer, offset, size));

    /// <summary>Initiates a write map; the mapped range is writable until unmap.</summary>
    public static uint BufferMapWrite(AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapWrite(buffer, offset, size));

    /// <summary>Polls a pending buffer map, returning <see cref="AlcoGPU.Status.NotReady"/> until it completes.</summary>
    /// <param name="buffer">Buffer handle.</param>
    public static uint BufferMapPoll(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapPoll(buffer));

    /// <summary>Retrieves the pointer for a mapped buffer range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset into the mapped range.</param>
    /// <param name="size">Size of the mapped range in bytes.</param>
    /// <param name="mapped">Receives the mapped memory pointer.</param>
    public static uint BufferGetMappedRange(AlcoGPU.BufferHandle buffer, ulong offset, ulong size, void** mapped) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferGetMappedRange(buffer, offset, size, mapped));

    /// <summary>Unmaps a buffer and releases its mapped range.</summary>
    /// <param name="buffer">Buffer handle.</param>
    public static uint BufferUnmap(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferUnmap(buffer));

    /// <summary>Creates a texture from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="texture">Receives the texture handle.</param>
    public static uint TextureCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.TextureDesc desc, out AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureCreate(device, in desc, out texture));

    /// <summary>Destroys a texture and releases its memory.</summary>
    /// <param name="texture">Texture handle.</param>
    public static uint TextureDestroy(AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureDestroy(texture));

    /// <summary>Retrieves information about a texture.</summary>
    /// <param name="texture">Texture handle.</param>
    /// <param name="info">Structure that receives the information.</param>
    public static uint TextureGetInfo(AlcoGPU.TextureHandle texture, ref AlcoGPU.TextureInfo info) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureGetInfo(texture, ref info));

    /// <summary>Creates a texture view; a null <c>desc</c> creates the default view of the whole texture.</summary>
    public static uint TextureCreateView(AlcoGPU.TextureHandle texture, AlcoGPU.TextureViewDesc* desc, out AlcoGPU.TextureViewHandle view) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureCreateView(texture, desc, out view));

    /// <summary>Destroys a texture view.</summary>
    /// <param name="view">Texture view handle.</param>
    public static uint TextureViewDestroy(AlcoGPU.TextureViewHandle view) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureViewDestroy(view));

    /// <summary>Creates a sampler from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="sampler">Receives the sampler handle.</param>
    public static uint SamplerCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SamplerDesc desc, out AlcoGPU.SamplerHandle sampler) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SamplerCreate(device, in desc, out sampler));

    /// <summary>Destroys a sampler.</summary>
    /// <param name="sampler">Sampler handle.</param>
    public static uint SamplerDestroy(AlcoGPU.SamplerHandle sampler) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SamplerDestroy(sampler));

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    public static uint TextureRelease(AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureRelease(texture));

    /// <summary>Creates a shader module from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="module">Receives the shader module handle.</param>
    public static uint ShaderModuleCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ShaderModuleDesc desc, out AlcoGPU.ShaderModuleHandle module) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ShaderModuleCreate(device, in desc, out module));

    /// <summary>Destroys a shader module.</summary>
    /// <param name="module">Shader module handle.</param>
    public static uint ShaderModuleDestroy(AlcoGPU.ShaderModuleHandle module) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ShaderModuleDestroy(module));

    /// <summary>Creates a bind group layout from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="layout">Receives the bind group layout handle.</param>
    public static uint BindGroupLayoutCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupLayoutDesc desc, out AlcoGPU.BindGroupLayoutHandle layout) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupLayoutCreate(device, in desc, out layout));

    /// <summary>Destroys a bind group layout.</summary>
    /// <param name="layout">Bind group layout handle.</param>
    public static uint BindGroupLayoutDestroy(AlcoGPU.BindGroupLayoutHandle layout) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupLayoutDestroy(layout));

    /// <summary>Creates a bind group from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="group">Receives the bind group handle.</param>
    public static uint BindGroupCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupDesc desc, out AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupCreate(device, in desc, out group));

    /// <summary>Destroys a bind group.</summary>
    /// <param name="group">Bind group handle.</param>
    public static uint BindGroupDestroy(AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupDestroy(group));

    /// <summary>Creates a graphics pipeline from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="pipeline">Receives the pipeline handle.</param>
    public static uint GraphicsPipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.GraphicsPipelineDesc desc, out AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.GraphicsPipelineCreate(device, in desc, out pipeline));

    /// <summary>Creates a compute pipeline from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="pipeline">Receives the pipeline handle.</param>
    public static uint ComputePipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ComputePipelineDesc desc, out AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePipelineCreate(device, in desc, out pipeline));

    /// <summary>Destroys a graphics pipeline.</summary>
    /// <param name="pipeline">Pipeline handle.</param>
    public static uint GraphicsPipelineDestroy(AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.GraphicsPipelineDestroy(pipeline));

    /// <summary>Destroys a compute pipeline.</summary>
    /// <param name="pipeline">Pipeline handle.</param>
    public static uint ComputePipelineDestroy(AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePipelineDestroy(pipeline));

    /// <summary>Creates a command encoder for recording GPU commands.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="name">Optional NUL-terminated UTF-8 debug name.</param>
    /// <param name="encoder">Receives the encoder handle.</param>
    public static uint EncoderCreate(AlcoGPU.DeviceHandle device, byte* name, out AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderCreate(device, name, out encoder));

    /// <summary>Finishes encoding and returns the recorded command buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="commandBuffer">Receives the command buffer handle.</param>
    public static uint EncoderFinish(AlcoGPU.EncoderHandle encoder, out AlcoGPU.CommandBufferHandle commandBuffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderFinish(encoder, out commandBuffer));

    /// <summary>Destroys an encoder that was not finished.</summary>
    /// <param name="encoder">Encoder handle.</param>
    public static uint EncoderDestroy(AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderDestroy(encoder));

    /// <summary>Destroys a command buffer without submitting it.</summary>
    /// <param name="commandBuffer">Command buffer handle.</param>
    public static uint CommandBufferDestroy(AlcoGPU.CommandBufferHandle commandBuffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CommandBufferDestroy(commandBuffer));

    /// <summary>Begins a render pass on the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="renderPass">Receives the render pass handle.</param>
    public static uint RenderPassBegin(AlcoGPU.EncoderHandle encoder, in AlcoGPU.RenderPassDesc desc, out AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassBegin(encoder, in desc, out renderPass));

    /// <summary>Ends the open render pass and validates its recorded commands.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    public static uint RenderPassEnd(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassEnd(renderPass));

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    public static uint RenderPassRelease(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassRelease(renderPass));

    /// <summary>Sets the graphics pipeline for subsequent draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    public static uint RenderPassSetPipeline(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetPipeline(renderPass, pipeline));

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    public static uint RenderPassSetBindGroup(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetBindGroup(renderPass, slot, group));

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint RenderPassSetVertexBuffer(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetVertexBuffer(renderPass, slot, buffer, offset, size));

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint RenderPassSetIndexBuffer(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetIndexBuffer(renderPass, buffer, format, offset, size));

    /// <summary>Sets the scissor rectangle in framebuffer coordinates.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="x">X coordinate of the scissor rectangle.</param>
    /// <param name="y">Y coordinate of the scissor rectangle.</param>
    /// <param name="width">Width of the scissor rectangle.</param>
    /// <param name="height">Height of the scissor rectangle.</param>
    public static uint RenderPassSetScissorRect(AlcoGPU.RenderPassHandle renderPass, uint x, uint y, uint width, uint height) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetScissorRect(renderPass, x, y, width, height));

    /// <summary>Sets the viewport in framebuffer coordinates.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="x">X coordinate of the viewport.</param>
    /// <param name="y">Y coordinate of the viewport.</param>
    /// <param name="width">Width of the viewport.</param>
    /// <param name="height">Height of the viewport.</param>
    /// <param name="depthMin">Minimum depth value.</param>
    /// <param name="depthMax">Maximum depth value.</param>
    public static uint RenderPassSetViewport(AlcoGPU.RenderPassHandle renderPass, float x, float y, float width, float height, float depthMin, float depthMax) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetViewport(renderPass, x, y, width, height, depthMin, depthMax));

    /// <summary>Sets the blend constant color.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="r">Red component of the blend constant.</param>
    /// <param name="g">Green component of the blend constant.</param>
    /// <param name="b">Blue component of the blend constant.</param>
    /// <param name="a">Alpha component of the blend constant.</param>
    public static uint RenderPassSetBlendConstant(AlcoGPU.RenderPassHandle renderPass, float r, float g, float b, float a) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetBlendConstant(renderPass, r, g, b, a));

    /// <summary>Sets the stencil reference value.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="reference">Stencil reference value.</param>
    public static uint RenderPassSetStencilReference(AlcoGPU.RenderPassHandle renderPass, uint reference) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetStencilReference(renderPass, reference));

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    public static uint RenderPassSetImmediates(AlcoGPU.RenderPassHandle renderPass, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetImmediates(renderPass, offset, data, size));

    /// <summary>Draws non-indexed primitives.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="vertexCount">Number of vertices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public static uint RenderPassDraw(AlcoGPU.RenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDraw(renderPass, vertexCount, instanceCount, firstVertex, firstInstance));

    /// <summary>Draws indexed primitives.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="indexCount">Number of indices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstIndex">Index of the first index.</param>
    /// <param name="vertexOffset">Signed offset added to each index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public static uint RenderPassDrawIndexed(AlcoGPU.RenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndexed(renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance));

    /// <summary>Draws non-indexed primitives with parameters read from a buffer.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    public static uint RenderPassDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndirect(renderPass, buffer, offset));

    /// <summary>Draws indexed primitives with parameters read from a buffer.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    public static uint RenderPassDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndexedIndirect(renderPass, buffer, offset));

    /// <summary>Issues multiple indexed indirect draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="count">Number of draws.</param>
    public static uint RenderPassMultiDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndexedIndirect(renderPass, buffer, offset, count));

    /// <summary>Issues multiple non-indexed indirect draws.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="count">Number of draws.</param>
    public static uint RenderPassMultiDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndirect(renderPass, buffer, offset, count));

    /// <summary>Issues non-indexed indirect draws using a GPU-side count, capped by maxCount.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="countBuffer">Buffer containing the draw count.</param>
    /// <param name="countBufferOffset">Byte offset of the draw count in the count buffer.</param>
    /// <param name="maxCount">Maximum number of draws.</param>
    public static uint RenderPassMultiDrawIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndirectCount(renderPass, buffer, offset, countBuffer, countBufferOffset, maxCount));

    /// <summary>Issues indexed indirect draws using a GPU-side count, capped by maxCount.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    /// <param name="countBuffer">Buffer containing the draw count.</param>
    /// <param name="countBufferOffset">Byte offset of the draw count in the count buffer.</param>
    /// <param name="maxCount">Maximum number of draws.</param>
    public static uint RenderPassMultiDrawIndexedIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndexedIndirectCount(renderPass, buffer, offset, countBuffer, countBufferOffset, maxCount));

    /// <summary>Inserts a debug marker into the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint RenderPassInsertDebugMarker(AlcoGPU.RenderPassHandle renderPass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassInsertDebugMarker(renderPass, label));

    /// <summary>Opens a named debug group in the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint RenderPassPushDebugGroup(AlcoGPU.RenderPassHandle renderPass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassPushDebugGroup(renderPass, label));

    /// <summary>Closes the innermost debug group in the render pass.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    public static uint RenderPassPopDebugGroup(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassPopDebugGroup(renderPass));

    /// <summary>Writes a timestamp into a query set.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="queryIndex">Index of the query within the set.</param>
    public static uint RenderPassWriteTimestamp(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.QuerySetHandle querySet, uint queryIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassWriteTimestamp(renderPass, querySet, queryIndex));

    /// <summary>Executes pre-recorded render bundles.</summary>
    /// <param name="renderPass">Render pass handle.</param>
    /// <param name="bundles">Render bundles to execute.</param>
    /// <param name="count">Number of render bundles.</param>
    public static uint RenderPassExecuteBundles(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.RenderBundleHandle* bundles, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassExecuteBundles(renderPass, bundles, count));

    /// <summary>Begins a compute pass on the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="timestampWrites">Optional timestamp writes; may be null.</param>
    /// <param name="computePass">Receives the compute pass handle.</param>
    public static uint ComputePassBegin(AlcoGPU.EncoderHandle encoder, AlcoGPU.TimestampWrites* timestampWrites, out AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassBegin(encoder, timestampWrites, out computePass));

    /// <summary>Ends the open compute pass and validates its recorded commands.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    public static uint ComputePassEnd(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassEnd(computePass));

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    public static uint ComputePassRelease(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassRelease(computePass));

    /// <summary>Sets the compute pipeline for subsequent dispatches.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    public static uint ComputePassSetPipeline(AlcoGPU.ComputePassHandle computePass, AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetPipeline(computePass, pipeline));

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    public static uint ComputePassSetBindGroup(AlcoGPU.ComputePassHandle computePass, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetBindGroup(computePass, slot, group));

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    public static uint ComputePassSetImmediates(AlcoGPU.ComputePassHandle computePass, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetImmediates(computePass, offset, data, size));

    /// <summary>Dispatches the given number of workgroups.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="x">Number of workgroups on the X axis.</param>
    /// <param name="y">Number of workgroups on the Y axis.</param>
    /// <param name="z">Number of workgroups on the Z axis.</param>
    public static uint ComputePassDispatchWorkgroups(AlcoGPU.ComputePassHandle computePass, uint x, uint y, uint z) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassDispatchWorkgroups(computePass, x, y, z));

    /// <summary>Dispatches workgroups with parameters read from a buffer.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the dispatch parameters in the buffer.</param>
    public static uint ComputePassDispatchWorkgroupsIndirect(AlcoGPU.ComputePassHandle computePass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassDispatchWorkgroupsIndirect(computePass, buffer, offset));

    /// <summary>Writes a timestamp into a query set.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="queryIndex">Index of the query within the set.</param>
    public static uint ComputePassWriteTimestamp(AlcoGPU.ComputePassHandle computePass, AlcoGPU.QuerySetHandle querySet, uint queryIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassWriteTimestamp(computePass, querySet, queryIndex));

    /// <summary>Inserts a debug marker into the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint ComputePassInsertDebugMarker(AlcoGPU.ComputePassHandle computePass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassInsertDebugMarker(computePass, label));

    /// <summary>Opens a named debug group in the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint ComputePassPushDebugGroup(AlcoGPU.ComputePassHandle computePass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassPushDebugGroup(computePass, label));

    /// <summary>Closes the innermost debug group in the compute pass.</summary>
    /// <param name="computePass">Compute pass handle.</param>
    public static uint ComputePassPopDebugGroup(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassPopDebugGroup(computePass));

    /// <summary>Copies bytes between buffers.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceOffset">Byte offset in the source buffer.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationOffset">Byte offset in the destination buffer.</param>
    /// <param name="size">Number of bytes to copy.</param>
    public static uint CopyBufferToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, ulong sourceOffset, AlcoGPU.BufferHandle destination, ulong destinationOffset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyBufferToBuffer(encoder, source, sourceOffset, destination, destinationOffset, size));

    /// <summary>Copies a buffer region into a texture.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceLayout">Texel layout of the source buffer.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationMipLevel">Destination mip level.</param>
    /// <param name="destinationAspect">Aspect mask of the destination texture.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    public static uint CopyBufferToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, in AlcoGPU.CopyLayout sourceLayout, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyBufferToTexture(encoder, source, in sourceLayout, destination, destinationMipLevel, destinationAspect, copySize));

    /// <summary>Copies a texture region into a buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceMipLevel">Source mip level.</param>
    /// <param name="sourceAspect">Aspect mask of the source texture.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationLayout">Texel layout of the destination buffer.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    public static uint CopyTextureToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoGPU.BufferHandle destination, in AlcoGPU.CopyLayout destinationLayout, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyTextureToBuffer(encoder, source, sourceMipLevel, sourceAspect, destination, in destinationLayout, copySize));

    /// <summary>Copies a texture region into another texture.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="source">Source resource handle.</param>
    /// <param name="sourceMipLevel">Source mip level.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationMipLevel">Destination mip level.</param>
    /// <param name="aspect">Texture aspect mask.</param>
    /// <param name="copySize">Extent of the copied region.</param>
    public static uint CopyTextureToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint aspect, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyTextureToTexture(encoder, source, sourceMipLevel, destination, destinationMipLevel, aspect, copySize));

    /// <summary>Resolves query results into a buffer.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="querySet">Query set handle.</param>
    /// <param name="firstQuery">Index of the first query to resolve.</param>
    /// <param name="queryCount">Number of queries to resolve.</param>
    /// <param name="destination">Destination resource handle.</param>
    /// <param name="destinationOffset">Byte offset in the destination buffer.</param>
    public static uint ResolveQuerySet(AlcoGPU.EncoderHandle encoder, AlcoGPU.QuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoGPU.BufferHandle destination, ulong destinationOffset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ResolveQuerySet(encoder, querySet, firstQuery, queryCount, destination, destinationOffset));

    /// <summary>Clears a buffer range to zero; a size of zero clears to the end of the buffer.</summary>
    public static uint EncoderClearBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderClearBuffer(encoder, buffer, offset, size));

    /// <summary>Clears a texture subresource to zero.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="texture">Texture handle.</param>
    /// <param name="range">Subresource range to clear.</param>
    public static uint EncoderClearTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle texture, AlcoGPU.SubresourceRange range) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderClearTexture(encoder, texture, range));

    /// <summary>Inserts a debug marker into the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint EncoderInsertDebugMarker(AlcoGPU.EncoderHandle encoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderInsertDebugMarker(encoder, label));

    /// <summary>Opens a named debug group in the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint EncoderPushDebugGroup(AlcoGPU.EncoderHandle encoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderPushDebugGroup(encoder, label));

    /// <summary>Closes the innermost debug group in the encoder.</summary>
    /// <param name="encoder">Encoder handle.</param>
    public static uint EncoderPopDebugGroup(AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderPopDebugGroup(encoder));

    /// <summary>Creates a query set with the given number of queries.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="count">Number of queries in the set.</param>
    /// <param name="name">Optional NUL-terminated UTF-8 debug name.</param>
    /// <param name="querySet">Receives the query set handle.</param>
    public static uint QuerySetCreate(AlcoGPU.DeviceHandle device, uint count, byte* name, out AlcoGPU.QuerySetHandle querySet) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QuerySetCreate(device, count, name, out querySet));

    /// <summary>Destroys a query set.</summary>
    /// <param name="querySet">Query set handle.</param>
    public static uint QuerySetDestroy(AlcoGPU.QuerySetHandle querySet) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QuerySetDestroy(querySet));

    /// <summary>Writes data to a buffer immediately, bypassing command encoding.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset in the buffer.</param>
    /// <param name="data">Pointer to the source data.</param>
    /// <param name="size">Number of bytes to write.</param>
    public static uint QueueWriteBuffer(AlcoGPU.DeviceHandle device, AlcoGPU.BufferHandle buffer, ulong offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueWriteBuffer(device, buffer, offset, data, size));

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
    public static uint QueueWriteTexture(AlcoGPU.DeviceHandle device, AlcoGPU.TextureHandle texture, uint mipLevel, AlcoGPU.Origin3D origin, uint aspect, byte* data, uint dataSize, in AlcoGPU.CopyLayout layout, AlcoGPU.Extent3D size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueWriteTexture(device, texture, mipLevel, origin, aspect, data, dataSize, in layout, size));

    /// <summary>Submits a command buffer; the native side consumes the handle.</summary>
    public static uint QueueSubmit(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle commandBuffer, ulong* outIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueSubmit(device, commandBuffer, outIndex));

    /// <summary>Submits an array of command buffers as one submission; the native side consumes every handle.</summary>
    public static uint QueueSubmitBatch(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle* commandBuffers, uint count, ulong* outIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueSubmitBatch(device, commandBuffers, count, outIndex));

    /// <summary>Creates a render bundle encoder from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="bundleEncoder">Receives the bundle encoder handle.</param>
    public static uint BundleEncoderCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BundleEncoderDesc desc, out AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderCreate(device, in desc, out bundleEncoder));

    /// <summary>Finishes the bundle encoder and returns the recorded bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="bundle">Receives the render bundle handle.</param>
    public static uint BundleEncoderFinish(AlcoGPU.BundleEncoderHandle bundleEncoder, out AlcoGPU.RenderBundleHandle bundle) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderFinish(bundleEncoder, out bundle));

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    public static uint BundleEncoderDestroy(AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderDestroy(bundleEncoder));

    /// <summary>Destroys a render bundle.</summary>
    /// <param name="bundle">Render bundle handle.</param>
    public static uint RenderBundleDestroy(AlcoGPU.RenderBundleHandle bundle) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderBundleDestroy(bundle));

    /// <summary>Sets the graphics pipeline for subsequent bundle draws.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="pipeline">Pipeline handle.</param>
    public static uint BundleSetPipeline(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetPipeline(bundleEncoder, pipeline));

    /// <summary>Binds a bind group to a slot.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="slot">Bind slot index.</param>
    /// <param name="group">Bind group handle.</param>
    public static uint BundleSetBindGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetBindGroup(bundleEncoder, slot, group));

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint BundleSetVertexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetVertexBuffer(bundleEncoder, slot, buffer, offset, size));

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint BundleSetIndexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetIndexBuffer(bundleEncoder, buffer, format, offset, size));

    /// <summary>Writes immediate data at the given byte offset.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="offset">Byte offset of the immediate data.</param>
    /// <param name="data">Pointer to the immediate data.</param>
    /// <param name="size">Size of the immediate data in bytes.</param>
    public static uint BundleSetImmediates(AlcoGPU.BundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetImmediates(bundleEncoder, offset, data, size));

    /// <summary>Records a non-indexed draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="vertexCount">Number of vertices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public static uint BundleDraw(AlcoGPU.BundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDraw(bundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance));

    /// <summary>Records an indexed draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="indexCount">Number of indices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw.</param>
    /// <param name="firstIndex">Index of the first index.</param>
    /// <param name="vertexOffset">Signed offset added to each index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public static uint BundleDrawIndexed(AlcoGPU.BundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndexed(bundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance));

    /// <summary>Records a non-indexed indirect draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    public static uint BundleDrawIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndirect(bundleEncoder, buffer, offset));

    /// <summary>Records an indexed indirect draw in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="buffer">Buffer handle.</param>
    /// <param name="offset">Byte offset of the draw parameters in the buffer.</param>
    public static uint BundleDrawIndexedIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndexedIndirect(bundleEncoder, buffer, offset));

    /// <summary>Inserts a debug marker into the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint BundleInsertDebugMarker(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleInsertDebugMarker(bundleEncoder, label));

    /// <summary>Opens a named debug group in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    /// <param name="label">NUL-terminated UTF-8 debug label.</param>
    public static uint BundlePushDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundlePushDebugGroup(bundleEncoder, label));

    /// <summary>Closes the innermost debug group in the bundle.</summary>
    /// <param name="bundleEncoder">Render bundle encoder handle.</param>
    public static uint BundlePopDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundlePopDebugGroup(bundleEncoder));

    /// <summary>Creates a surface from the supplied descriptor.</summary>
    /// <param name="device">Device handle.</param>
    /// <param name="desc">Creation descriptor.</param>
    /// <param name="surface">Receives the surface handle.</param>
    public static uint SurfaceCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SurfaceDesc desc, out AlcoGPU.SurfaceHandle surface) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceCreate(device, in desc, out surface));

    /// <summary>Retrieves the surface's capabilities.</summary>
    /// <param name="surface">Surface handle.</param>
    /// <param name="caps">Structure that receives the surface capabilities.</param>
    public static uint SurfaceGetCapabilities(AlcoGPU.SurfaceHandle surface, ref AlcoGPU.SurfaceCapabilities caps) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceGetCapabilities(surface, ref caps));

    /// <summary>Applies a configuration to the surface.</summary>
    /// <param name="surface">Surface handle.</param>
    /// <param name="config">Surface configuration.</param>
    public static uint SurfaceConfigure(AlcoGPU.SurfaceHandle surface, in AlcoGPU.SurfaceConfig config) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceConfigure(surface, in config));

    /// <summary>
    /// Acquires the current frame's texture; <c>status</c> receives an
    /// <see cref="AlcoGPU.AcquireStatus"/> value and a non-success acquire
    /// returns a null texture without raising an ABI error.
    /// </summary>
    public static uint SurfaceGetCurrentTexture(AlcoGPU.SurfaceHandle surface, out AlcoGPU.TextureHandle texture, uint* status) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceGetCurrentTexture(surface, out texture, status));

    /// <summary>Presents the current surface texture; the optional status out receives an <see cref="AlcoGPU.AcquireStatus"/> value.</summary>
    public static uint SurfacePresent(AlcoGPU.SurfaceHandle surface, uint* status) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfacePresent(surface, status));

    /// <summary>Destroys a surface.</summary>
    /// <param name="surface">Surface handle.</param>
    public static uint SurfaceDestroy(AlcoGPU.SurfaceHandle surface) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceDestroy(surface));
}
