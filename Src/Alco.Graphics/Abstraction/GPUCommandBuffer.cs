using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// The buffer to record GPU commands which used for rendering and compute.
/// </summary> 
public abstract class GPUCommandBuffer : BaseGPUObject
{
    protected bool _isRecording = false;
    //API
    public abstract bool HasBuffer { get; }

    public bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _isRecording);
    }

    protected GPUCommandBuffer(in CommandBufferDescriptor? descriptor): base(descriptor?.Name ?? "unnamed_command_buffer")
    {
    }

    public void Begin()
    {
        UtilsAssert.IsFalse(_isRecording, "Command buffer is already recording, you might call GPUCommandBuffer.Begin(GPURenderPass) twice before calling GPUCommandBuffer.End()");
        _isRecording = true;
        BeginCore();
    }

    public void End()
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording, you might call GPUCommandBuffer.End() twice before calling GPUCommandBuffer.Begin(GPURenderPass)");
        EndCore();
        _isRecording = false;
    }

    //graphics

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetFrameBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetFrameBufferCore(frameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearColor(ColorFloat color, uint index = 0)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while ClearColor, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        ClearColorCore(color, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearDepth(float depth)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while ClearDepth, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        ClearDepthCore(depth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearStencil(uint stencil)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while ClearDepthStencil, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        ClearStencilCore(stencil);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetScissorRect(uint x, uint y, uint width, uint height)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetScissorRect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetScissorRectCore(x, y, width, height);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetGraphicsPipelineCore(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStencilReference(uint value)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetStencilReference, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetStencilReferenceCore(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetGraphicsResourcesCore(slot, resourceGroup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetVertexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetVertexBufferCore(slot, buffer, offset, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format,  ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetIndexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetIndexBufferCore(buffer, format, offset, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while Draw, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndirectCore(indirectBuffer, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedIndirectCore(indirectBuffer, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushGraphicsConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while UpdateBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        PushGraphicsConstantsCore(stage, bufferOffset, data, size);
    }

    //compute

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComputePipeline(GPUPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetComputePipelineCore(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComputeResources(uint slot, GPUResourceGroup resourceGroup)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetComputeResourcesCore(slot, resourceGroup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DispatchCompute(uint x, uint y, uint z)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DispatchCompute, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DispatchComputeCore(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DispatchComputeIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DispatchComputeIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DispatchComputeIndirectCore(indirectBuffer, offset);
    }

    // polymorphism

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
    public unsafe void PushGraphicsConstants<T>(ShaderStage stage, uint bufferOffset, T data) where T : unmanaged
    {
        PushGraphicsConstants(stage, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushGraphicsConstants<T>(ShaderStage stage, T data) where T : unmanaged
    {
        PushGraphicsConstants(stage, 0, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushComputeConstants<T>(uint bufferOffset, T data) where T : unmanaged
    {
        PushComputeConstants(bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushComputeConstants<T>(T data) where T : unmanaged
    {
        PushComputeConstants(0, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushComputeConstants(uint bufferOffset, byte* data, uint size)
    {
        PushComputeConstantsCore(bufferOffset, data, size);
    }


    public void CopyBuffer(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while CopyBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
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
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while CopyBufferToTexture, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        CopyBufferToTextureCore(src, dst, mipLevel, offset, aspect);
    }



    // need to be implemented for each backend
    protected abstract void BeginCore();
    protected abstract void EndCore();
    protected abstract void SetFrameBufferCore(GPUFrameBuffer frameBuffer);
    protected abstract void ClearColorCore(ColorFloat color, uint index);



    protected abstract void ClearDepthCore(float depth);
    protected abstract void ClearStencilCore(uint stencil);
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

    protected abstract void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size);
    protected abstract void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect);
}