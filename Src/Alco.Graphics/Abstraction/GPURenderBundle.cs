using System.Runtime.CompilerServices;

namespace Alco.Graphics;


/// <summary>
/// The reusable sub command buffer for rendering. Can be executed by <see cref="GPUCommandBuffer.ExecuteBundle(GPURenderBundle)"/> or <see cref="GPUCommandBuffer.ExecuteBundle(ReadOnlySpan{GPURenderBundle})"/>.
/// </summary>
public unsafe abstract class GPURenderBundle : BaseGPUObject
{
    protected bool _isRecording = false;
    private List<BaseGPUObject> _recordedResources = new();
    private List<BaseGPUObject> _recordingResources = new();

    /// <summary>Gets whether the bundle holds recorded commands; a bundle without recorded commands cannot be executed.</summary>
    public abstract bool HasBuffer { get; }

    /// <summary>Gets whether the bundle is recording (between <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>).</summary>
    public virtual bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isRecording;
    }

    protected GPURenderBundle(in RenderBundleDescriptor? descriptor) : base(descriptor?.Name ?? "unnamed_reusable_render_buffer")
    {
    }

    /// <summary>
    /// Begins recording graphics commands into this bundle for later replay; the recorded commands
    /// can be replayed by <see cref="GPUCommandBuffer.RenderPass.ExecuteBundle(GPURenderBundle)"/> in
    /// any later frame. The attachment layout declares the render target formats the recording
    /// expects; it must be compatible with the frame buffer of the pass executing the bundle.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout the recorded commands target.</param>
    public void Begin(GPUAttachmentLayout attachmentLayout)
    {
        AssetUtility.IsFalse(_isRecording, "Command buffer is already recording, you might call GPURenderBundle.Begin(GPUAttachmentLayout) twice before calling GPURenderBundle.End()");
        _isRecording = true;
        _recordingResources.Clear();
        BeginCore(attachmentLayout);
    }

    /// <summary>
    /// Finishes recording; the bundle can then be replayed in render passes whose frame buffer is
    /// compatible with the attachment layout passed to <see cref="Begin(GPUAttachmentLayout)"/>.
    /// </summary>
    public void End()
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording, you might call GPURenderBundle.End() twice before calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _isRecording = false;
        EndCore();

        // A native render bundle retains native handles, but the GC cannot see that ownership.
        // Keep the managed GPU wrappers alive so their finalizers cannot destroy resources that
        // are still referenced by the recorded bundle. Swap only after EndCore has replaced the
        // previous native bundle.
        List<BaseGPUObject> previousResources = _recordedResources;
        _recordedResources = _recordingResources;
        _recordingResources = previousResources;
        _recordingResources.Clear();
    }

    // Graphics recording commands

    /// <summary>
    /// Records the graphics pipeline binding used by subsequent draws; must be called between
    /// <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="pipeline">The graphics pipeline to bind.</param>
    public void SetGraphicsPipeline(GPUPipeline pipeline)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while SetPipeline, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(pipeline);
        SetGraphicsPipelineCore(pipeline);
    }

    /// <summary>
    /// Records a resource group binding to a bind group slot for subsequent draws; must be called
    /// between <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="slot">The bind group slot index.</param>
    /// <param name="resourceGroup">The resource group to bind.</param>
    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while SetResourceGroup, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(resourceGroup);
        SetGraphicsResourcesCore(slot, resourceGroup);
    }

    /// <summary>
    /// Records a vertex buffer range binding to a vertex slot; must be called between
    /// <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="slot">The vertex buffer slot index.</param>
    /// <param name="buffer">The vertex buffer to bind.</param>
    /// <param name="offset">The offset of the range within the buffer, in bytes.</param>
    /// <param name="size">The size of the range, in bytes.</param>
    public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while SetVertexBuffer, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(buffer);
        SetVertexBufferCore(slot, buffer, offset, size);
    }

    /// <summary>
    /// Records an index buffer range binding used by subsequent indexed draws; must be called
    /// between <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="buffer">The index buffer to bind.</param>
    /// <param name="format">The index format (16- or 32-bit).</param>
    /// <param name="offset">The offset of the range within the buffer, in bytes.</param>
    /// <param name="size">The size of the range, in bytes.</param>
    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while SetIndexBuffer, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(buffer);
        SetIndexBufferCore(buffer, format, offset, size);
    }

    /// <summary>
    /// Records a non-indexed draw; must be called between <see cref="Begin(GPUAttachmentLayout)"/>
    /// and <see cref="End"/>.
    /// </summary>
    /// <param name="vertexCount">The number of vertices to draw.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="firstVertex">The index of the first vertex to draw.</param>
    /// <param name="firstInstance">The index of the first instance to draw.</param>
    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while Draw, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>
    /// Records an indexed draw; must be called between <see cref="Begin(GPUAttachmentLayout)"/> and
    /// <see cref="End"/>.
    /// </summary>
    /// <param name="indexCount">The number of indices to draw.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="firstIndex">The index of the first index to draw.</param>
    /// <param name="vertexOffset">A value added to each index before vertex fetch.</param>
    /// <param name="firstInstance">The index of the first instance to draw.</param>
    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexed, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    /// <summary>
    /// Records an indirect non-indexed draw whose arguments (one <see cref="IndirectData"/> record)
    /// are read from a GPU buffer; must be called between <see cref="Begin(GPUAttachmentLayout)"/>
    /// and <see cref="End"/>.
    /// </summary>
    /// <param name="indirectBuffer">The buffer holding the draw arguments.</param>
    /// <param name="offset">The byte offset of the record.</param>
    public void DrawIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while DrawIndirect, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(indirectBuffer);
        DrawIndirectCore(indirectBuffer, offset);
    }

    /// <summary>
    /// Records an indirect indexed draw whose arguments (one <see cref="IndexedIndirectData"/>
    /// record) are read from a GPU buffer; must be called between
    /// <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="indirectBuffer">The buffer holding the draw arguments.</param>
    /// <param name="offset">The byte offset of the record.</param>
    public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while DrawIndexedIndirect, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        _recordingResources.Add(indirectBuffer);
        DrawIndexedIndirectCore(indirectBuffer, offset);
    }

    /// <summary>
    /// Records a graphics push-constants write from raw memory; must be called between
    /// <see cref="Begin(GPUAttachmentLayout)"/> and <see cref="End"/>.
    /// </summary>
    /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
    /// <param name="data">A pointer to the constant data.</param>
    /// <param name="size">The size of the data, in bytes.</param>
    public void PushGraphicsConstants(uint bufferOffset, byte* data, uint size)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while PushGraphicsConstants, try start recording by calling GPURenderBundle.Begin(GPUAttachmentLayout)");
        PushGraphicsConstantsCore(bufferOffset, data, size);
    }

    /// <summary>Pushes an unmanaged value as graphics constants; see <see cref="PushGraphicsConstants(uint, byte*, uint)"/>.</summary>
    /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
    /// <param name="data">The value to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushGraphicsConstants<T>(uint bufferOffset, T data) where T : unmanaged
    {
        PushGraphicsConstants(bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    /// <summary>Pushes an unmanaged value as graphics constants at offset 0; see <see cref="PushGraphicsConstants(uint, byte*, uint)"/>.</summary>
    /// <param name="data">The value to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void PushGraphicsConstants<T>(T data) where T : unmanaged
    {
        PushGraphicsConstants(0, data);
    }



    // polymorphism

    /// <summary>Binds the whole buffer to the vertex slot; see <see cref="SetVertexBuffer(uint, GPUBuffer, ulong, ulong)"/>.</summary>
    /// <param name="slot">The vertex buffer slot index.</param>
    /// <param name="buffer">The vertex buffer to bind.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVertexBuffer(uint slot, GPUBuffer buffer)
    {
        SetVertexBuffer(slot, buffer, 0, buffer.Size);
    }

    /// <summary>Binds the whole buffer as the index buffer; see <see cref="SetIndexBuffer(GPUBuffer, IndexFormat, ulong, ulong)"/>.</summary>
    /// <param name="buffer">The index buffer to bind.</param>
    /// <param name="format">The index format (16- or 32-bit).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format)
    {
        SetIndexBuffer(buffer, format, 0, buffer.Size);
    }

    /// <summary>Backend-specific implementation.</summary>
    protected abstract void BeginCore(GPUAttachmentLayout attachmentLayout);
    protected abstract void EndCore();
    protected abstract void SetGraphicsPipelineCore(GPUPipeline pipeline);
    protected abstract void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size);
    protected abstract void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size);
    protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
    protected abstract void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);
    protected abstract void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size);
}
