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



    // need to be implemented for each backend
    protected abstract void BeginCore();
    protected abstract void EndCore();

    protected abstract void BeginRenderCore(GPUFrameBuffer frameBuffer, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth, uint? clearStencil);
    protected abstract void EndRenderCore();

    protected abstract void BeginComputeCore();
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
}