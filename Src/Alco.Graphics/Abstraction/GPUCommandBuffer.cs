using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// Records GPU commands for rendering and compute passes, submitted to the device as a single unit.
/// </summary>
public abstract class GPUCommandBuffer : BaseGPUObject
{
    /// <summary>A recording scope for a render pass.</summary>
    public readonly struct RenderPass : IDisposable
    {
        private readonly GPUCommandBuffer _commandBuffer;

        internal RenderPass(GPUCommandBuffer commandBuffer)
        {
            _commandBuffer = commandBuffer;
        }

        /// <summary>
        /// Records a scissor rectangle restricting rasterization; only valid while the render pass is
        /// active (between <see cref="BeginRender(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>
        /// and disposing the pass).
        /// </summary>
        /// <param name="x">The x coordinate of the rectangle's top-left corner, in pixels.</param>
        /// <param name="y">The y coordinate of the rectangle's top-left corner, in pixels.</param>
        /// <param name="width">The width of the rectangle, in pixels.</param>
        /// <param name="height">The height of the rectangle, in pixels.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetScissorRect(uint x, uint y, uint width, uint height)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetScissorRect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetScissorRectCore(x, y, width, height);
        }

        /// <summary>
        /// Records the graphics pipeline binding used by subsequent draws; only valid while the render
        /// pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="pipeline">The graphics pipeline to bind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPipeline(GPUPipeline pipeline)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetGraphicsPipelineCore(pipeline);
        }

        /// <summary>
        /// Records the stencil reference value compared against by stencil tests; only valid while
        /// the render pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="value">The stencil reference value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetStencilReference(uint value)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetStencilReference, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetStencilReferenceCore(value);
        }

        /// <summary>
        /// Records a resource group binding to a bind group slot for subsequent draws; only valid
        /// while the render pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="slot">The bind group slot index.</param>
        /// <param name="resourceGroup">The resource group to bind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResources(uint slot, GPUResourceGroup resourceGroup)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetResources, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetGraphicsResourcesCore(slot, resourceGroup);
        }

        /// <summary>
        /// Records a vertex buffer range binding to a vertex slot; only valid while the render pass
        /// is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="slot">The vertex buffer slot index.</param>
        /// <param name="buffer">The vertex buffer to bind.</param>
        /// <param name="offset">The offset of the range within the buffer, in bytes.</param>
        /// <param name="size">The size of the range, in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetVertexBuffer, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetVertexBufferCore(slot, buffer, offset, size);
        }

        /// <summary>
        /// Records an index buffer range binding used by subsequent indexed draws; only valid while
        /// the render pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="buffer">The index buffer to bind.</param>
        /// <param name="format">The index format (16- or 32-bit).</param>
        /// <param name="offset">The offset of the range within the buffer, in bytes.</param>
        /// <param name="size">The size of the range, in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while SetIndexBuffer, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.SetIndexBufferCore(buffer, format, offset, size);
        }

        /// <summary>
        /// Records a non-indexed draw; only valid while the render pass is active (between
        /// BeginRender and disposing the pass).
        /// </summary>
        /// <param name="vertexCount">The number of vertices to draw.</param>
        /// <param name="instanceCount">The number of instances to draw.</param>
        /// <param name="firstVertex">The index of the first vertex to draw.</param>
        /// <param name="firstInstance">The index of the first instance to draw.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while Draw, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>
        /// Records an indexed draw; only valid while the render pass is active (between BeginRender
        /// and disposing the pass).
        /// </summary>
        /// <param name="indexCount">The number of indices to draw.</param>
        /// <param name="instanceCount">The number of instances to draw.</param>
        /// <param name="firstIndex">The index of the first index to draw.</param>
        /// <param name="vertexOffset">A value added to each index before vertex fetch.</param>
        /// <param name="firstInstance">The index of the first instance to draw.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndexed, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndexedCore(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        /// <summary>
        /// Records an indirect non-indexed draw whose arguments (one <see cref="IndirectData"/>
        /// record: vertexCount, instanceCount, firstVertex, firstInstance) are read from a GPU
        /// buffer; only valid while the render pass is active (between BeginRender and disposing
        /// the pass).
        /// </summary>
        /// <param name="indirectBuffer">The buffer holding the draw arguments.</param>
        /// <param name="offset">The byte offset of the record.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndirect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndirectCore(indirectBuffer, offset);
        }

        /// <summary>
        /// Records an indirect indexed draw whose arguments (one <see cref="IndexedIndirectData"/>
        /// record: indexCount, instanceCount, firstIndex, vertexOffset, firstInstance) are read
        /// from a GPU buffer; only valid while the render pass is active (between BeginRender and
        /// disposing the pass).
        /// </summary>
        /// <param name="indirectBuffer">The buffer holding the draw arguments.</param>
        /// <param name="offset">The byte offset of the record.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while DrawIndexedIndirect, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.DrawIndexedIndirectCore(indirectBuffer, offset);
        }

        /// <summary>
        /// Draws <paramref name="drawCount"/> meshes whose draw arguments (one
        /// <see cref="IndexedIndirectData"/> record each, 20-byte stride) are read
        /// consecutively from <paramref name="indirectBuffer"/> starting at
        /// <paramref name="offset"/>. Requires <see cref="GPUFeatures.MultiDrawIndirect"/>;
        /// backends without the feature fall back to one indirect draw per record.
        /// </summary>
        /// <param name="indirectBuffer">The buffer holding the indirect draw records.</param>
        /// <param name="offset">The byte offset of the first record.</param>
        /// <param name="drawCount">The number of consecutive records to draw.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MultiDrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while MultiDrawIndexedIndirect, try start recording by calling GPUCommandBuffer.BeginRender()");
            if (drawCount == 0)
            {
                return;
            }
            if (_commandBuffer.Device.IsFeatureSupported(GPUFeatures.MultiDrawIndirect))
            {
                _commandBuffer.MultiDrawIndexedIndirectCore(indirectBuffer, offset, drawCount);
            }
            else
            {
                for (uint i = 0; i < drawCount; i++)
                {
                    _commandBuffer.DrawIndexedIndirectCore(indirectBuffer, offset + i * 20);
                }
            }
        }

        /// <summary>
        /// Records a graphics push-constants write from raw memory; only valid while the render pass
        /// is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
        /// <param name="data">A pointer to the constant data.</param>
        /// <param name="size">The size of the data, in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants(uint bufferOffset, byte* data, uint size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while PushConstants, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.PushGraphicsConstantsCore(bufferOffset, data, size);
        }

        // polymorphism overloads
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

        /// <summary>Pushes an unmanaged value as graphics constants; see <see cref="PushConstants(uint, byte*, uint)"/>.</summary>
        /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
        /// <param name="data">The value to push.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(uint bufferOffset, T data) where T : unmanaged
        {
            PushConstants(bufferOffset, (byte*)&data, (uint)sizeof(T));
        }

        /// <summary>Pushes an unmanaged value as graphics constants at offset 0; see <see cref="PushConstants(uint, byte*, uint)"/>.</summary>
        /// <param name="data">The value to push.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(T data) where T : unmanaged
        {
            PushConstants(0, data);
        }

        /// <summary>
        /// Records execution of a previously recorded render bundle into this pass; only valid
        /// while the render pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="bundle">The render bundle to replay.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteBundle(GPURenderBundle bundle)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while ExecuteBundle, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.ExecuteBundleCore(bundle);
        }

        /// <summary>
        /// Records execution of several previously recorded render bundles into this pass, in order;
        /// only valid while the render pass is active (between BeginRender and disposing the pass).
        /// </summary>
        /// <param name="bundles">The render bundles to replay.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExecuteBundle(ReadOnlySpan<GPURenderBundle> bundles)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while ExecuteBundle, try start recording by calling GPUCommandBuffer.BeginRender()");
            _commandBuffer.ExecuteBundleCore(bundles);
        }

        /// <summary>
        /// Writes a timestamp inside this open render pass. If the device does not
        /// support <see cref="GPUFeatures.TimestampQueryInsidePasses"/>, this
        /// method is a no-op (the timing is silently disabled) so callers can use it
        /// unconditionally for maximum device compatibility.
        /// </summary>
        /// <param name="querySet">The destination timestamp query set.</param>
        /// <param name="queryIndex">The slot to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTimestamp(GPUTimestampQuerySet querySet, uint queryIndex)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingRender, "Render pass is not recording while WriteTimestamp, try start recording by calling GPUCommandBuffer.BeginRender()");
            if (!_commandBuffer.Device.IsFeatureSupported(GPUFeatures.TimestampQueryInsidePasses))
            {
                return;
            }
            if (queryIndex >= querySet.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            _commandBuffer.WriteTimestampInsidePassCore(querySet, queryIndex);
        }

        /// <summary>Ends this render pass and restores its recording state even on failure.</summary>
        public void Dispose()
        {
            try
            {
                _commandBuffer.EndRenderCore();
            }
            finally
            {
                _commandBuffer._isRecordingRender = false;
            }
        }
    }

    /// <summary>A recording scope for a compute pass.</summary>
    public readonly struct ComputePass : IDisposable
    {
        private readonly GPUCommandBuffer _commandBuffer;

        internal ComputePass(GPUCommandBuffer commandBuffer)
        {
            _commandBuffer = commandBuffer;
        }

        /// <summary>
        /// Records the compute pipeline binding used by subsequent dispatches; only valid while the
        /// compute pass is active (between <see cref="BeginCompute()"/> and disposing the pass).
        /// </summary>
        /// <param name="pipeline">The compute pipeline to bind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPipeline(GPUPipeline pipeline)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while SetPipeline, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.SetComputePipelineCore(pipeline);
        }

        /// <summary>
        /// Records a resource group binding to a bind group slot for subsequent dispatches; only
        /// valid while the compute pass is active (between BeginCompute and disposing the pass).
        /// </summary>
        /// <param name="slot">The bind group slot index.</param>
        /// <param name="resourceGroup">The resource group to bind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResources(uint slot, GPUResourceGroup resourceGroup)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while SetResources, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.SetComputeResourcesCore(slot, resourceGroup);
        }

        /// <summary>
        /// Records a compute dispatch of the given workgroup counts; only valid while the compute
        /// pass is active (between BeginCompute and disposing the pass).
        /// </summary>
        /// <param name="x">The workgroup count along x.</param>
        /// <param name="y">The workgroup count along y.</param>
        /// <param name="z">The workgroup count along z.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchCompute(uint x, uint y, uint z)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while DispatchCompute, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.DispatchComputeCore(x, y, z);
        }

        /// <summary>
        /// Records an indirect compute dispatch whose workgroup counts (x, y, z as consecutive
        /// u32 values) are read from a GPU buffer; only valid while the compute pass is active
        /// (between BeginCompute and disposing the pass).
        /// </summary>
        /// <param name="indirectBuffer">The buffer holding the workgroup counts.</param>
        /// <param name="offset">The byte offset of the counts.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchComputeIndirect(GPUBuffer indirectBuffer, uint offset)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while DispatchComputeIndirect, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.DispatchComputeIndirectCore(indirectBuffer, offset);
        }

        /// <summary>
        /// Records a compute push-constants write from raw memory; only valid while the compute pass
        /// is active (between BeginCompute and disposing the pass).
        /// </summary>
        /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
        /// <param name="data">A pointer to the constant data.</param>
        /// <param name="size">The size of the data, in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants(uint bufferOffset, byte* data, uint size)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while PushConstants, try start recording by calling GPUCommandBuffer.BeginCompute()");
            _commandBuffer.PushComputeConstantsCore(bufferOffset, data, size);
        }

        // polymorphism overloads
        /// <summary>Pushes an unmanaged value as compute constants; see <see cref="PushConstants(uint, byte*, uint)"/>.</summary>
        /// <param name="bufferOffset">The offset within the push-constants block, in bytes.</param>
        /// <param name="data">The value to push.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(uint bufferOffset, T data) where T : unmanaged
        {
            PushConstants(bufferOffset, (byte*)&data, (uint)sizeof(T));
        }

        /// <summary>Pushes an unmanaged value as compute constants at offset 0; see <see cref="PushConstants(uint, byte*, uint)"/>.</summary>
        /// <param name="data">The value to push.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void PushConstants<T>(T data) where T : unmanaged
        {
            PushConstants(0, data);
        }

        /// <summary>
        /// Writes a timestamp inside this open compute pass. If the device does not
        /// support <see cref="GPUFeatures.TimestampQueryInsidePasses"/>, this
        /// method is a no-op (the timing is silently disabled) so callers can use it
        /// unconditionally for maximum device compatibility.
        /// </summary>
        /// <param name="querySet">The destination timestamp query set.</param>
        /// <param name="queryIndex">The slot to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTimestamp(GPUTimestampQuerySet querySet, uint queryIndex)
        {
            AssetUtility.IsTrue(_commandBuffer._isRecordingCompute, "Compute pass is not recording while WriteTimestamp, try start recording by calling GPUCommandBuffer.BeginCompute()");
            if (!_commandBuffer.Device.IsFeatureSupported(GPUFeatures.TimestampQueryInsidePasses))
            {
                return;
            }
            if (queryIndex >= querySet.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            _commandBuffer.WriteTimestampInsidePassCore(querySet, queryIndex);
        }

        /// <summary>Ends this compute pass and restores its recording state even on failure.</summary>
        public void Dispose()
        {
            try
            {
                _commandBuffer.EndComputeCore();
            }
            finally
            {
                _commandBuffer._isRecordingCompute = false;
            }
        }
    }

    protected bool _isRecording = false;

    protected bool _isRecordingRender = false;
    protected bool _isRecordingCompute = false;

    /// <summary>Gets whether the command buffer holds recorded commands; an empty command buffer cannot be submitted.</summary>
    public abstract bool HasBuffer { get; }

    /// <summary>Gets whether the command buffer is recording (between <see cref="Begin"/> and <see cref="End"/>); a render or compute pass may additionally be active inside that scope.</summary>
    public bool IsRecording
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _isRecording);
    }

    protected GPUCommandBuffer(in CommandBufferDescriptor? descriptor) : base(descriptor?.Name ?? "unnamed_command_buffer")
    {
    }

    /// <summary>Begins recording; a failed begin leaves the command buffer idle.</summary>
    public void Begin()
    {
        AssetUtility.IsFalse(_isRecording, "Command buffer is already recording, you might call GPUCommandBuffer.Begin() twice before calling GPUCommandBuffer.End()");
        BeginCore();
        _isRecordingRender = _isRecordingCompute = false;
        _isRecording = true;
    }

    /// <summary>Finishes recording and restores all recording flags even on failure.</summary>
    public void End()
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording, you might call GPUCommandBuffer.End() twice before calling GPUCommandBuffer.Begin()");
        try
        {
            EndCore();
        }
        finally
        {
            _isRecording = _isRecordingRender = _isRecordingCompute = false;
        }
    }

    /// <summary>
    /// Begins a render pass into the frame buffer and returns its RAII recording scope; only one
    /// render or compute pass may be active at a time.
    /// </summary>
    /// <param name="frameBuffer">The target frame buffer.</param>
    /// <param name="clearColors">Per-color-attachment clear values.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <param name="colorOps">Optional per-color-attachment load/store ops, indexed by attachment. A clear specified through <paramref name="clearColors"/> takes precedence over the load op.</param>
    /// <param name="depthOps">Optional depth/stencil load/store ops. <paramref name="clearDepth"/> and <paramref name="clearStencil"/> take precedence over the corresponding load/store ops.</param>
    /// <returns>An RAII render-pass scope; dispose it to end the pass.</returns>
    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth = null,
        uint? clearStencil = null,
        ReadOnlySpan<AttachmentOps> colorOps = default,
        AttachmentOps? depthOps = null
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

        BeginRenderCore(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    /// <summary>
    /// Begins a render pass into the frame buffer with a single clear color applied to the first
    /// color attachment; only one render or compute pass may be active at a time.
    /// </summary>
    /// <param name="frameBuffer">The target frame buffer.</param>
    /// <param name="clearColor">The clear value for color attachment 0.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <param name="colorOps">Optional per-color-attachment load/store ops, indexed by attachment. The clear specified through <paramref name="clearColor"/> takes precedence over the load op.</param>
    /// <param name="depthOps">Optional depth/stencil load/store ops. <paramref name="clearDepth"/> and <paramref name="clearStencil"/> take precedence over the corresponding load/store ops.</param>
    /// <returns>An RAII render-pass scope; dispose it to end the pass.</returns>
    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        Vector4 clearColor,
        float? clearDepth = null,
        uint? clearStencil = null,
        ReadOnlySpan<AttachmentOps> colorOps = default,
        AttachmentOps? depthOps = null
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
        BeginRenderCore(frameBuffer, clearColorsSpan, clearDepth, clearStencil, colorOps, depthOps);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    /// <summary>
    /// Begins a render pass into the frame buffer without clearing any attachment; only one render
    /// or compute pass may be active at a time.
    /// </summary>
    /// <param name="frameBuffer">The target frame buffer.</param>
    /// <returns>An RAII render-pass scope; dispose it to end the pass.</returns>
    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer
        )
    {
        return BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, null, null);
    }

    /// <summary>
    /// Begins a render pass and writes timestamps at its beginning and/or end.
    /// A null index skips the corresponding write, which allows bracketing a span
    /// of consecutive passes with one timestamp pair (begin on the first pass,
    /// end on the last).
    /// </summary>
    /// <param name="frameBuffer">The target framebuffer.</param>
    /// <param name="clearColors">Attachment clear values.</param>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginningQueryIndex">The slot written when the pass begins, or null to skip.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends, or null to skip.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <param name="colorOps">Optional per-color-attachment load/store ops, indexed by attachment. A clear specified through <paramref name="clearColors"/> takes precedence over the load op.</param>
    /// <param name="depthOps">Optional depth/stencil load/store ops. <paramref name="clearDepth"/> and <paramref name="clearStencil"/> take precedence over the corresponding load/store ops.</param>
    /// <returns>An RAII render-pass scope.</returns>
    public RenderPass BeginRender(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex,
        float? clearDepth = null,
        uint? clearStencil = null,
        ReadOnlySpan<AttachmentOps> colorOps = default,
        AttachmentOps? depthOps = null)
    {
        if (_isRecordingRender)
        {
            throw new InvalidOperationException("Render pass is already recording, try end current Render pass before starting a new one");
        }
        if (_isRecordingCompute)
        {
            throw new InvalidOperationException("Compute pass is already recording, try end current pass before starting a new one");
        }
        if (beginningQueryIndex == null && endQueryIndex == null)
        {
            throw new ArgumentException("At least one of the timestamp query indices must be non-null.");
        }
        if (beginningQueryIndex >= querySet.Count || endQueryIndex >= querySet.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(beginningQueryIndex));
        }

        BeginRenderTimestampCore(frameBuffer, clearColors, querySet, beginningQueryIndex, endQueryIndex, clearDepth, clearStencil, colorOps, depthOps);
        _isRecordingRender = true;
        return new RenderPass(this);
    }

    /// <summary>
    /// Begins a compute pass and returns its RAII recording scope; only one render or compute pass
    /// may be active at a time.
    /// </summary>
    /// <returns>An RAII compute-pass scope; dispose it to end the pass.</returns>
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
    /// Begins a compute pass and writes timestamps at its beginning and/or end.
    /// A null index skips the corresponding write (see
    /// <see cref="BeginRender(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, GPUTimestampQuerySet, uint?, uint?, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>).
    /// </summary>
    /// <param name="querySet">The destination timestamp query set.</param>
    /// <param name="beginningQueryIndex">The slot written when the pass begins, or null to skip.</param>
    /// <param name="endQueryIndex">The slot written when the pass ends, or null to skip.</param>
    /// <returns>An RAII compute-pass scope.</returns>
    public ComputePass BeginCompute(
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex)
    {
        if (_isRecordingRender || _isRecordingCompute)
        {
            throw new InvalidOperationException("Another GPU pass is already recording.");
        }
        if (beginningQueryIndex == null && endQueryIndex == null)
        {
            throw new ArgumentException("At least one of the timestamp query indices must be non-null.");
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


    // Copy commands

    /// <summary>
    /// Records a buffer-to-buffer copy; valid while the command buffer is recording but outside
    /// any render/compute pass.
    /// </summary>
    /// <param name="src">The source buffer.</param>
    /// <param name="dst">The destination buffer.</param>
    /// <param name="srcOffset">The source byte offset.</param>
    /// <param name="dstOffset">The destination byte offset.</param>
    /// <param name="size">The number of bytes to copy.</param>
    public void CopyBuffer(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        AssetUtility.IsTrue(_isRecording, "Command buffer is not recording while CopyBuffer, try start recording by calling GPUCommandBuffer.Begin()");
        CopyBufferCore(src, dst, srcOffset, dstOffset, size);
    }

    /// <summary>Copies <paramref name="size"/> bytes between the buffers at offset 0; see <see cref="CopyBuffer(GPUBuffer, GPUBuffer, ulong, ulong, ulong)"/>.</summary>
    /// <param name="src">The source buffer.</param>
    /// <param name="dst">The destination buffer.</param>
    /// <param name="size">The number of bytes to copy.</param>
    public void CopyBuffer(GPUBuffer src, GPUBuffer dst, ulong size)
    {
        CopyBuffer(src, dst, 0, 0, size);
    }


    /// <summary>Copies the whole source buffer to the destination buffer; see <see cref="CopyBuffer(GPUBuffer, GPUBuffer, ulong, ulong, ulong)"/>.</summary>
    /// <param name="src">The source buffer.</param>
    /// <param name="dst">The destination buffer.</param>
    public void CopyBuffer(GPUBuffer src, GPUBuffer dst)
    {
        CopyBuffer(src, dst, 0, 0, src.Size);
    }

    /// <summary>
    /// Records a buffer-to-texture upload into <paramref name="dst"/>'s image content; valid while
    /// the command buffer is recording but outside any render/compute pass.
    /// </summary>
    /// <param name="src">The source buffer holding the image data.</param>
    /// <param name="dst">The destination texture.</param>
    /// <param name="mipLevel">The destination mip level.</param>
    /// <param name="offset">The source byte offset.</param>
    /// <param name="aspect">The texture aspect to write (All / DepthOnly / StencilOnly).</param>
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



    /// <summary>Backend-specific implementation.</summary>
    protected abstract void BeginCore();
    protected abstract void EndCore();

    protected abstract void BeginRenderCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps);
    protected abstract void BeginRenderTimestampCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps);
    protected abstract void EndRenderCore();

    protected abstract void BeginComputeCore();
    protected abstract void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex);
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
    protected abstract void MultiDrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount);
    protected abstract void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void SetComputePipelineCore(GPUPipeline pipeline);
    protected abstract void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup);
    protected abstract void DispatchComputeCore(uint x, uint y, uint z);
    protected abstract void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset);
    protected abstract unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size);
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
