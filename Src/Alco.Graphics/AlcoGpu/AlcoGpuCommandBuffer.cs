using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe partial class AlcoGpuCommandBuffer : GPUCommandBuffer
{
    private static readonly Exception ExceptionNoFramebuffer = new("No framebuffer is set before set the graphics pipeline");
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");
    private static readonly Exception ExceptionNoComputePipeline = new("No compute pipeline is set before dispatching");

    #region Properties
    private readonly AlcoGpuDevice _device;

    // recreated every Begin()
    private AlcoHandle _encoder;

    // cached state create by internal, released on pass end
    private AlcoHandle _renderPass;
    private AlcoHandle _computePass;

    // cached state from outside
    private UnsafeArray<AlcoColorAttachment> _colorAttachmentsCache;
    private AlcoDepthStencilAttachment? _depthStencilAttachmentCache;
    private AlcoHandle _graphicsPipeline;
    private AlcoHandle _computePipeline;

    // created on end(), consumed on submit
    private AlcoHandle _buffer;

    // borrowed from descriptor data, owned by this object
    private readonly byte* _nativeName;

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

        Cleanup(TryFinishCurrentRenderPass);
        Cleanup(TryFinishCurrentComputePass);
        Cleanup(ReleaseCommandBuffer);
        Cleanup(ReleaseCommandEncoder);

        InteropUtility.Free(_nativeName);
        _colorAttachmentsCache.Dispose();
        _depthStencilAttachmentCache = null;
        _graphicsPipeline = AlcoHandle.Null;
        _computePipeline = AlcoHandle.Null;
        _isRecording = _isRecordingRender = _isRecordingCompute = false;
        failure?.Throw();
    }

    /// <summary>Begins the native command encoder used for recording.</summary>
    protected unsafe override void BeginCore()
    {
        ReleaseCommandBuffer();
        ReleaseCommandEncoder();

        uint status = AlcoGpuNative.EncoderCreate(_device.Native, _nativeName, out _encoder);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    /// <summary>Ends the native command encoder and finishes the command buffer.</summary>
    protected unsafe override void EndCore()
    {
        try
        {
            TryFinishCurrentComputePass();
            TryFinishCurrentRenderPass();

            uint status = AlcoGpuNative.EncoderFinish(_device.Native, _encoder, out _buffer);
            // Finish consumes the encoder on success and validation failure alike.
            _encoder = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
        finally
        {
            _graphicsPipeline = AlcoHandle.Null;
            _computePipeline = AlcoHandle.Null;
            _depthStencilAttachmentCache = null;
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
        AlcoTimestampWrites timestampWrites = new()
        {
            QuerySet = ((AlcoGpuTimestampQuerySet)querySet).Native,
            BeginningIndex = ToTimestampIndex(beginningQueryIndex),
            EndIndex = ToTimestampIndex(endQueryIndex),
        };
        BeginRenderInternal(frameBuffer, clearColors, clearDepth, clearStencil, colorOps, depthOps, &timestampWrites);
    }

    private void BeginRenderInternal(
        GPUFrameBuffer frameBuffer,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps,
        AlcoTimestampWrites* timestampWrites)
    {
        AlcoGpuFrameBufferBase nativeFrameBuffer = (AlcoGpuFrameBufferBase)frameBuffer;

        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        AlcoRenderPassDesc tmpDescriptor = nativeFrameBuffer.Native;
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
            AlcoDepthStencilAttachment attachment = *tmpDescriptor.DepthStencil;
            AlcoDepthAttachmentInfo depthInfo = ((AlcoGpuAttachmentLayout)frameBuffer.AttachmentLayout).DepthInfo!.Value;
            bool depthReadOnly = attachment.DepthLoadOp == AlcoGpuAbi.AlcoNone;
            bool stencilReadOnly = attachment.StencilLoadOp == AlcoGpuAbi.AlcoNone;

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
        AlcoRenderPassDesc renderPassDesc = new()
        {
            ColorAttachments = _colorAttachmentsCache.Ptr,
            ColorAttachmentCount = tmpDescriptor.ColorAttachmentCount,
            DepthStencil = null,
            TimestampWrites = timestampWrites,
        };

        if (_depthStencilAttachmentCache.HasValue)
        {
            // copy to keep the pointer alive across the native call
            AlcoDepthStencilAttachment depthStencilAttachment = _depthStencilAttachmentCache.Value;
            renderPassDesc.DepthStencil = &depthStencilAttachment;
        }

        uint status = AlcoGpuNative.RenderPassBegin(_device.Native, _encoder, in renderPassDesc, out _renderPass);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void EndRenderCore()
    {
        if (!_renderPass.IsNull)
        {
            uint status = AlcoGpuNative.RenderPassEnd(_device.Native, _renderPass);
            _renderPass = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected override void BeginComputeCore()
    {
        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        uint status = AlcoGpuNative.ComputePassBegin(_device.Native, _encoder, null, out _computePass);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint? beginningQueryIndex,
        uint? endQueryIndex)
    {
        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        AlcoTimestampWrites timestampWrites = new()
        {
            QuerySet = ((AlcoGpuTimestampQuerySet)querySet).Native,
            BeginningIndex = ToTimestampIndex(beginningQueryIndex),
            EndIndex = ToTimestampIndex(endQueryIndex),
        };

        uint status = AlcoGpuNative.ComputePassBegin(_device.Native, _encoder, &timestampWrites, out _computePass);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    // An absent timestamp write is marked with the all-ones sentinel (mirrors
    // WGPU_QUERY_SET_INDEX_UNDEFINED).
    private static uint ToTimestampIndex(uint? queryIndex)
    {
        return queryIndex ?? AlcoGpuAbi.AlcoNone;
    }

    protected override void EndComputeCore()
    {
        if (!_computePass.IsNull)
        {
            uint status = AlcoGpuNative.ComputePassEnd(_device.Native, _computePass);
            _computePass = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected override void WriteTimestampInsidePassCore(
        GPUTimestampQuerySet querySet,
        uint queryIndex)
    {
        if (!_computePass.IsNull)
        {
            uint status = AlcoGpuNative.ComputePassWriteTimestamp(
                _device.Native, _computePass, ((AlcoGpuTimestampQuerySet)querySet).Native, queryIndex);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
        else if (!_renderPass.IsNull)
        {
            uint status = AlcoGpuNative.RenderPassWriteTimestamp(
                _device.Native, _renderPass, ((AlcoGpuTimestampQuerySet)querySet).Native, queryIndex);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected override void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset)
    {
        uint status = AlcoGpuNative.ResolveQuerySet(
            _device.Native,
            _encoder,
            ((AlcoGpuTimestampQuerySet)querySet).Native,
            firstQuery,
            queryCount,
            ((AlcoGpuBuffer)destination).Native,
            destinationOffset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
        uint status = AlcoGpuNative.RenderPassSetScissorRect(_device.Native, _renderPass, x, y, width, height);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((AlcoGpuGraphicsPipeline)pipeline).Native;
        uint status = AlcoGpuNative.RenderPassSetPipeline(_device.Native, _renderPass, _graphicsPipeline);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetStencilReferenceCore(uint value)
    {
        uint status = AlcoGpuNative.RenderPassSetStencilReference(_device.Native, _renderPass, value);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassSetBindGroup(
            _device.Native, _renderPass, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassSetVertexBuffer(
            _device.Native, _renderPass, slot, ((AlcoGpuBuffer)buffer).Native, offset, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassSetIndexBuffer(
            _device.Native, _renderPass, ((AlcoGpuBuffer)buffer).Native, (uint)format, offset, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassDraw(_device.Native, _renderPass, vertexCount, instanceCount, firstVertex, firstInstance);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassDrawIndexed(_device.Native, _renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassDrawIndirect(
            _device.Native, _renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassDrawIndexedIndirect(
            _device.Native, _renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void MultiDrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.RenderPassMultiDrawIndexedIndirect(
            _device.Native, _renderPass, ((AlcoGpuBuffer)indirectBuffer).Native, offset, drawCount);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        uint status = AlcoGpuNative.RenderPassSetImmediates(_device.Native, _renderPass, bufferOffset, data, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override unsafe void PushComputeConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        uint status = AlcoGpuNative.ComputePassSetImmediates(_device.Native, _computePass, bufferOffset, data, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        _computePipeline = ((AlcoGpuComputePipeline)pipeline).Native;
        uint status = AlcoGpuNative.ComputePassSetPipeline(_device.Native, _computePass, _computePipeline);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateComputePipeline();

        uint status = AlcoGpuNative.ComputePassSetBindGroup(
            _device.Native, _computePass, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        ValidateComputePipeline();

        uint status = AlcoGpuNative.ComputePassDispatchWorkgroups(_device.Native, _computePass, x, y, z);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateComputePipeline();

        uint status = AlcoGpuNative.ComputePassDispatchWorkgroupsIndirect(
            _device.Native, _computePass, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        uint status = AlcoGpuNative.CopyBufferToBuffer(
            _device.Native, _encoder,
            ((AlcoGpuBuffer)src).Native, srcOffset,
            ((AlcoGpuBuffer)dst).Native, dstOffset,
            size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect)
    {
        AlcoGpuTexture nativeDst = (AlcoGpuTexture)dst;

        AlcoCopyLayout layout = AlcoGpuUtility.GetTextureDataLayout(nativeDst.PixelFormat, nativeDst.Width, nativeDst.Height);
        layout.Offset = offset;

        AlcoExtent3D extent = new()
        {
            Width = nativeDst.Width,
            Height = nativeDst.Height,
            DepthOrArrayLayers = nativeDst.Depth,
        };

        uint status = AlcoGpuNative.CopyBufferToTexture(
            _device.Native, _encoder,
            ((AlcoGpuBuffer)src).Native, in layout,
            nativeDst.Native, mipLevel, (uint)aspect, extent);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect)
    {
        AlcoGpuTextureBase nativeSrc = (AlcoGpuTextureBase)src;
        AlcoGpuTextureBase nativeDst = (AlcoGpuTextureBase)dst;

        // Copy the full mip extent (adjusted for the source mip level).
        uint mipWidth = nativeSrc.GetMipWidth(srcMipLevel);
        uint mipHeight = nativeSrc.GetMipHeight(srcMipLevel);
        AlcoExtent3D copySize = new()
        {
            Width = mipWidth,
            Height = mipHeight,
            DepthOrArrayLayers = 1,
        };

        uint status = AlcoGpuNative.CopyTextureToTexture(
            _device.Native, _encoder,
            nativeSrc.Native, srcMipLevel,
            nativeDst.Native, dstMipLevel,
            (uint)aspect, copySize);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void ExecuteBundleCore(GPURenderBundle bundle)
    {
        AlcoHandle native = ((AlcoGpuRenderBundle)bundle).Native;
        uint status = AlcoGpuNative.RenderPassExecuteBundles(_device.Native, _renderPass, &native, 1);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void ExecuteBundleCore(ReadOnlySpan<GPURenderBundle> bundle)
    {
        AlcoHandle* nativeBundles = stackalloc AlcoHandle[bundle.Length];
        for (int i = 0; i < bundle.Length; i++)
        {
            nativeBundles[i] = ((AlcoGpuRenderBundle)bundle[i]).Native;
        }

        uint status = AlcoGpuNative.RenderPassExecuteBundles(_device.Native, _renderPass, nativeBundles, (uint)bundle.Length);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    #endregion

    #region AlcoGpu Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal AlcoHandle TakeBuffer()
    {
        AlcoHandle buffer = _buffer;
        _buffer = AlcoHandle.Null;
        return buffer;
    }

    internal unsafe AlcoGpuCommandBuffer(AlcoGpuDevice device, in CommandBufferDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        _device = device;

        _buffer = AlcoHandle.Null;
        _encoder = AlcoHandle.Null;

        _renderPass = AlcoHandle.Null;
        _computePass = AlcoHandle.Null;

        ReadOnlySpan<byte> nameSpan = Name.Utf8Z();
        fixed (byte* ptr = nameSpan)
        {
            _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length);
            InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
        }

        _colorAttachmentsCache = new UnsafeArray<AlcoColorAttachment>(8);
    }

    private void ReleaseCommandEncoder()
    {
        if (!_encoder.IsNull)
        {
            uint status = AlcoGpuNative.EncoderDestroy(_device.Native, _encoder);
            _encoder = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    private void ReleaseCommandBuffer()
    {
        if (!_buffer.IsNull)
        {
            uint status = AlcoGpuNative.CommandBufferDestroy(_device.Native, _buffer);
            _buffer = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentRenderPass()
    {
        if (!_renderPass.IsNull)
        {
            uint status = AlcoGpuNative.RenderPassEnd(_device.Native, _renderPass);
            _renderPass = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentComputePass()
    {
        if (!_computePass.IsNull)
        {
            uint status = AlcoGpuNative.ComputePassEnd(_device.Native, _computePass);
            _computePass = AlcoHandle.Null;
            AlcoGpuMarshal.ThrowIfFailed(status);
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
