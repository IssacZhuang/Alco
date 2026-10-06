using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Records native GPU commands while keeping borrowed managed resources alive during calls.</summary>
internal sealed unsafe partial class AlcoGpuCommandBuffer : GPUCommandBuffer
{
    private static readonly Exception ExceptionNoFramebuffer = new("No framebuffer is set before set the graphics pipeline");
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");
    private static readonly Exception ExceptionNoComputePipeline = new("No compute pipeline is set before dispatching");

    #region Properties
    private readonly AlcoGpuDevice _device;

    // recreated every Begin()
    private AlcoGPU.EncoderHandle _encoder;

    // cached state create by internal, released on pass end
    private AlcoGPU.RenderPassHandle _renderPass;
    private AlcoGPU.ComputePassHandle _computePass;

    // cached state from outside
    private UnsafeArray<AlcoGPU.ColorAttachment> _colorAttachmentsCache;
    private AlcoGPU.DepthStencilAttachment? _depthStencilAttachmentCache;
    private AlcoGPU.GraphicsPipelineHandle _graphicsPipeline;
    private AlcoGPU.ComputePipelineHandle _computePipeline;

    // created on end(), consumed on submit
    private AlcoGPU.CommandBufferHandle _buffer;

    // borrowed from descriptor data, owned by this object
    private byte* _nativeName;

    #endregion

    #region Abstract Implementation

    protected override GPUDevice Device { get; }

