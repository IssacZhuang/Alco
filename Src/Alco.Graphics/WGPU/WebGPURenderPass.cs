
using System.Numerics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPURenderPass : GPURenderPass
{

    #region Properties
    private readonly WGPUDevice _nativeDevice;

    //the texture view are not setted in the WebGPURenderPass object, these attachments are used to create the framebuffer
    private readonly WGPUColorAttachmentInfo[] _colorInfos;
    private readonly WGPUDepthAttachmentInfo? _depthInfo;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        // Nothing to do because only meta data inside
    }

    #endregion

    #region WebGPU Implementation

    internal ReadOnlySpan<WGPUColorAttachmentInfo> WebGPUColorInfos
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorInfos;
    }

    internal WGPUDepthAttachmentInfo? WebGPUDepthInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthInfo;
    }

    internal WGPUDevice NativeDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeDevice;
    }

    // for GPUDevice.CreateRenderPass(RenderPassDescriptor)
    public unsafe WebGPURenderPass(WebGPUDevice device, in RenderPassDescriptor descriptor): base(descriptor)
    {
        Device = device;
        
        _nativeDevice = device.Native;

        int colorCount = descriptor.Colors.Length;

        _colorInfos = new WGPUColorAttachmentInfo[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            ColorAttachment color = descriptor.Colors[i];
            _colorInfos[i] = new WGPUColorAttachmentInfo
            {
                format = UtilsWebGPU.PixelFormatToWebGPU(color.Format),
                clearColor = UtilsWebGPU.ConvertColor(color.ClearColor),
            };
        }

        if (descriptor.Depth.HasValue)
        {

            DepthAttachment depth = descriptor.Depth.Value;
            _depthInfo = new WGPUDepthAttachmentInfo
            {
                format = UtilsWebGPU.PixelFormatToWebGPU(depth.Format),
                clearDepth = depth.ClearDepth,
                isDepthReadOnly = false,
                clearStencil = depth.ClearStencil,
                isStencilReadOnly = false,
            };
        }
    }

    #endregion
}