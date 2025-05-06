using System.Runtime.CompilerServices;

namespace Alco.Graphics;


/// <summary>
/// The reusable sub command buffer for rendering. Can be executed by <see cref="GPUCommandBuffer.ExecuteBundle(GPURenderBundle)"/> or <see cref="GPUCommandBuffer.ExecuteBundle(ReadOnlySpan{GPURenderBundle})"/>.
/// </summary>
public unsafe abstract class GPURenderBundle : BaseGPUObject
{
    protected bool _isRecording = false;
    //API
    public abstract bool HasBuffer { get; }
    public virtual bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isRecording;
    }

    protected GPURenderBundle(in RenderBundleDescriptor? descriptor) : base(descriptor?.Name ?? "unnamed_reusable_render_buffer")
    {
    }

    public void Begin(GPURenderPass renderPass)
    {
        UtilsAssert.IsFalse(_isRecording, "Command buffer is already recording, you might call GPUCommandBuffer.Begin(GPURenderPass) twice before calling GPUCommandBuffer.End()");
        _isRecording = true;
        BeginCore(renderPass);
    }

    public void End()
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording, you might call GPUCommandBuffer.End() twice before calling GPUCommandBuffer.Begin(GPURenderPass)");
        _isRecording = false;
        EndCore();
    }

    //graphics

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

    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
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

    public void PushGraphicsConstants(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        UtilsAssert.IsTrue(_isRecording, "Command buffer is not recording while PushGraphicsConstants, try start recording by calling GPUCommandBuffer.Begin(GPURenderPass)");
        PushGraphicsConstantsCore(stage, bufferOffset, data, size);
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

    // need to be implemented for each backend
    protected abstract void BeginCore(GPURenderPass renderPass);
    protected abstract void EndCore();
    protected abstract void SetGraphicsPipelineCore(GPUPipeline pipeline);
    protected abstract void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size);
    protected abstract void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);
    protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
    protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void PushGraphicsConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size);
}