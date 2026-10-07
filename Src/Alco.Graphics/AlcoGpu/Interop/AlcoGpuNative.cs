namespace Alco.Graphics.AlcoGpu.Interop;

/// <summary>
/// Managed facade over the raw alco-gpu P/Invoke surface. Every fallible entry
/// returns a <see cref="AlcoGPU.Status"/> code; failures are converted to
/// <see cref="GraphicsException"/> by <see cref="AlcoGpuMarshal.ThrowIfFailure"/>
/// once control is back in managed code, never from inside the native calls.
/// </summary>
internal static unsafe partial class AlcoGpuNative
{
    public static uint AbiVersion() => AlcoGpuRaw.AbiVersion();

    public static void BuildInfo(ref AlcoGPU.BuildInfo info) => AlcoGpuRaw.BuildInfo(ref info);

    public static void GetLastError(ref AlcoGPU.ErrorInfo info) => AlcoGpuRaw.GetLastError(ref info);

    /// <summary>
    /// Registers the process-wide native log callback (null unregisters) and
    /// installs the native log forwarder on first use. Records fire
    /// synchronously from inside wgpu-core, so the callback must never throw;
    /// the message is borrowed for the duration of the call only. Fails with
    /// <see cref="AlcoGPU.Status.Unsupported"/> (thrown as
    /// <see cref="GraphicsException"/>) when another library already owns the
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

