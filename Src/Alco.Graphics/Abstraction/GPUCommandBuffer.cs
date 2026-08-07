using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// The buffer to record GPU commands which used for rendering and compute.
/// </summary> 
public abstract class GPUCommandBuffer : BaseGPUObject
{
    public readonly struct RenderPass : IDisposable
    {
        private readonly GPUCommandBuffer _commandBuffer;

        internal RenderPass(GPUCommandBuffer commandBuffer)
        {
            _commandBuffer = commandBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetScissorRect(uint x, uint y, uint width, uint height)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetScissorRect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetScissorRectCore(x, y, width, height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPipeline(GPUPipeline pipeline)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetGraphicsPipelineCore(pipeline);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetStencilReference(uint value)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetStencilReference, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetStencilReferenceCore(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResources(uint slot, GPUResourceGroup resourceGroup)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetResources, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetGraphicsResourcesCore(slot, resourceGroup);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetVertexBuffer, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetVertexBufferCore(slot, buffer, offset, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetIndexBuffer, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetIndexBufferCore(buffer, format, offset, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while Draw, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndirectCore(indirectBuffer, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndexedIndirectCore(indirectBuffer, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while PushConstants, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.PushGraphicsConstantsCore(stage, bufferOffset, data, size);
        }

        // polymorphism overloads
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVertexBuffer(uint slot, GPUBuffer buffer)
        {
            SetVertexBuffer(slot, buffer, 0, buffer.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format)
        {
            SetIndexBuffer(buffer, format, 0, buffer.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(ShaderStage stage, uint bufferOffset, T data) where T : unmanaged
        {
            PushConstants(stage, bufferOffset, (byte*)&data, (uint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(ShaderStage stage, T data) where T : unmanaged
        {
            PushConstants(stage, 0, data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteBundle(GPURenderBundle bundle)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while ExecuteBundle, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.ExecuteBundleCore(bundle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteBundle(ReadOnlySpan<GPURenderBundle> bundles)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while ExecuteBundle, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.ExecuteBundleCore(bundles);
        }

        /// <summary>
        /// Writes a timestamp inside this open render pass. If the device does not
        /// support <see cref="GPUDevice.TimestampQueryInsidePassesSupported"/>, this
        /// method is a no-op (the timing is silently disabled) so callers can use it
        /// unconditionally for maximum device compatibility.
        /// </summary>
        /// <param name="querySet">The destination timestamp query set.</param>
        /// <param name="queryIndex">The slot to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTimestamp(GPUTimestampQuerySet querySet, uint queryIndex)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while WriteTimestamp, try start recording by calling GPUCommandBuffer.BeginRender()");
            if (!_commandBuffer.Device.TimestampQueryInsidePassesSupported)
            {
                return;
            }
            if (queryIndex >= querySet.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            _commandBuffer.WriteTimestampInsidePassCore(querySet, queryIndex);
        }

        public void Dispose()
        {
            _commandBuffer.EndRenderCore();
            _commandBuffer._isRecordingRender = false;
        }
    }

    public readonly struct ComputePass : IDisposable
    {
        private readonly GPUCommandBuffer _commandBuffer;

        internal ComputePass(GPUCommandBuffer commandBuffer)
        {
            _commandBuffer = commandBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPipeline(GPUPipeline pipeline)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.SetComputePipelineCore(pipeline);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResources(uint slot, GPUResourceGroup resourceGroup)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while SetResources, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.SetComputeResourcesCore(slot, resourceGroup);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchCompute(uint x, uint y, uint z)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while DispatchCompute, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.DispatchComputeCore(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchComputeIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while DispatchComputeIndirect, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.DispatchComputeIndirectCore(indirectBuffer, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants(uint bufferOffset, byte* data, uint size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while PushConstants, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.PushComputeConstantsCore(bufferOffset, data, size);
        }

        // polymorphism overloads
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(uint bufferOffset, T data) where T : unmanaged
        {
            PushConstants(bufferOffset, (byte*)&data, (uint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(T data) where T : unmanaged
        {
            PushConstants(0, data);
        }

        /// <summary>
        /// Writes a timestamp inside this open compute pass. If the device does not
        /// support <see cref="GPUDevice.TimestampQueryInsidePassesSupported"/>, this
        /// method is a no-op (the timing is silently disabled) so callers can use it
        /// unconditionally for maximum device compatibility.
        /// </summary>
        /// <param name="querySet">The destination timestamp query set.</param>
        /// <param name="queryIndex">The slot to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTimestamp(GPUTimestampQuerySet querySet, uint queryIndex)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while WriteTimestamp, try start recording by calling GPUCommandBuffer.BeginCompute()");
            if (!_commandBuffer.Device.TimestampQueryInsidePassesSupported)
            {
                return;
            }
            if (queryIndex >= querySet.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            _commandBuffer.WriteTimestampInsidePassCore(querySet, queryIndex);
        }

        public void Dispose()
        {
            _commandBuffer.EndComputeCore();
            _commandBuffer._isRecordingCompute = false;
        }
    }

    protected bool _isRecording = false;

    //new api
    protected bool _isRecordingRender = false;
    protected bool _isRecordingCompute = false;

    //API
    public abstract bool HasBuffer { get; }

    public bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _isRecording);
    }

    protected GPUCommandBuffer(in CommandBufferDescriptor? descriptor) : base(descriptor?.Name ?? "unnamed_command_buffer")
    {
    }

    public void Begin()
    {
        AssetUtility.IsFalse(_isRecording, "Command buffer is already recording, you might call GPUCommandBuffer.Begin() twice before calling GPUCommandBuffer.End()");
        _isRecording = true;
        BeginCore();
    }

    public void End()
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording, you might call GPUCommandBuffer.End() twice before calling GPUCommandBuffer.Begin()");
        EndCore();
        _isRecording = false;
    }

    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        if (_isRecordingRender)
        {
            throw new InvalidOperationException("Render pass is already recording, try end current Render pass before starting a new one");
        }

        if (_isRecordingCompute)
        {
            throw new InvalidOperationException("Compute pass is already recording, try end current Compute pass before starting a new one");
        }

        BeginRenderCore(frameBuffer, clearColors, clearDepth, clearStencil);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        Vector4 clearColor,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        if (_isRecordingRender)
        {
            throw new InvalidOperationException("Render pass is already recording, try end current Render pass before starting a new one");
        }

        if (_isRecordingCompute)
        {
            throw new InvalidOperationException("Compute pass is already recording, try end current Compute pass before starting a new one");
        }

        ReadOnlySpan<ClearColorData> clearColorsSpan = stackalloc ClearColorData[1] { new ClearColorData(0, clearColor) };
        BeginRenderCore(frameBuffer, clearColorsSpan, clearDepth, clearStencil);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer
        )
    {
        return BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, null, null);
    }

    /// <summary>
    /// Begins a render pass and writes timestamps at its beginning and end.
    /// </summary>
    /// <param name="frameBuffer">The target framebuffer.</param>
    /// <param name="clearColors">Attachment clear values.</param>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginningQueryIndex">The slot written when the pass begins.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <returns>An RAII render-pass scope.</returns>
    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint beginningQueryIndex,
        uint endQueryIndex,
        float? clearDepth = null,
        uint? clearStencil = null)
    {
        if (_isRecordingRender)
        {
            throw new InvalidOperationException("Render pass is already recording, try end current Render pass before starting a new one");
        }
        if (_isRecordingCompute)
        {
            throw new InvalidOperationException("Compute pass is already recording, try end current pass before starting a new one");
        }
        if (beginningQueryIndex >= querySet.Count || endQueryIndex >= querySet.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(beginningQueryIndex));
        }

        BeginRenderTimestampCore(frameBuffer, clearColors, querySet, beginningQueryIndex, endQueryIndex, clearDepth, clearStencil);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    public ComputePass BeginCompute()
    {
        if (_isRecordingRender)
        {
            throw new InvalidOperationException("Render pass is already recording, try end current Render pass before starting a new one");
        }

        if (_isRecordingCompute)
        {
            throw new InvalidOperationException("Compute pass is already recording, try end current Compute pass before starting a new one");
        }

        BeginComputeCore();
        _isRecordingCompute = true;
        return new ComputePass(this);
    }

    /// <summary>
    /// Begins a compute pass and writes timestamps at its beginning and end.
    /// </summary>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginningQueryIndex">The slot written when the pass begins.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends.</param>
    /// <returns>An RAII compute-pass scope.</returns>
    public ComputePass BeginCompute(
        GPUTimestampQuerySet querySet,
        uint beginningQueryIndex,
        uint endQueryIndex)
    {
        if (_isRecordingRender || _isRecordingCompute)
        {
            throw new InvalidOperationException("Another GPU pass is already recording.");
        }
        if (beginningQueryIndex >= querySet.Count || endQueryIndex >= querySet.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(beginningQueryIndex));
        }

        BeginComputeTimestampCore(querySet, beginningQueryIndex, endQueryIndex);
        _isRecordingCompute = true;
        return new ComputePass(this);
    }

    /// <summary>Resolves timestamp query values into a query-resolve buffer.</summary>
    /// <param name="querySet">The source query set.</param>
    /// <param name="firstQuery">The first source query slot.</param>
    /// <param name="queryCount">The number of slots to resolve.</param>
    /// <param name="destination">A buffer created with <see cref="BufferUsage.QueryResolve"/>.</param>
    /// <param name="destinationOffset">The destination byte offset.</param>
    public void ResolveTimestamps(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset = 0)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer must be recording while resolving timestamps.");
        AssetUtility.IsFalse(_isRecordingRender || _isRecordingCompute, "End the active pass before resolving timestamps.");
        if (queryCount == 0 || firstQuery + queryCount > querySet.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(queryCount));
        }
        if ((destination.Usage & BufferUsage.QueryResolve) == 0)
        {
            throw new ArgumentException("The destination buffer does not support query resolve.", nameof(destination));
        }
        ResolveTimestampsCore(querySet, firstQuery, queryCount, destination, destinationOffset);
    }


    // API

    public void CopyBuffer(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while CopyBuffer, try start recording by calling GPUCommandBuffer.Begin()");
        CopyBufferCore(src, dst, srcOffset, dstOffset, size);
    }

    public void CopyBuffer(GPUBuffer src, GPUBuffer dst, ulong size)
    {
        CopyBuffer(src, dst, 0, 0, size);
    }


    public void CopyBuffer(GPUBuffer src, GPUBuffer dst)
    {
        CopyBuffer(src, dst, 0, 0, src.Size);
    }

    public void CopyBufferToTexture(GPUBuffer src, GPUTexture dst, uint mipLevel = 0, uint offset = 0, TextureAspect aspect = TextureAspect.All)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while CopyBufferToTexture, try start recording by calling GPUCommandBuffer.Begin()");
        CopyBufferToTextureCore(src, dst, mipLevel, offset, aspect);
    }

    /// <summary>
    /// Copy a region of one texture to another. Both textures must have a compatible
    /// pixel format and the copy must be recorded outside any render/compute pass.
    /// The source texture must have <see cref="TextureUsage.Read"/> (CopySrc) and the
    /// destination must have <see cref="TextureUsage.Write"/> (CopyDst).
    /// </summary>
    /// <param name="src">The source texture.</param>
    /// <param name="dst">The destination texture.</param>
    /// <param name="srcMipLevel">The source mip level.</param>
    /// <param name="dstMipLevel">The destination mip level.</param>
    /// <param name="aspect">The texture aspect to copy (All / DepthOnly / StencilOnly).</param>
    public void CopyTexture(GPUTexture src, GPUTexture dst, uint srcMipLevel = 0, uint dstMipLevel = 0, TextureAspect aspect = TextureAspect.All)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while CopyTexture, try start recording by calling GPUCommandBuffer.Begin()");
        CopyTextureCore(src, dst, srcMipLevel, dstMipLevel, aspect);
    }



    // need to be implemented for each backend
    protected abstract void BeginCore();
    protected abstract void EndCore();

    protected abstract void BeginRenderCore(GPUFrameBuffer frameBuffer, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth, uint? clearStencil);
    protected abstract void BeginRenderTimestampCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint beginningQueryIndex,
        uint endQueryIndex,
        float? clearDepth,
        uint? clearStencil);
    protected abstract void EndRenderCore();

    protected abstract void BeginComputeCore();
    protected abstract void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint beginningQueryIndex,
        uint endQueryIndex);
    protected abstract void WriteTimestampInsidePassCore(
        GPUTimestampQuerySet querySet,
        uint queryIndex);
    protected abstract void EndComputeCore();

    protected abstract void SetScissorRectCore(uint x, uint y, uint width, uint height);
    protected abstract void SetGraphicsPipelineCore(GPUPipeline pipeline);
    protected abstract void SetStencilReferenceCore(uint value);
    protected abstract void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size);
    protected abstract void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);
    protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
    protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void SetComputePipelineCore(GPUPipeline pipeline);
    protected abstract void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void DispatchComputeCore(uint x, uint y, uint z);
    protected abstract void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract unsafe void PushGraphicsConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size);
    protected abstract unsafe void PushComputeConstantsCore(uint bufferOffset, byte* data, uint size);

    protected abstract void ExecuteBundleCore(GPURenderBundle bundle);
    protected abstract void ExecuteBundleCore(ReadOnlySpan<GPURenderBundle> bundle);

    protected abstract void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size);
    protected abstract void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect);
    protected abstract void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect);
    protected abstract void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset);
}
