using System.Diagnostics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

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
    private WebGPUFrameBufferBase? _frameBuffer;

    // create on end(), can be reused
    private WGPUCommandBuffer _buffer;

    //release on dispose
    private readonly byte* _nativeName;

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

        UtilsInterop.Free(_nativeName);
        _colorAttachmentsCache.Dispose();
    }

    // begin the encoder
    protected unsafe override void BeginCore()
    {
        WGPUCommandEncoderDescriptor descriptor = new WGPUCommandEncoderDescriptor
        {
            label = _nativeName
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
        //check if there is any render pass descriptor is set but not start
        CheckRenderPass(false);

        TryFinishCurrentComputePass();
        TryFinishCurrentRenderPass();

        WGPUCommandBufferDescriptor descriptor = new WGPUCommandBufferDescriptor
        {
            label = _nativeName
        };

        _buffer = wgpuCommandEncoderFinish(_encoder, &descriptor);

        _graphicsPipeline = WGPURenderPipeline.Null;
        _computePipeline = WGPUComputePipeline.Null;
        _frameBuffer = null;
        _depthStencilAttachmentCache = null;

        // release encoder
        wgpuCommandEncoderRelease(_encoder);
        _encoder = WGPUCommandEncoder.Null;
    }

    protected override unsafe void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
    {
        WebGPUFrameBufferBase nativeFrameBuffer = (WebGPUFrameBufferBase)frameBuffer;
        _frameBuffer = nativeFrameBuffer;
        TryFinishCurrentRenderPass();
        //TryFinishCurrentComputePass();
        WGPURenderPassDescriptor tmpDescriptor = nativeFrameBuffer.Native;
        _colorAttachmentsCache.EnsureCapacity((int)tmpDescriptor.colorAttachmentCount);

        for (uint i = 0; i < tmpDescriptor.colorAttachmentCount; i++)
        {
            _colorAttachmentsCache[i] = tmpDescriptor.colorAttachments[i];
        }

        if (tmpDescriptor.depthStencilAttachment != null)
        {
            _depthStencilAttachmentCache = *tmpDescriptor.depthStencilAttachment;
        }
        else
        {
            _depthStencilAttachmentCache = null;
        }
    }

    protected override void ClearColorCore(ColorFloat color, uint index)
    {

        if (_frameBuffer == null)
        {
            throw new GraphicsException("Framebuffer must be setted before ClearColor");
        }

        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            throw new GraphicsException("The pipeline is already set, can not clear color");
        }

        if (index >= _frameBuffer.Native.colorAttachmentCount)
        {
            throw new GraphicsException("The index is out of range of color attachment");
        }

        WGPURenderPassColorAttachment attachment = _colorAttachmentsCache[index];
        attachment.loadOp = WGPULoadOp.Clear;
        attachment.storeOp = WGPUStoreOp.Store;
        attachment.clearValue = new WGPUColor
        {
            r = color.R,
            g = color.G,
            b = color.B,
            a = color.A
        };
        _colorAttachmentsCache[index] = attachment;
    }

    protected override void ClearDepthCore(float depth)
    {
        if (_frameBuffer == null)
        {
            throw new GraphicsException("Framebuffer must be setted before ClearDepthStencil");
        }

        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            throw new GraphicsException("The pipeline is already set, can not clear depth stencil");
        }

        if (!_depthStencilAttachmentCache.HasValue)
        {
            //no depth stencil attachment
            return;
        }

        WGPURenderPassDepthStencilAttachment attachment = _depthStencilAttachmentCache.Value;
        attachment.depthLoadOp = WGPULoadOp.Clear;
        attachment.depthStoreOp = WGPUStoreOp.Store;
        attachment.depthClearValue = depth;
        _depthStencilAttachmentCache = attachment;
    }

    protected override void ClearStencilCore(uint stencil)
    {
        if (_frameBuffer == null)
        {
            throw new GraphicsException("Framebuffer must be setted before ClearDepthStencil");
        }

        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            throw new GraphicsException("The pipeline is already set, can not clear depth stencil");
        }

        if (!_depthStencilAttachmentCache.HasValue)
        {
            //no depth stencil attachment
            return;
        }

        WGPURenderPassDepthStencilAttachment attachment = _depthStencilAttachmentCache.Value;
        attachment.stencilLoadOp = WGPULoadOp.Clear;
        attachment.stencilStoreOp = WGPUStoreOp.Store;
        attachment.stencilClearValue = stencil;
        _depthStencilAttachmentCache = attachment;
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        CheckRenderPass(true);

        _graphicsPipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline);
    }

    protected override void SetStencilReferenceCore(uint value)
    {
        ValidateGraphicsPipeline();

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
        wgpuRenderPassEncoderSetIndexBuffer(_renderPass, nativeBuffer.Native, UtilsWebGPU.IndexFormatToWebGPU(format), offset, size);
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



    protected override unsafe void PushConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        WGPUShaderStage shaderStage = UtilsWebGPU.ConvertShaderStage(stage);
        wgpuRenderPassEncoderSetPushConstants(_renderPass, shaderStage, bufferOffset, size, data);
    }

    protected unsafe override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        CheckComputePass();
        //ValidateComputePass();

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

    #endregion

    #region WebGPU Implementation

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
            _nativeName = UtilsInterop.Alloc<byte>(nameSpan.Length + 1);
            UtilsInterop.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckRenderPass(bool throwIfNotExist = false)
    {
        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            return;
        }

        TryFinishCurrentComputePass();

        if (_frameBuffer != null)
        {
            WGPURenderPassDescriptor tmp = new WGPURenderPassDescriptor
            {
                colorAttachmentCount = _frameBuffer.Native.colorAttachmentCount,
                colorAttachments = _colorAttachmentsCache.Ptr,
            };

            if (_depthStencilAttachmentCache.HasValue)
            {
                //stackalloc
                WGPURenderPassDepthStencilAttachment depthStencilAttachment = _depthStencilAttachmentCache.Value;
                tmp.depthStencilAttachment = &depthStencilAttachment;
            }

            _renderPass = wgpuCommandEncoderBeginRenderPass(_encoder, &tmp);
            return;
        }

        if (throwIfNotExist)
        {
            throw ExceptionNoFramebuffer;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckComputePass()
    {
        if (_computePass != WGPUComputePassEncoder.Null)
        {
            return;
        }

        TryFinishCurrentRenderPass();

        _computePass = wgpuCommandEncoderBeginComputePass(_encoder, null);
    }


    //debug validate

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateComputePass()
    {
        if (_computePass == WGPUComputePassEncoder.Null)
        {
            throw ExceptionNoComputePipeline;
        }
    }

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