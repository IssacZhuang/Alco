using System.Diagnostics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal unsafe sealed class WebGPURenderBundle : GPURenderBundle
{
    private static readonly Exception ExceptionNoFramebuffer = new("No framebuffer is set before set the graphics pipeline");
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");


    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private WGPURenderBundleEncoder _renderBundleEncoder;
    private WGPURenderBundle _bundle;

    private WGPURenderPipeline _graphicsPipeline;


    //release on dispose
    private readonly byte* _nativeName;
    private readonly WGPUStringView _nativeNameView;

    #endregion

    #region Abstract Implementation

    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bundle != WGPURenderBundle.Null;
    }

    internal WGPURenderBundle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get=> _bundle;
    }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        // the buffer will not be released if the End() is not called
        // do check here to prevent memory leak

        ReleaseRenderBundle();
        ReleaseRenderBundleEncoder();

        InteropUtility.Free(_nativeName);
    }

    // begin the encoder
    protected unsafe override void BeginCore(GPUAttachmentLayout attachmentLayout)
    {
        ReleaseRenderBundleEncoder();
        WebGPUAttachmentLayout nativeAttachmentLayout = (WebGPUAttachmentLayout)attachmentLayout;

        int colorCount = nativeAttachmentLayout.WebGPUColorInfos.Length;
        WGPUTextureFormat* colors = stackalloc WGPUTextureFormat[colorCount];
        for (int i = 0; i < colorCount; i++)
        {

            colors[i] = nativeAttachmentLayout.WebGPUColorInfos[i].format;
        }

        WGPURenderBundleEncoderDescriptor descriptor = new WGPURenderBundleEncoderDescriptor
        {
            label = _nativeNameView,
            colorFormatCount = (nuint)colorCount,
            colorFormats = colors,
            depthStencilFormat = nativeAttachmentLayout.WebGPUDepthInfo?.format ?? WGPUTextureFormat.Undefined,
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
            label = _nativeNameView
        };
        _bundle = wgpuRenderBundleEncoderFinish(_renderBundleEncoder, &descriptor);
        ReleaseRenderBundleEncoder();
        _graphicsPipeline = WGPURenderPipeline.Null;

    }


    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderBundleEncoderSetPipeline(_renderBundleEncoder, _graphicsPipeline);
    }

    protected unsafe override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        WebGPUResourceGroup nativeResourceGroup = (WebGPUResourceGroup)resourceGroup;
        wgpuRenderBundleEncoderSetBindGroup(_renderBundleEncoder, slot, nativeResourceGroup.Native, 0, null);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        wgpuRenderBundleEncoderSetVertexBuffer(_renderBundleEncoder, slot, nativeBuffer.Native, offset, size);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)buffer;
        wgpuRenderBundleEncoderSetIndexBuffer(_renderBundleEncoder, nativeBuffer.Native, UtilsWebGPU.IndexFormatToWebGPU(format), offset, size);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        wgpuRenderBundleEncoderDraw(_renderBundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        wgpuRenderBundleEncoderDrawIndexed(_renderBundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        wgpuRenderBundleEncoderDrawIndirect(_renderBundleEncoder, nativeBuffer.Native, offset);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        WebGPUBuffer nativeBuffer = (WebGPUBuffer)indirectBuffer;
        wgpuRenderBundleEncoderDrawIndexedIndirect(_renderBundleEncoder, nativeBuffer.Native, offset);
    }

    protected override unsafe void PushGraphicsConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        ValidateGraphicsPipeline();

        WGPUShaderStage shaderStage = UtilsWebGPU.ConvertShaderStage(stage);
        wgpuRenderBundleEncoderSetPushConstants(_renderBundleEncoder, shaderStage, bufferOffset, size, data);
    }


    #endregion

    #region WebGPU Implementation

    public unsafe WebGPURenderBundle(WebGPUDevice device, in RenderBundleDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        WGPUDevice nativeDevice = device.Native;

        _nativeDevice = nativeDevice;

        ReadOnlySpan<byte> nameSpan = Name.GetUtf8Span();
        fixed (byte* ptr = nameSpan)
        {
            _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length + 1);
            InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
            _nativeNameView = new WGPUStringView(_nativeName, nameSpan.Length);
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
    private void ReleaseRenderBundleEncoder()
    {
        if (_renderBundleEncoder != WGPURenderBundleEncoder.Null)
        {
            wgpuRenderBundleEncoderRelease(_renderBundleEncoder);
            _renderBundleEncoder = WGPURenderBundleEncoder.Null;
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