    /// <summary>Gets whether a finished native command buffer is available for submission.</summary>
    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !_buffer.IsNull;
    }

    protected override void Dispose(bool disposing)
    {
        ExceptionDispatchInfo? failure = null;
        void Cleanup(Action release)
        {
            try
            {
                release();
            }
            catch (Exception error)
            {
                // Capture the native message before another cleanup call overwrites it.
                failure ??= ExceptionDispatchInfo.Capture(error);
            }
        }

        bool abandon = !disposing || _device is null || !_device.IsNativeAlive;
        Cleanup(() => TryFinishCurrentRenderPass(abandon));
        Cleanup(() => TryFinishCurrentComputePass(abandon));
        Cleanup(ReleaseCommandBuffer);
        Cleanup(ReleaseCommandEncoder);

        byte* name = _nativeName;
        _nativeName = null;
        InteropUtility.Free(name);
        _colorAttachmentsCache.Dispose();
        _depthStencilAttachmentCache = null;
        _graphicsPipeline = AlcoGPU.GraphicsPipelineHandle.Null;
        _computePipeline = AlcoGPU.ComputePipelineHandle.Null;
        _isRecording = _isRecordingRender = _isRecordingCompute = false;
        failure?.Throw();
    }

    /// <summary>Begins the native command encoder used for recording.</summary>
    protected unsafe override void BeginCore()
    {
        try
        {
            ReleaseCommandBuffer();
            ReleaseCommandEncoder();

            AlcoGpuNative.EncoderCreate(_device.Native, _nativeName, out _encoder);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <summary>Ends the native command encoder and finishes the command buffer.</summary>
    protected unsafe override void EndCore()
    {
        try
        {
            try
            {
                TryFinishCurrentComputePass();
                TryFinishCurrentRenderPass();

                // Finish consumes the encoder on success and validation failure alike,
                // so the mirror handle is cleared before the call.
                AlcoGPU.EncoderHandle encoder = _encoder;
                _encoder = AlcoGPU.EncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(encoder, out _buffer);
            }
            finally
            {
                _graphicsPipeline = AlcoGPU.GraphicsPipelineHandle.Null;
                _computePipeline = AlcoGPU.ComputePipelineHandle.Null;
                _depthStencilAttachmentCache = null;
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    protected override void BeginRenderCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        BeginRenderInternal(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps, timestampWrites: null);
    }

    /// <inheritdoc />
    protected override void BeginRenderTimestampCore(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        AlcoGPU.TimestampWrites timestampWrites = new()
        {
            QuerySet = ((AlcoGpuTimestampQuerySet)querySet).Native,
            BeginningIndex = ToTimestampIndex(beginningQueryIndex),
            EndIndex = ToTimestampIndex(endQueryIndex),
        };
        try
        {
            BeginRenderInternal(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps, &timestampWrites);
        }
        finally
        {
            GC.KeepAlive(querySet);
            GC.KeepAlive(frameBuffer);
            GC.KeepAlive(this);
        }
    }

    private void BeginRenderInternal(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps,
        AlcoGPU.TimestampWrites* timestampWrites)
    {
        try
        {
            AlcoGpuFrameBufferBase nativeFrameBuffer = (AlcoGpuFrameBufferBase)frameBuffer;

            TryFinishCurrentRenderPass();
            TryFinishCurrentComputePass();

            AlcoGPU.RenderPassDesc tmpDescriptor = nativeFrameBuffer.Native;
            _colorAttachmentsCache.EnsureCapacity((int)tmpDescriptor.ColorAttachmentCount);

            // Setup color attachments with clear values
            for (uint i = 0; i < tmpDescriptor.ColorAttachmentCount; i++)
            {
                _colorAttachmentsCache[(int)i] = tmpDescriptor.ColorAttachments[i];
            }

            uint clearedColorMask = 0;
            for (int i = 0; i < clearColors.Length; i++)
            {
                ClearColorData clearColor = clearColors[i];
                uint index = clearColor.Index;
                if (index >= tmpDescriptor.ColorAttachmentCount)
                {
                    continue;
                }

                clearedColorMask |= 1u << (int)index;
                _colorAttachmentsCache[(int)index].LoadOp = AlcoGpuUtility.LoadOpToAbi(AttachmentLoadOp.Clear);
                _colorAttachmentsCache[(int)index].StoreOp = 0; // store
                Vector4 color = clearColor.Color;
                _colorAttachmentsCache[(int)index].ClearColor[0] = color.X;
                _colorAttachmentsCache[(int)index].ClearColor[1] = color.Y;
                _colorAttachmentsCache[(int)index].ClearColor[2] = color.Z;
                _colorAttachmentsCache[(int)index].ClearColor[3] = color.W;
            }

            // Apply explicit load/store ops after the clear handling: a clear specified through
            // clearColors implies LoadOp.Clear and takes precedence over the load op.
            int colorOpsCount = Math.Min(colorOps.Length, (int)tmpDescriptor.ColorAttachmentCount);
            for (int i = 0; i < colorOpsCount; i++)
            {
                AttachmentOps ops = colorOps[i];
                _colorAttachmentsCache[i].StoreOp = AlcoGpuUtility.StoreOpToAbi(ops.StoreOp);
                if ((clearedColorMask & (1u << i)) == 0)
                {
                    _colorAttachmentsCache[i].LoadOp = AlcoGpuUtility.LoadOpToAbi(ops.LoadOp);
                }
            }

            // Setup depth stencil attachment with clear values
            if (tmpDescriptor.DepthStencil != null)
            {
                AlcoGPU.DepthStencilAttachment attachment = *tmpDescriptor.DepthStencil;
                DepthAttachmentInfo depthInfo = ((AlcoGpuAttachmentLayout)frameBuffer.AttachmentLayout).DepthInfo!.Value;
                bool depthReadOnly = attachment.DepthLoadOp == AlcoGPU.None;
                bool stencilReadOnly = attachment.StencilLoadOp == AlcoGPU.None;

                // Missing aspects are not user-declared read-only channels. Ignore their
                // clear values and never synthesize operations for them from depthOps.
                if (depthInfo.HasDepth && depthReadOnly && (clearDepth.HasValue || depthOps.HasValue))
                {
                    throw new InvalidOperationException(
                        "The pass's depth attachment is read-only (the attachment layout declares " +
                        "DepthAttachment.ReadOnly): it cannot be cleared or have explicit load/store ops.");
                }
                if (depthInfo.HasStencil && stencilReadOnly && (clearStencil.HasValue || depthOps.HasValue))
                {
                    throw new InvalidOperationException(
                        "The pass's stencil attachment is read-only (the attachment layout declares " +
                        "DepthAttachment.ReadOnly): it cannot be cleared or have explicit load/store ops.");
                }

                if (depthInfo.HasDepth)
                {
                    if (clearDepth.HasValue)
                    {
                        attachment.DepthLoadOp = AlcoGpuUtility.LoadOpToAbi(AttachmentLoadOp.Clear);
                        attachment.DepthStoreOp = 0; // store
                        attachment.DepthClear = clearDepth.Value;
                    }
                    else if (depthOps.HasValue)
                    {
                        attachment.DepthLoadOp = AlcoGpuUtility.LoadOpToAbi(depthOps.Value.LoadOp);
                        attachment.DepthStoreOp = AlcoGpuUtility.StoreOpToAbi(depthOps.Value.StoreOp);
                    }
                }

                if (depthInfo.HasStencil)
                {
                    if (clearStencil.HasValue)
                    {
                        attachment.StencilLoadOp = AlcoGpuUtility.LoadOpToAbi(AttachmentLoadOp.Clear);
                        attachment.StencilStoreOp = 0; // store
                        attachment.StencilClear = clearStencil.Value;
                    }
                    else if (depthOps.HasValue)
                    {
                        attachment.StencilLoadOp = AlcoGpuUtility.LoadOpToAbi(depthOps.Value.LoadOp);
                        attachment.StencilStoreOp = AlcoGpuUtility.StoreOpToAbi(depthOps.Value.StoreOp);
                    }
                }

                _depthStencilAttachmentCache = attachment;
            }
            else
            {
                _depthStencilAttachmentCache = null;
            }

            // Start the render pass
            AlcoGPU.RenderPassDesc renderPassDesc = new()
            {
                ColorAttachments = _colorAttachmentsCache.Ptr,
                ColorAttachmentCount = tmpDescriptor.ColorAttachmentCount,
                DepthStencil = null,
                TimestampWrites = timestampWrites,
            };

            AlcoGPU.DepthStencilAttachment depthStencilAttachment = _depthStencilAttachmentCache.GetValueOrDefault();
            if (_depthStencilAttachmentCache.HasValue)
            {
                renderPassDesc.DepthStencil = &depthStencilAttachment;
            }

            AlcoGpuNative.RenderPassBegin(_encoder, in renderPassDesc, out _renderPass);
            // The framebuffer owns the attachment wrappers and unmanaged descriptor storage.
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(frameBuffer);
        }
    }

    protected override void EndRenderCore()
    {
        TryFinishCurrentRenderPass();
    }

    protected override void BeginComputeCore()
    {
        try
        {
            TryFinishCurrentRenderPass();
            TryFinishCurrentComputePass();

            AlcoGpuNative.ComputePassBegin(_encoder, null, out _computePass);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex)
    {
        try
        {
            TryFinishCurrentRenderPass();
            TryFinishCurrentComputePass();

            AlcoGPU.TimestampWrites timestampWrites = new()
            {
                QuerySet = ((AlcoGpuTimestampQuerySet)querySet).Native,
                BeginningIndex = ToTimestampIndex(beginningQueryIndex),
                EndIndex = ToTimestampIndex(endQueryIndex),
            };

            AlcoGpuNative.ComputePassBegin(_encoder, &timestampWrites, out _computePass);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(querySet);
        }
    }

    // An absent timestamp write is marked with the all-ones sentinel (mirrors
    // WGPU_QUERY_SET_INDEX_UNDEFINED).
    private static uint ToTimestampIndex(uint? queryIndex)
    {
        return queryIndex ?? AlcoGPU.None;
    }

    protected override void EndComputeCore()
    {
        TryFinishCurrentComputePass();
    }

    /// <inheritdoc />
    protected override void WriteTimestampInsidePassCore(
        GPUTimestampQuerySet querySet,
        uint queryIndex)
    {
        try
        {
            if (!_computePass.IsNull)
            {
                AlcoGpuNative.ComputePassWriteTimestamp(_computePass, ((AlcoGpuTimestampQuerySet)querySet).Native, queryIndex);
            }
            else if (!_renderPass.IsNull)
            {
                AlcoGpuNative.RenderPassWriteTimestamp(_renderPass, ((AlcoGpuTimestampQuerySet)querySet).Native, queryIndex);
            }
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(querySet);
        }
    }

    /// <inheritdoc />
    protected override void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset)
    {
        try
        {
            AlcoGpuNative.ResolveQuerySet(_encoder,
                ((AlcoGpuTimestampQuerySet)querySet).Native,
                firstQuery,
                queryCount,
                ((AlcoGpuBuffer)destination).Native,
                destinationOffset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(querySet);
            GC.KeepAlive(destination);
        }
    }

    /// <inheritdoc />
    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
        try
        {
            AlcoGpuNative.RenderPassSetScissorRect(_renderPass, x, y, width, height);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        try
        {
            _graphicsPipeline = ((AlcoGpuGraphicsPipeline)pipeline).Native;
            AlcoGpuNative.RenderPassSetPipeline(_renderPass, _graphicsPipeline);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(pipeline);
        }
    }

    /// <inheritdoc />
    protected override void SetStencilReferenceCore(uint value)
    {
        try
        {
            AlcoGpuNative.RenderPassSetStencilReference(_renderPass, value);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassSetBindGroup(_renderPass, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(resourceGroup);
        }
    }

    /// <inheritdoc />
    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassSetVertexBuffer(_renderPass, slot, ((AlcoGpuBuffer)buffer).Native, offset, size);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(buffer);
        }
    }

    /// <inheritdoc />
    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassSetIndexBuffer(_renderPass, ((AlcoGpuBuffer)buffer).Native, (uint)format, offset, size);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(buffer);
        }
    }

    /// <inheritdoc />
    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassDraw(_renderPass, vertexCount, instanceCount, firstVertex, firstInstance);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassDrawIndexed(_renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassDrawIndirect(_renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassDrawIndexedIndirect(_renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override void MultiDrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.RenderPassMultiDrawIndexedIndirect(_renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset, drawCount);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        try
        {
            AlcoGpuNative.RenderPassSetImmediates(_renderPass, bufferOffset, data, size);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override unsafe void PushComputeConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        try
        {
            AlcoGpuNative.ComputePassSetImmediates(_computePass, bufferOffset, data, size);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        try
        {
            _computePipeline = ((AlcoGpuComputePipeline)pipeline).Native;
            AlcoGpuNative.ComputePassSetPipeline(_computePass, _computePipeline);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(pipeline);
        }
    }

    /// <inheritdoc />
    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        try
        {
            ValidateComputePipeline();

            AlcoGpuNative.ComputePassSetBindGroup(_computePass, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(resourceGroup);
        }
    }

    /// <inheritdoc />
    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        try
        {
            ValidateComputePipeline();

            AlcoGpuNative.ComputePassDispatchWorkgroups(_computePass, x, y, z);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        try
        {
            ValidateComputePipeline();

            AlcoGpuNative.ComputePassDispatchWorkgroupsIndirect(_computePass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        try
        {
            AlcoGpuNative.CopyBufferToBuffer(_encoder,
                ((AlcoGpuBuffer)src).Native, srcOffset,
                ((AlcoGpuBuffer)dst).Native, dstOffset,
                size);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(src);
            GC.KeepAlive(dst);
        }
    }

    /// <inheritdoc />
    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect)
    {
        try
        {
            AlcoGpuTexture nativeDst = (AlcoGpuTexture)dst;

            AlcoGPU.CopyLayout layout = AlcoGpuUtility.GetTextureDataLayout(nativeDst.PixelFormat, nativeDst.Width, nativeDst.Height);
            layout.Offset = offset;

            AlcoGPU.Extent3D extent = new()
            {
                Width = nativeDst.Width,
                Height = nativeDst.Height,
                DepthOrArrayLayers = nativeDst.Depth,
            };

            AlcoGpuNative.CopyBufferToTexture(_encoder,
                ((AlcoGpuBuffer)src).Native, in layout,
                nativeDst.Native, mipLevel, (uint)aspect, extent);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(src);
            GC.KeepAlive(dst);
        }
    }

    /// <inheritdoc />
    protected override void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect)
    {
        try
        {
            AlcoGpuTextureBase nativeSrc = (AlcoGpuTextureBase)src;
            AlcoGpuTextureBase nativeDst = (AlcoGpuTextureBase)dst;

            // Copy the full mip extent (adjusted for the source mip level).
            uint mipWidth = nativeSrc.GetMipWidth(srcMipLevel);
            uint mipHeight = nativeSrc.GetMipHeight(srcMipLevel);
            AlcoGPU.Extent3D copySize = new()
            {
                Width = mipWidth,
                Height = mipHeight,
                DepthOrArrayLayers = 1,
            };

            AlcoGpuNative.CopyTextureToTexture(_encoder,
                nativeSrc.Native, srcMipLevel,
                nativeDst.Native, dstMipLevel,
                (uint)aspect, copySize);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(src);
            GC.KeepAlive(dst);
        }
    }

    /// <inheritdoc />
    protected override void ExecuteBundleCore(GPURenderBundle bundle)
    {
        try
        {
            AlcoGPU.RenderBundleHandle native = ((AlcoGpuRenderBundle)bundle).Native;
            AlcoGpuNative.RenderPassExecuteBundles(_renderPass, &native, 1);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(bundle);
        }
    }

    /// <inheritdoc />
    protected override void ExecuteBundleCore(ReadOnlySpan<GPURenderBundle> bundle)
    {
        try
        {
            Span<AlcoGPU.RenderBundleHandle> nativeBundleStorage = bundle.Length <= 64
                ? stackalloc AlcoGPU.RenderBundleHandle[bundle.Length] : new AlcoGPU.RenderBundleHandle[bundle.Length];
            for (int i = 0; i < bundle.Length; i++)
            {
                nativeBundleStorage[i] = ((AlcoGpuRenderBundle)bundle[i]).Native;
            }

            fixed (AlcoGPU.RenderBundleHandle* nativeBundles = nativeBundleStorage)
            {
                AlcoGpuNative.RenderPassExecuteBundles(_renderPass, nativeBundles, (uint)bundle.Length);
            }
            // The native handle array does not retain the managed bundle wrappers.
            for (int i = 0; i < bundle.Length; i++)
            {
            }
        }
        finally
        {
            for (int i = 0; i < bundle.Length; i++) { GC.KeepAlive(bundle[i]); }
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal AlcoGPU.CommandBufferHandle TakeBuffer()
    {
        AlcoGPU.CommandBufferHandle buffer = _buffer;
        _buffer = AlcoGPU.CommandBufferHandle.Null;
        return buffer;
    }

    internal unsafe AlcoGpuCommandBuffer(AlcoGpuDevice device, in CommandBufferDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        _device = device;

        _buffer = AlcoGPU.CommandBufferHandle.Null;
        _encoder = AlcoGPU.EncoderHandle.Null;

        _renderPass = AlcoGPU.RenderPassHandle.Null;
        _computePass = AlcoGPU.ComputePassHandle.Null;

        try
        {
            ReadOnlySpan<byte> nameSpan = Name.Utf8Z();
            fixed (byte* ptr = nameSpan)
            {
                _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length);
                InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
            }

            _colorAttachmentsCache = new UnsafeArray<AlcoGPU.ColorAttachment>(8);
        }
        catch
        {
            Destroy(false);
            throw;
        }
    }

    private void ReleaseCommandEncoder()
    {
        try
        {
            if (!_encoder.IsNull)
            {
                // Destroy consumes the handle; a stale handle is worthless either way.
                AlcoGPU.EncoderHandle encoder = _encoder;
                _encoder = AlcoGPU.EncoderHandle.Null;
                AlcoGpuNative.EncoderDestroy(encoder);
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    private void ReleaseCommandBuffer()
    {
        try
        {
            if (!_buffer.IsNull)
            {
                AlcoGPU.CommandBufferHandle buffer = _buffer;
                _buffer = AlcoGPU.CommandBufferHandle.Null;
                AlcoGpuNative.CommandBufferDestroy(buffer);
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentRenderPass(bool abandon = false)
    {
        try
        {
            if (!_renderPass.IsNull)
            {
                // End consumes the pass handle on success and failure alike.
                AlcoGPU.RenderPassHandle pass = _renderPass;
                _renderPass = AlcoGPU.RenderPassHandle.Null;
                if (abandon)
                {
                    AlcoGpuNative.RenderPassRelease(pass);
                }
                else
                {
                    AlcoGpuNative.RenderPassEnd(pass);
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentComputePass(bool abandon = false)
    {
        try
        {
            if (!_computePass.IsNull)
            {
                // End consumes the pass handle on success and failure alike.
                AlcoGPU.ComputePassHandle pass = _computePass;
                _computePass = AlcoGPU.ComputePassHandle.Null;
                if (abandon)
                {
                    AlcoGpuNative.ComputePassRelease(pass);
                }
                else
                {
                    AlcoGpuNative.ComputePassEnd(pass);
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    //debug validate

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateGraphicsPipeline()
    {
        if (_graphicsPipeline.IsNull)
        {
            throw ExceptionNoGraphicsPipeline;
        }
    }

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateComputePipeline()
    {
        if (_computePipeline.IsNull)
        {
            throw ExceptionNoComputePipeline;
        }
    }

    #endregion
}
