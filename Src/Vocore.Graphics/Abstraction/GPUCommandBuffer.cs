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

    public void SetFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetFrameBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetFrameBufferCore(frameBuffer);
    }

    public void SetPipeline(GPUPipeline pipeline)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetPipelineCore(pipeline);
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

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndirectCore(indirectBuffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        DrawIndexedIndirectCore(indirectBuffer, offset, drawCount, stride);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while UpdateBuffer, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        UpdateBufferCore(buffer, bufferOffset, data, size);
    }

    public void SetResourceGroup(uint slot, GPUResourceGroup resourceGroup)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        SetResourceGroupCore(slot, resourceGroup);
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
    protected abstract void SetPipelineCore(GPUPipeline pipeline);
    protected abstract void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size);
    protected abstract void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);
    protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride);
    protected abstract void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride);
    protected abstract void SetResourceGroupCore(uint slot, GPUResourceGroup resourceGroup);
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);
}