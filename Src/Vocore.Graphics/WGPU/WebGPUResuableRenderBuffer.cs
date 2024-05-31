using System.Diagnostics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUResuableRenderBuffer : GPUResuableRenderBuffer
{
    private static readonly Exception ExceptionNoFramebuffer = new("No framebuffer is set before set the graphics pipeline");
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");


    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private WGPURenderBundleEncoder _renderBundleEncoder;
    private WGPURenderBundle _bundle;

    private WGPURenderPipeline _graphicsPipeline;
    private WebGPUFrameBufferBase? _frameBuffer;


    //release on dispose
    private readonly sbyte* _nativeName;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bundle != WGPURenderBundle.Null;
    }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        // the buffer will not be released if the End() is not called
        // do check here to prevent memory leak
        
        ReleaseRenderBundle();
        ReleaseRenderBunleEncoder();

        UtilsInterop.Free(_nativeName);
    }

    // begin the encoder
    protected unsafe override void BeginCore(GPUFrameBuffer frameBuffer)
    {
        ReleaseRenderBunleEncoder();
        _frameBuffer = (WebGPUFrameBufferBase)frameBuffer;

        int colorCount = _frameBuffer.NativeColorFormats.Count;
        WGPUTextureFormat* colors = stackalloc WGPUTextureFormat[colorCount];
        for (int i = 0; i < colorCount; i++)
        {

            colors[i] = _frameBuffer.NativeColorFormats[i];
        }

        WGPURenderBundleEncoderDescriptor descriptor = new WGPURenderBundleEncoderDescriptor
        {
            label = _nativeName,
            colorFormatCount = (nuint)colorCount,
            colorFormats = colors,
            depthStencilFormat = _frameBuffer.NativeDepthFormat.HasValue? _frameBuffer.NativeDepthFormat.Value : WGPUTextureFormat.Undefined,
            sampleCount = 1,
        };
        _renderBundleEncoder = wgpuDeviceCreateRenderBundleEncoder(_nativeDevice, &descriptor);
    }

    // end the encoder
    protected unsafe override void EndCore()
    {
        ReleaseRenderBundle();
        WGPURenderBundleDescriptor descriptor = new WGPURenderBundleDescriptor
        {
            label = _nativeName
        };
        _bundle = wgpuRenderBundleEncoderFinish(_renderBundleEncoder, &descriptor);
        ReleaseRenderBunleEncoder();
        _graphicsPipeline = WGPURenderPipeline.Null;

    }


    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        //wgpuRenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline);
        wgpuRenderBundleEncoderSetPipeline(_renderBundleEncoder, _graphicsPipeline);
    }

    protected unsafe override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        WebGPUResourceGroup nativeResourceGroup = (WebGPUResourceGroup)resourceGroup;
        // wgpuRenderPassEncoderSetBindGroup(_renderPass, slot, nativeResourceGroup.Native, 0, null);
        wgpuRenderBundleEncoderSetBindGroup(_renderBundleEncoder, slot, nativeResourceGroup.Native, 0, null);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        //wgpuRenderPassEncoderSetVertexBuffer(_renderPass, slot, nativeBuffer.Native, offset, size);
        wgpuRenderBundleEncoderSetVertexBuffer(_renderBundleEncoder, slot, nativeBuffer.Native, offset, size);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        //wgpuRenderPassEncoderSetIndexBuffer(_renderPass, nativeBuffer.Native, UtilsWebGPU.IndexFormatToWebGPU(format), offset, size);
        wgpuRenderBundleEncoderSetIndexBuffer(_renderBundleEncoder, nativeBuffer.Native, UtilsWebGPU.IndexFormatToWebGPU(format), offset, size);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        //wgpuRenderPassEncoderDraw(_renderPass, vertexCount, instanceCount, firstVertex, firstInstance);
        wgpuRenderBundleEncoderDraw(_renderBundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        //wgpuRenderPassEncoderDrawIndexed(_renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        wgpuRenderBundleEncoderDrawIndexed(_renderBundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        //wgpuRenderPassEncoderDrawIndirect(_renderPass, nativeBuffer.Native, offset);
        wgpuRenderBundleEncoderDrawIndirect(_renderBundleEncoder, nativeBuffer.Native, offset);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        //wgpuRenderPassEncoderDrawIndexedIndirect(_renderPass, nativeBuffer.Native, offset);
        wgpuRenderBundleEncoderDrawIndexedIndirect(_renderBundleEncoder, nativeBuffer.Native, offset);
    }


    #endregion

    #region WebGPU Implementation

    public unsafe WebGPUResuableRenderBuffer(WebGPUDevice device, ResuableRenderBufferDescriptor? descriptor = null)
    {
        Device = device;
        WGPUDevice nativeDevice = device.Native;
        
        _nativeDevice = nativeDevice;
        if (descriptor.HasValue)
        {
            Name = descriptor.Value.Name;
        }
        else
        {
            Name = "unnamed_command_buffer";
        }

        ReadOnlySpan<sbyte> nameSpan = Name.GetUtf8Span();
        fixed (sbyte* ptr = nameSpan)
        {
            _nativeName = UtilsInterop.Alloc<sbyte>(nameSpan.Length + 1);
            UtilsInterop.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReleaseRenderBundle()
    {
        if (_bundle != WGPURenderBundle.Null)
        {
            wgpuRenderBundleRelease(_bundle);
            _bundle = WGPURenderBundle.Null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReleaseRenderBunleEncoder()
    {
        if (_renderBundleEncoder != WGPURenderBundleEncoder.Null)
        {
            wgpuRenderBundleEncoderRelease(_renderBundleEncoder);
            _renderBundleEncoder = WGPURenderBundleEncoder.Null;
        }
    }


    internal void ExecuteBundle(WGPUQueue queue)
    {
        if (_bundle == WGPURenderBundle.Null)
        {
            throw new InvalidOperationException("No render bundle is set before drawing");
        }

        if(_frameBuffer == null)
        {
            throw ExceptionNoFramebuffer;
        }

        WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, null);
        WGPURenderPassDescriptor descriptor = _frameBuffer.Native;
        WGPURenderPassEncoder renderPass = wgpuCommandEncoderBeginRenderPass(encoder, &descriptor);
        
        WGPURenderBundle bundle = _bundle;
        wgpuRenderPassEncoderExecuteBundles(renderPass, 1, &bundle);
        wgpuRenderPassEncoderEnd(renderPass);
        WGPUCommandBuffer buffer = wgpuCommandEncoderFinish(encoder, null);
        wgpuQueueSubmit(queue, 1, &buffer);

        wgpuCommandBufferRelease(buffer);
        wgpuRenderPassEncoderRelease(renderPass);
        wgpuCommandEncoderRelease(encoder);
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