    public static uint DeviceCreate(in AlcoGPU.DeviceDesc desc, out AlcoGPU.DeviceHandle device) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DeviceCreate(in desc, out device));

    public static uint DeviceDestroy(AlcoGPU.DeviceHandle device) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DeviceDestroy(device));

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

    public static uint DevicePopMessage(AlcoGPU.DeviceHandle device, ref AlcoGPU.DeviceMessage message) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.DevicePopMessage(device, ref message));

    public static uint BufferCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BufferDesc desc, out AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferCreate(device, in desc, out buffer));

    public static uint BufferDestroy(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferDestroy(buffer));

    public static uint BufferMapRead(AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapRead(buffer, offset, size));

    /// <summary>Initiates a write map; the mapped range is writable until unmap.</summary>
    public static uint BufferMapWrite(AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapWrite(buffer, offset, size));

    public static uint BufferMapPoll(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferMapPoll(buffer));

    public static uint BufferGetMappedRange(AlcoGPU.BufferHandle buffer, ulong offset, ulong size, void** mapped) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferGetMappedRange(buffer, offset, size, mapped));

    public static uint BufferUnmap(AlcoGPU.BufferHandle buffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BufferUnmap(buffer));

    public static uint TextureCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.TextureDesc desc, out AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureCreate(device, in desc, out texture));

    public static uint TextureDestroy(AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureDestroy(texture));

    public static uint TextureGetInfo(AlcoGPU.TextureHandle texture, ref AlcoGPU.TextureInfo info) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureGetInfo(texture, ref info));

    /// <summary>Creates a texture view; a null <c>desc</c> creates the default view of the whole texture.</summary>
    public static uint TextureCreateView(AlcoGPU.TextureHandle texture, AlcoGPU.TextureViewDesc* desc, out AlcoGPU.TextureViewHandle view) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureCreateView(texture, desc, out view));

    public static uint TextureViewDestroy(AlcoGPU.TextureViewHandle view) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureViewDestroy(view));

    public static uint SamplerCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SamplerDesc desc, out AlcoGPU.SamplerHandle sampler) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SamplerCreate(device, in desc, out sampler));

    public static uint SamplerDestroy(AlcoGPU.SamplerHandle sampler) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SamplerDestroy(sampler));

    /// <summary>Releases an acquired surface texture (destroy is rejected for those).</summary>
    public static uint TextureRelease(AlcoGPU.TextureHandle texture) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.TextureRelease(texture));

    public static uint ShaderModuleCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ShaderModuleDesc desc, out AlcoGPU.ShaderModuleHandle module) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ShaderModuleCreate(device, in desc, out module));

    public static uint ShaderModuleDestroy(AlcoGPU.ShaderModuleHandle module) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ShaderModuleDestroy(module));

    public static uint BindGroupLayoutCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupLayoutDesc desc, out AlcoGPU.BindGroupLayoutHandle layout) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupLayoutCreate(device, in desc, out layout));

    public static uint BindGroupLayoutDestroy(AlcoGPU.BindGroupLayoutHandle layout) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupLayoutDestroy(layout));

    public static uint BindGroupCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BindGroupDesc desc, out AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupCreate(device, in desc, out group));

    public static uint BindGroupDestroy(AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BindGroupDestroy(group));

    public static uint GraphicsPipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.GraphicsPipelineDesc desc, out AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.GraphicsPipelineCreate(device, in desc, out pipeline));

    public static uint ComputePipelineCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.ComputePipelineDesc desc, out AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePipelineCreate(device, in desc, out pipeline));

    public static uint GraphicsPipelineDestroy(AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.GraphicsPipelineDestroy(pipeline));

    public static uint ComputePipelineDestroy(AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePipelineDestroy(pipeline));

    public static uint EncoderCreate(AlcoGPU.DeviceHandle device, byte* name, out AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderCreate(device, name, out encoder));

    public static uint EncoderFinish(AlcoGPU.EncoderHandle encoder, out AlcoGPU.CommandBufferHandle commandBuffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderFinish(encoder, out commandBuffer));

    public static uint EncoderDestroy(AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderDestroy(encoder));

    public static uint CommandBufferDestroy(AlcoGPU.CommandBufferHandle commandBuffer) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CommandBufferDestroy(commandBuffer));

    public static uint RenderPassBegin(AlcoGPU.EncoderHandle encoder, in AlcoGPU.RenderPassDesc desc, out AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassBegin(encoder, in desc, out renderPass));

    public static uint RenderPassEnd(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassEnd(renderPass));

    /// <summary>Consumes an open render pass without validating or ending it.</summary>
    public static uint RenderPassRelease(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassRelease(renderPass));

    public static uint RenderPassSetPipeline(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetPipeline(renderPass, pipeline));

    public static uint RenderPassSetBindGroup(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetBindGroup(renderPass, slot, group));

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint RenderPassSetVertexBuffer(AlcoGPU.RenderPassHandle renderPass, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetVertexBuffer(renderPass, slot, buffer, offset, size));

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint RenderPassSetIndexBuffer(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetIndexBuffer(renderPass, buffer, format, offset, size));

    public static uint RenderPassSetScissorRect(AlcoGPU.RenderPassHandle renderPass, uint x, uint y, uint width, uint height) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetScissorRect(renderPass, x, y, width, height));

    public static uint RenderPassSetViewport(AlcoGPU.RenderPassHandle renderPass, float x, float y, float width, float height, float depthMin, float depthMax) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetViewport(renderPass, x, y, width, height, depthMin, depthMax));

    public static uint RenderPassSetBlendConstant(AlcoGPU.RenderPassHandle renderPass, float r, float g, float b, float a) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetBlendConstant(renderPass, r, g, b, a));

    public static uint RenderPassSetStencilReference(AlcoGPU.RenderPassHandle renderPass, uint reference) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetStencilReference(renderPass, reference));

    public static uint RenderPassSetImmediates(AlcoGPU.RenderPassHandle renderPass, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassSetImmediates(renderPass, offset, data, size));

    public static uint RenderPassDraw(AlcoGPU.RenderPassHandle renderPass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDraw(renderPass, vertexCount, instanceCount, firstVertex, firstInstance));

    public static uint RenderPassDrawIndexed(AlcoGPU.RenderPassHandle renderPass, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndexed(renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance));

    public static uint RenderPassDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndirect(renderPass, buffer, offset));

    public static uint RenderPassDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassDrawIndexedIndirect(renderPass, buffer, offset));

    public static uint RenderPassMultiDrawIndexedIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndexedIndirect(renderPass, buffer, offset, count));

    public static uint RenderPassMultiDrawIndirect(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndirect(renderPass, buffer, offset, count));

    public static uint RenderPassMultiDrawIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndirectCount(renderPass, buffer, offset, countBuffer, countBufferOffset, maxCount));

    public static uint RenderPassMultiDrawIndexedIndirectCount(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.BufferHandle buffer, ulong offset, AlcoGPU.BufferHandle countBuffer, ulong countBufferOffset, uint maxCount) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassMultiDrawIndexedIndirectCount(renderPass, buffer, offset, countBuffer, countBufferOffset, maxCount));

    public static uint RenderPassInsertDebugMarker(AlcoGPU.RenderPassHandle renderPass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassInsertDebugMarker(renderPass, label));

    public static uint RenderPassPushDebugGroup(AlcoGPU.RenderPassHandle renderPass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassPushDebugGroup(renderPass, label));

    public static uint RenderPassPopDebugGroup(AlcoGPU.RenderPassHandle renderPass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassPopDebugGroup(renderPass));

    public static uint RenderPassWriteTimestamp(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.QuerySetHandle querySet, uint queryIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassWriteTimestamp(renderPass, querySet, queryIndex));

    public static uint RenderPassExecuteBundles(AlcoGPU.RenderPassHandle renderPass, AlcoGPU.RenderBundleHandle* bundles, uint count) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderPassExecuteBundles(renderPass, bundles, count));

    public static uint ComputePassBegin(AlcoGPU.EncoderHandle encoder, AlcoGPU.TimestampWrites* timestampWrites, out AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassBegin(encoder, timestampWrites, out computePass));

    public static uint ComputePassEnd(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassEnd(computePass));

    /// <summary>Consumes an open compute pass without validating or ending it.</summary>
    public static uint ComputePassRelease(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassRelease(computePass));

    public static uint ComputePassSetPipeline(AlcoGPU.ComputePassHandle computePass, AlcoGPU.ComputePipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetPipeline(computePass, pipeline));

    public static uint ComputePassSetBindGroup(AlcoGPU.ComputePassHandle computePass, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetBindGroup(computePass, slot, group));

    public static uint ComputePassSetImmediates(AlcoGPU.ComputePassHandle computePass, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassSetImmediates(computePass, offset, data, size));

    public static uint ComputePassDispatchWorkgroups(AlcoGPU.ComputePassHandle computePass, uint x, uint y, uint z) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassDispatchWorkgroups(computePass, x, y, z));

    public static uint ComputePassDispatchWorkgroupsIndirect(AlcoGPU.ComputePassHandle computePass, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassDispatchWorkgroupsIndirect(computePass, buffer, offset));

    public static uint ComputePassWriteTimestamp(AlcoGPU.ComputePassHandle computePass, AlcoGPU.QuerySetHandle querySet, uint queryIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassWriteTimestamp(computePass, querySet, queryIndex));

    public static uint ComputePassInsertDebugMarker(AlcoGPU.ComputePassHandle computePass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassInsertDebugMarker(computePass, label));

    public static uint ComputePassPushDebugGroup(AlcoGPU.ComputePassHandle computePass, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassPushDebugGroup(computePass, label));

    public static uint ComputePassPopDebugGroup(AlcoGPU.ComputePassHandle computePass) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ComputePassPopDebugGroup(computePass));

    public static uint CopyBufferToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, ulong sourceOffset, AlcoGPU.BufferHandle destination, ulong destinationOffset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyBufferToBuffer(encoder, source, sourceOffset, destination, destinationOffset, size));

    public static uint CopyBufferToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle source, in AlcoGPU.CopyLayout sourceLayout, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint destinationAspect, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyBufferToTexture(encoder, source, in sourceLayout, destination, destinationMipLevel, destinationAspect, copySize));

    public static uint CopyTextureToBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, uint sourceAspect, AlcoGPU.BufferHandle destination, in AlcoGPU.CopyLayout destinationLayout, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyTextureToBuffer(encoder, source, sourceMipLevel, sourceAspect, destination, in destinationLayout, copySize));

    public static uint CopyTextureToTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle source, uint sourceMipLevel, AlcoGPU.TextureHandle destination, uint destinationMipLevel, uint aspect, AlcoGPU.Extent3D copySize) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.CopyTextureToTexture(encoder, source, sourceMipLevel, destination, destinationMipLevel, aspect, copySize));

    public static uint ResolveQuerySet(AlcoGPU.EncoderHandle encoder, AlcoGPU.QuerySetHandle querySet, uint firstQuery, uint queryCount, AlcoGPU.BufferHandle destination, ulong destinationOffset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.ResolveQuerySet(encoder, querySet, firstQuery, queryCount, destination, destinationOffset));

    /// <summary>Clears a buffer range to zero; a size of zero clears to the end of the buffer.</summary>
    public static uint EncoderClearBuffer(AlcoGPU.EncoderHandle encoder, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderClearBuffer(encoder, buffer, offset, size));

    public static uint EncoderClearTexture(AlcoGPU.EncoderHandle encoder, AlcoGPU.TextureHandle texture, AlcoGPU.SubresourceRange range) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderClearTexture(encoder, texture, range));

    public static uint EncoderInsertDebugMarker(AlcoGPU.EncoderHandle encoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderInsertDebugMarker(encoder, label));

    public static uint EncoderPushDebugGroup(AlcoGPU.EncoderHandle encoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderPushDebugGroup(encoder, label));

    public static uint EncoderPopDebugGroup(AlcoGPU.EncoderHandle encoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.EncoderPopDebugGroup(encoder));

    public static uint QuerySetCreate(AlcoGPU.DeviceHandle device, uint count, byte* name, out AlcoGPU.QuerySetHandle querySet) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QuerySetCreate(device, count, name, out querySet));

    public static uint QuerySetDestroy(AlcoGPU.QuerySetHandle querySet) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QuerySetDestroy(querySet));

    public static uint QueueWriteBuffer(AlcoGPU.DeviceHandle device, AlcoGPU.BufferHandle buffer, ulong offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueWriteBuffer(device, buffer, offset, data, size));

    public static uint QueueWriteTexture(AlcoGPU.DeviceHandle device, AlcoGPU.TextureHandle texture, uint mipLevel, AlcoGPU.Origin3D origin, uint aspect, byte* data, uint dataSize, in AlcoGPU.CopyLayout layout, AlcoGPU.Extent3D size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueWriteTexture(device, texture, mipLevel, origin, aspect, data, dataSize, in layout, size));

    /// <summary>Submits a command buffer; the native side consumes the handle.</summary>
    public static uint QueueSubmit(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle commandBuffer, ulong* outIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueSubmit(device, commandBuffer, outIndex));

    /// <summary>Submits an array of command buffers as one submission; the native side consumes every handle.</summary>
    public static uint QueueSubmitBatch(AlcoGPU.DeviceHandle device, AlcoGPU.CommandBufferHandle* commandBuffers, uint count, ulong* outIndex) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.QueueSubmitBatch(device, commandBuffers, count, outIndex));

    public static uint BundleEncoderCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.BundleEncoderDesc desc, out AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderCreate(device, in desc, out bundleEncoder));

    public static uint BundleEncoderFinish(AlcoGPU.BundleEncoderHandle bundleEncoder, out AlcoGPU.RenderBundleHandle bundle) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderFinish(bundleEncoder, out bundle));

    /// <summary>Destroys a bundle encoder that was never finished.</summary>
    public static uint BundleEncoderDestroy(AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleEncoderDestroy(bundleEncoder));

    public static uint RenderBundleDestroy(AlcoGPU.RenderBundleHandle bundle) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.RenderBundleDestroy(bundle));

    public static uint BundleSetPipeline(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.GraphicsPipelineHandle pipeline) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetPipeline(bundleEncoder, pipeline));

    public static uint BundleSetBindGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BindGroupHandle group) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetBindGroup(bundleEncoder, slot, group));

    /// <summary>Binds a vertex buffer to a slot; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint BundleSetVertexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, uint slot, AlcoGPU.BufferHandle buffer, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetVertexBuffer(bundleEncoder, slot, buffer, offset, size));

    /// <summary>Binds the index buffer; a size of zero binds the rest of the buffer from the offset.</summary>
    public static uint BundleSetIndexBuffer(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, uint format, ulong offset, ulong size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetIndexBuffer(bundleEncoder, buffer, format, offset, size));

    public static uint BundleSetImmediates(AlcoGPU.BundleEncoderHandle bundleEncoder, uint offset, byte* data, uint size) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleSetImmediates(bundleEncoder, offset, data, size));

    public static uint BundleDraw(AlcoGPU.BundleEncoderHandle bundleEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDraw(bundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance));

    public static uint BundleDrawIndexed(AlcoGPU.BundleEncoderHandle bundleEncoder, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndexed(bundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance));

    public static uint BundleDrawIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndirect(bundleEncoder, buffer, offset));

    public static uint BundleDrawIndexedIndirect(AlcoGPU.BundleEncoderHandle bundleEncoder, AlcoGPU.BufferHandle buffer, ulong offset) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleDrawIndexedIndirect(bundleEncoder, buffer, offset));

    public static uint BundleInsertDebugMarker(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundleInsertDebugMarker(bundleEncoder, label));

    public static uint BundlePushDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder, byte* label) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundlePushDebugGroup(bundleEncoder, label));

    public static uint BundlePopDebugGroup(AlcoGPU.BundleEncoderHandle bundleEncoder) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.BundlePopDebugGroup(bundleEncoder));

    public static uint SurfaceCreate(AlcoGPU.DeviceHandle device, in AlcoGPU.SurfaceDesc desc, out AlcoGPU.SurfaceHandle surface) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceCreate(device, in desc, out surface));

    public static uint SurfaceGetCapabilities(AlcoGPU.SurfaceHandle surface, ref AlcoGPU.SurfaceCapabilities caps) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceGetCapabilities(surface, ref caps));

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

    public static uint SurfaceDestroy(AlcoGPU.SurfaceHandle surface) =>
        AlcoGpuMarshal.ThrowIfFailure(AlcoGpuRaw.SurfaceDestroy(surface));
}
