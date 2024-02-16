using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

/// <summary>
/// The buffer to record GPU commands which used for rendering and compute.
/// </summary> 
public abstract class GPUCommandBuffer : BaseGPUObject
{
    protected bool _isRecording = false;
    //API
    public abstract bool HasBuffer { get; }
    public virtual bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isRecording;
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
        _isRecording = false;
        EndCore();
    }

    //graphics

    public void SetFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetFrameBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetFrameBufferCore(frameBuffer);
    }

    public void ClearFrame(ColorFloat color, float depth = 1.0f, uint stencil = 0)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while ClearColor, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        ClearFrameCore(color, depth, stencil);
    }

    public void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetGraphicsPipelineCore(pipeline);
    }

    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetGraphicsResourcesCore(slot, resourceGroup);
    }

    public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetVertexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetVertexBufferCore(slot, buffer, offset, size);
    }

    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format,  ulong offset, ulong size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetIndexBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetIndexBufferCore(buffer, format, offset, size);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while Draw, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndirectCore(indirectBuffer, offset);
    }

    public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedIndirectCore(indirectBuffer, offset);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while UpdateBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        UpdateBufferCore(buffer, bufferOffset, data, size);
    }

    //compute

    public void SetComputePipeline(GPUPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetComputePipelineCore(pipeline);
    }

    public void SetComputeResources(uint slot, GPUResourceGroup resourceGroup)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetComputeResourcesCore(slot, resourceGroup);
    }

    public void DispatchCompute(uint x, uint y, uint z)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DispatchCompute, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DispatchComputeCore(x, y, z);
    }

    public void DispatchComputeIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DispatchComputeIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DispatchComputeIndirectCore(indirectBuffer, offset);
    }

    // polymorphism

    public void SetVertexBuffer(uint slot, GPUBuffer buffer)
    {
        SetVertexBuffer(slot, buffer, 0, buffer.Size);
    }

    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format)
    {
        SetIndexBuffer(buffer, format, 0, buffer.Size);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, byte* data, uint size)
    {
        UpdateBuffer(buffer, 0, data, size);
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            UpdateBuffer(buffer, bufferOffset, (byte*)ptr, (uint)(sizeof(T) * data.Length));
        }
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T[] data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, data);
    }


    // need to be implemented for each backend
    protected abstract void BeginCore();
    protected abstract void EndCore();
    protected abstract void SetFrameBufferCore(GPUFrameBuffer frameBuffer);
    protected abstract void ClearFrameCore(ColorFloat color, float depth, uint stencil);
    protected abstract void SetGraphicsPipelineCore(GPUPipeline pipeline);
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
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);
}