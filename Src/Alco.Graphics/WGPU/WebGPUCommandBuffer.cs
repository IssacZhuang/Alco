using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed unsafe partial class WebGPUCommandBuffer : GPUCommandBuffer
{
    private static readonly Exception ExceptionNoFramebuffer = new("No framebuffer is set before set the graphics pipeline");
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");
    private static readonly Exception ExceptionNoComputePipeline = new("No compute pipeline is set before dispatching");


    #region Properties
    private readonly WGPUDevice _nativeDevice;

    // used every frame
    private WGPUCommandEncoder _encoder;

    // cached state create by internal, release on end()
    private WGPURenderPassEncoder _renderPass;
    private WGPUComputePassEncoder _computePass;

    // cached state from outside
    private UnsafeArray<WGPURenderPassColorAttachment> _colorAttachmentsCache;
    private WGPURenderPassDepthStencilAttachment? _depthStencilAttachmentCache;

    private WGPURenderPipeline _graphicsPipeline;
    private WGPUComputePipeline _computePipeline;

    // create on end(), can be reused
    private WGPUCommandBuffer _buffer;

    //release on dispose
    private readonly byte* _nativeName;
    private readonly WGPUStringView _nativeNameView;


    #endregion

    #region Abstract Implementation

    protected override GPUDevice Device { get; }

    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer != WGPUCommandBuffer.Null;
    }

    protected override void Dispose(bool disposing)
    {
        // the buffer will not be released if the End() is not called
        // do check here to prevent memory leak

        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        ReleaseCommandBuffer();
        ReleaseCommandEncoder();

        InteropUtility.Free(_nativeName);
        _colorAttachmentsCache.Dispose();
    }

    // begin the encoder
    protected unsafe override void BeginCore()
    {
        WGPUCommandEncoderDescriptor descriptor = new WGPUCommandEncoderDescriptor
        {
            label = _nativeNameView
        };
        _encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, &descriptor);

        // clear buffer
        if (_buffer != WGPUCommandBuffer.Null)
        {
            //only happens when the buffer is not submitted
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }
    }

    // end the encoder
    protected unsafe override void EndCore()
    {
        TryFinishCurrentComputePass();
        TryFinishCurrentRenderPass();

        WGPUCommandBufferDescriptor descriptor = new WGPUCommandBufferDescriptor
        {
            label = _nativeNameView
        };

        _buffer = wgpuCommandEncoderFinish(_encoder, &descriptor);

        _graphicsPipeline = WGPURenderPipeline.Null;
        _computePipeline = WGPUComputePipeline.Null;

        _depthStencilAttachmentCache = null;

        // release encoder
        wgpuCommandEncoderRelease(_encoder);
        _encoder = WGPUCommandEncoder.Null;
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
        uint beginningQueryIndex,
        uint endQueryIndex,
        float? clearDepth,
        uint? clearStencil,
        ReadOnlySpan<AttachmentOps> colorOps,
        AttachmentOps? depthOps)
    {
        WGPUPassTimestampWrites timestampWrites = new()
        {
            querySet = ((WebGPUTimestampQuerySet)querySet).Native,
            beginningOfPassWriteIndex = beginningQueryIndex,
            endOfPassWriteIndex = endQueryIndex,
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
        WGPUPassTimestampWrites* timestampWrites)
    {
        WebGPUFrameBufferBase nativeFrameBuffer = (WebGPUFrameBufferBase)frameBuffer;

        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        WGPURenderPassDescriptor tmpDescriptor = nativeFrameBuffer.Native;
        _colorAttachmentsCache.EnsureCapacity((int)tmpDescriptor.colorAttachmentCount);

        // Setup color attachments with clear values
        for (uint i = 0; i < tmpDescriptor.colorAttachmentCount; i++)
        {
            _colorAttachmentsCache[i] = tmpDescriptor.colorAttachments[i];
        }

        uint clearedColorMask = 0;
        for (int i = 0; i < clearColors.Length; i++)
        {
            ClearColorData clearColor = clearColors[i];
            uint index = clearColor.Index;
            if (index >= tmpDescriptor.colorAttachmentCount)
            {
                continue;
            }

            clearedColorMask |= 1u << (int)index;
            _colorAttachmentsCache[index].loadOp = WGPULoadOp.Clear;
            _colorAttachmentsCache[index].storeOp = WGPUStoreOp.Store;
            _colorAttachmentsCache[index].clearValue = new WGPUColor
            {
                r = clearColor.Color.X,
                g = clearColor.Color.Y,
                b = clearColor.Color.Z,
                a = clearColor.Color.W
            };
        }

        // Apply explicit load/store ops after the clear handling: a clear specified through
        // clearColors implies LoadOp.Clear and takes precedence over the load op.
        int colorOpsCount = Math.Min(colorOps.Length, (int)tmpDescriptor.colorAttachmentCount);
        for (int i = 0; i < colorOpsCount; i++)
        {
            AttachmentOps ops = colorOps[i];
            _colorAttachmentsCache[i].storeOp = ToWebGPU(ops.StoreOp);
            if ((clearedColorMask & (1u << i)) == 0)
            {
                _colorAttachmentsCache[i].loadOp = ToWebGPU(ops.LoadOp);
            }
        }

        // Setup depth stencil attachment with clear values
        if (tmpDescriptor.depthStencilAttachment != null)
        {
            WGPURenderPassDepthStencilAttachment attachment = *tmpDescriptor.depthStencilAttachment;

            if (clearDepth.HasValue)
            {
                attachment.depthLoadOp = WGPULoadOp.Clear;
                attachment.depthStoreOp = WGPUStoreOp.Store;
                attachment.depthClearValue = clearDepth.Value;
            }
            else if (depthOps.HasValue)
            {
                attachment.depthLoadOp = ToWebGPU(depthOps.Value.LoadOp);
                attachment.depthStoreOp = ToWebGPU(depthOps.Value.StoreOp);
            }

            if (clearStencil.HasValue)
            {
                attachment.stencilLoadOp = WGPULoadOp.Clear;
                attachment.stencilStoreOp = WGPUStoreOp.Store;
                attachment.stencilClearValue = clearStencil.Value;
            }
            else if (depthOps.HasValue)
            {
                attachment.stencilLoadOp = ToWebGPU(depthOps.Value.LoadOp);
                attachment.stencilStoreOp = ToWebGPU(depthOps.Value.StoreOp);
            }

            _depthStencilAttachmentCache = attachment;
        }
        else
        {
            _depthStencilAttachmentCache = null;
        }

        // Start the render pass
        WGPURenderPassDescriptor renderPassDesc = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = nativeFrameBuffer.Native.colorAttachmentCount,
            colorAttachments = _colorAttachmentsCache.Ptr,
            timestampWrites = timestampWrites,
        };

        if (_depthStencilAttachmentCache.HasValue)
        {
            WGPURenderPassDepthStencilAttachment depthStencilAttachment = _depthStencilAttachmentCache.Value;
            renderPassDesc.depthStencilAttachment = &depthStencilAttachment;
        }

        _renderPass = wgpuCommandEncoderBeginRenderPass(_encoder, &renderPassDesc);
    }

    protected override void EndRenderCore()
    {
        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            wgpuRenderPassEncoderEnd(_renderPass);
            wgpuRenderPassEncoderRelease(_renderPass);
            _renderPass = WGPURenderPassEncoder.Null;
        }
    }

    protected override void BeginComputeCore()
    {
        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        _computePass = wgpuCommandEncoderBeginComputePass(_encoder, null);
    }

    protected override void BeginComputeTimestampCore(
        GPUTimestampQuerySet querySet,
        uint beginningQueryIndex,
        uint endQueryIndex)
    {
        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();

        WGPUPassTimestampWrites timestampWrites = new()
        {
            querySet = ((WebGPUTimestampQuerySet)querySet).Native,
            beginningOfPassWriteIndex = beginningQueryIndex,
            endOfPassWriteIndex = endQueryIndex,
        };
        WGPUComputePassDescriptor descriptor = new()
        {
            timestampWrites = &timestampWrites,
        };
        _computePass = wgpuCommandEncoderBeginComputePass(_encoder, &descriptor);
    }

    protected override void EndComputeCore()
    {
        if (_computePass != WGPUComputePassEncoder.Null)
        {
            wgpuComputePassEncoderEnd(_computePass);
            wgpuComputePassEncoderRelease(_computePass);
            _computePass = WGPUComputePassEncoder.Null;
        }
    }

    protected override void WriteTimestampInsidePassCore(
        GPUTimestampQuerySet querySet,
        uint queryIndex)
    {
        if (_computePass != WGPUComputePassEncoder.Null)
        {
            wgpuComputePassEncoderWriteTimestamp(
                _computePass,
                ((WebGPUTimestampQuerySet)querySet).Native,
                queryIndex);
        }
        else if (_renderPass != WGPURenderPassEncoder.Null)
        {
            wgpuRenderPassEncoderWriteTimestamp(
                _renderPass,
                ((WebGPUTimestampQuerySet)querySet).Native,
                queryIndex);
        }
    }

    protected override void ResolveTimestampsCore(
        GPUTimestampQuerySet querySet,
        uint firstQuery,
        uint queryCount,
        GPUBuffer destination,
        ulong destinationOffset)
    {
        WGPUQuerySet nativeQuerySet = ((WebGPUTimestampQuerySet)querySet).Native;
        WGPUBuffer nativeDestination = ((WebGPUBuffer)destination).Native;
        wgpuCommandEncoderResolveQuerySet(
            _encoder,
            nativeQuerySet,
            firstQuery,
            queryCount,
            nativeDestination,
            destinationOffset);
    }

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
        wgpuRenderPassEncoderSetScissorRect(_renderPass, x, y, width, height);
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline);
    }

    protected override void SetStencilReferenceCore(uint value)
    {
        wgpuRenderPassEncoderSetStencilReference(_renderPass, value);
    }

    protected unsafe override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        WebGPUResourceGroup nativeResourceGroup = (WebGPUResourceGroup)resourceGroup;
        wgpuRenderPassEncoderSetBindGroup(_renderPass, slot, nativeResourceGroup.Native, 0, null);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        wgpuRenderPassEncoderSetVertexBuffer(_renderPass, slot, nativeBuffer.Native, offset, size);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        wgpuRenderPassEncoderSetIndexBuffer(_renderPass, nativeBuffer.Native, WebGPUUtility.IndexFormatToWebGPU(format), offset, size);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        wgpuRenderPassEncoderDraw(_renderPass, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        wgpuRenderPassEncoderDrawIndexed(_renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        wgpuRenderPassEncoderDrawIndirect(_renderPass, nativeBuffer.Native, offset);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        wgpuRenderPassEncoderDrawIndexedIndirect(_renderPass, nativeBuffer.Native, offset);
    }



    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        wgpuRenderPassEncoderSetImmediates(_renderPass, bufferOffset, data, size);
    }

    protected override unsafe void PushComputeConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        wgpuComputePassEncoderSetImmediates(_computePass, bufferOffset, data, size);
    }

    protected unsafe override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        _computePipeline = ((WebGPUComputePipeline)pipeline).Native;
        wgpuComputePassEncoderSetPipeline(_computePass, _computePipeline);
    }

    protected unsafe override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateComputePipeline();

        WebGPUResourceGroup nativeResourceGroup = (WebGPUResourceGroup)resourceGroup;
        wgpuComputePassEncoderSetBindGroup(_computePass, slot, nativeResourceGroup.Native, 0, null);
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        ValidateComputePipeline();

        wgpuComputePassEncoderDispatchWorkgroups(_computePass, x, y, z);
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateComputePipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        wgpuComputePassEncoderDispatchWorkgroupsIndirect(_computePass, nativeBuffer.Native, offset);
    }

    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        WebGPUBuffer nativeSrc = (WebGPUBuffer)src;
        WebGPUBuffer nativeDst = (WebGPUBuffer)dst;
        wgpuCommandEncoderCopyBufferToBuffer(_encoder, nativeSrc.Native, srcOffset, nativeDst.Native, dstOffset, size);
    }


    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint mipLevel, uint offset, TextureAspect aspect)
    {
        WebGPUBuffer nativeSrc = (WebGPUBuffer)src;
        WebGPUTexture nativeDst = (WebGPUTexture)dst;
        WGPUExtent3D extent = new WGPUExtent3D
        {
            width = nativeDst.Width,
            height = nativeDst.Height,
            depthOrArrayLayers = nativeDst.Depth
        };

        WGPUTexture nativeTexture = nativeDst.Native;
        WGPUBuffer nativeBuffer = nativeSrc.Native;
        WGPUTexelCopyBufferInfo imageCopyBuffer = new WGPUTexelCopyBufferInfo
        {
            buffer = nativeBuffer,
            layout = WebGPUUtility.GetTextureDataLayout(nativeDst.PixelFormat, nativeDst.Width, nativeDst.Height),
        };

        WGPUTexelCopyTextureInfo imageCopyTexture = new WGPUTexelCopyTextureInfo
        {
            texture = nativeTexture,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D
            {
                x = 0,
                y = 0,
                z = 0
            },

            aspect = WebGPUUtility.TextureAspectToWebGPU(aspect)
        };


        wgpuCommandEncoderCopyBufferToTexture(_encoder, &imageCopyBuffer, &imageCopyTexture, &extent);


    }

    protected override void CopyTextureCore(GPUTexture src, GPUTexture dst, uint srcMipLevel, uint dstMipLevel, TextureAspect aspect)
    {
        WebGPUTexture nativeSrc = (WebGPUTexture)src;
        WebGPUTexture nativeDst = (WebGPUTexture)dst;
        WGPUTextureAspect wgpuAspect = WebGPUUtility.TextureAspectToWebGPU(aspect);

        WGPUTexelCopyTextureInfo source = new WGPUTexelCopyTextureInfo
        {
            texture = nativeSrc.Native,
            mipLevel = srcMipLevel,
            origin = new WGPUOrigin3D { x = 0, y = 0, z = 0 },
            aspect = wgpuAspect
        };

        WGPUTexelCopyTextureInfo destination = new WGPUTexelCopyTextureInfo
        {
            texture = nativeDst.Native,
            mipLevel = dstMipLevel,
            origin = new WGPUOrigin3D { x = 0, y = 0, z = 0 },
            aspect = wgpuAspect
        };

        // Copy the full mip extent (adjusted for the source mip level).
        uint mipWidth = nativeSrc.GetMipWidth(srcMipLevel);
        uint mipHeight = nativeSrc.GetMipHeight(srcMipLevel);
        WGPUExtent3D copySize = new WGPUExtent3D
        {
            width = mipWidth,
            height = mipHeight,
            depthOrArrayLayers = 1
        };

        wgpuCommandEncoderCopyTextureToTexture(_encoder, &source, &destination, &copySize);
    }

    protected override void ExecuteBundleCore(GPURenderBundle bundle)
    {
        WebGPURenderBundle nativeBundle = (WebGPURenderBundle)bundle;
        WGPURenderBundle native = nativeBundle.Native;
        wgpuRenderPassEncoderExecuteBundles(_renderPass, 1, &native);
    }

    protected override void ExecuteBundleCore(ReadOnlySpan<GPURenderBundle> bundle)
    {
        WGPURenderBundle* nativeBundles = stackalloc WGPURenderBundle[bundle.Length];
        for (int i = 0; i < bundle.Length; i++)
        {
            WebGPURenderBundle nativeBundle = (WebGPURenderBundle)bundle[i];
            nativeBundles[i] = nativeBundle.Native;
        }
        wgpuRenderPassEncoderExecuteBundles(_renderPass, (nuint)bundle.Length, nativeBundles);
    }





    #endregion

    #region WebGPU Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static WGPULoadOp ToWebGPU(AttachmentLoadOp loadOp)
    {
        return loadOp switch
        {
            AttachmentLoadOp.Load => WGPULoadOp.Load,
            AttachmentLoadOp.Clear => WGPULoadOp.Clear,
            _ => WGPULoadOp.Load,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static WGPUStoreOp ToWebGPU(AttachmentStoreOp storeOp)
    {
        return storeOp switch
        {
            AttachmentStoreOp.Store => WGPUStoreOp.Store,
            AttachmentStoreOp.Discard => WGPUStoreOp.Discard,
            _ => WGPUStoreOp.Store,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WGPUCommandBuffer TakeBuffer()
    {
        WGPUCommandBuffer buffer = _buffer;
        _buffer = WGPUCommandBuffer.Null;
        return buffer;
    }

    public unsafe WebGPUCommandBuffer(WebGPUDevice device, in CommandBufferDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        WGPUDevice nativeDevice = device.Native;
        _nativeDevice = nativeDevice;

        _buffer = WGPUCommandBuffer.Null;
        _encoder = WGPUCommandEncoder.Null;

        _renderPass = WGPURenderPassEncoder.Null;
        _computePass = WGPUComputePassEncoder.Null;

        ReadOnlySpan<byte> nameSpan = Name.GetUtf8Span();
        fixed (byte* ptr = nameSpan)
        {
            _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length + 1);
            InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
            _nativeNameView = new WGPUStringView(_nativeName, nameSpan.Length);
        }

        _colorAttachmentsCache = new UnsafeArray<WGPURenderPassColorAttachment>(8);
    }

    private void ReleaseCommandEncoder()
    {
        if (_encoder != WGPUCommandEncoder.Null)
        {
            wgpuCommandEncoderRelease(_encoder);
            _encoder = WGPUCommandEncoder.Null;
        }
    }

    private void ReleaseCommandBuffer()
    {
        if (_buffer != WGPUCommandBuffer.Null)
        {
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentRenderPass()
    {
        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            wgpuRenderPassEncoderEnd(_renderPass);
            wgpuRenderPassEncoderRelease(_renderPass);
            _renderPass = WGPURenderPassEncoder.Null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFinishCurrentComputePass()
    {
        if (_computePass != WGPUComputePassEncoder.Null)
        {
            wgpuComputePassEncoderEnd(_computePass);
            wgpuComputePassEncoderRelease(_computePass);
            _computePass = WGPUComputePassEncoder.Null;
        }
    }

    //debug validate

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateGraphicsPipeline()
    {
        if (_graphicsPipeline == WGPURenderPipeline.Null)
        {
            throw ExceptionNoGraphicsPipeline;
        }
    }

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateComputePipeline()
    {
        if (_computePipeline == WGPUComputePipeline.Null)
        {
            throw ExceptionNoComputePipeline;
        }
    }

    #endregion
}
