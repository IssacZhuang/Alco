using System.Diagnostics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUResuableRenderBuffer : GPUResuableRenderBuffer
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

    // cached state from outside
    private UnsafeArray<WGPURenderPassColorAttachment> _colorAttachmentsCache;
    private WGPURenderPassDepthStencilAttachment? _depthStencilAttachmentCache;
    private WGPURenderPipeline _graphicsPipeline;
    private WebGPUFrameBufferBase? _frameBuffer;

    // create on end(), can be reused
    private WGPUCommandBuffer _buffer;

    //release on dispose
    private readonly sbyte* _nativeName;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

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
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }
    }

    // end the encoder
    protected unsafe override void EndCore()
    {
        //check if there is any render pass descriptor is set but not start
        CheckRenderPass(false);

        TryFinishCurrentRenderPass();

        WGPUCommandBufferDescriptor descriptor = new WGPUCommandBufferDescriptor
        {
            label = _nativeName
        };

        _buffer = wgpuCommandEncoderFinish(_encoder, &descriptor);

        _graphicsPipeline = WGPURenderPipeline.Null;
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


    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        CheckRenderPass(true);

        _graphicsPipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline);
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

    #endregion

    #region WebGPU Implementation

    public WGPUCommandBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public unsafe WebGPUResuableRenderBuffer(WGPUDevice nativeDevice, CommandBufferDescriptor? descriptor = null)
    {

        _nativeDevice = nativeDevice;
        if (descriptor.HasValue)
        {
            Name = descriptor.Value.Name;
        }
        else
        {
            Name = "unnamed_command_buffer";
        }

        _buffer = WGPUCommandBuffer.Null;
        _encoder = WGPUCommandEncoder.Null;

        _renderPass = WGPURenderPassEncoder.Null;

        ReadOnlySpan<sbyte> nameSpan = Name.GetUtf8Span();
        fixed (sbyte* ptr = nameSpan)
        {
            _nativeName = UtilsInterop.Alloc<sbyte>(nameSpan.Length + 1);
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



    private void CheckRenderPass(bool throwIfNotExist = false)
    {
        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            return;
        }

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

    #endregion
}