
using System.Numerics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPURenderPass : GPURenderPass
{




    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private readonly RenderPassDescriptor _descriptor;

    //the texture view are not setted in the WebGPURenderPass object, these attachments are used to create the framebuffer
    private readonly WGPUColorAttachmentInfo[] _colorInfos;
    private readonly WGPUDepthAttachmentInfo? _depthInfo;

    #endregion

    #region Abstract Implementation

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor.Name;
    }

    public override IReadOnlyList<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor.Colors;
    }

    public override DepthAttachment? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor.Depth;
    }

    protected override void Dispose(bool disposing)
    {
        // Nothing to do because only meta data inside
    }

    public override GPUFrameBuffer CreateFrameBuffer(uint width, uint height, string? name = null)
    {
        if (name == null)
        {
            name = $"{Name} - FrameBuffer";
        }

        return new WebGPUFrameBuffer(this, width, height, name);
    }

    #endregion

    #region WebGPU Implementation

    internal IReadOnlyList<WGPUColorAttachmentInfo> WebGPUColorInfos
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
    public unsafe WebGPURenderPass(WGPUDevice nativeDevice, in RenderPassDescriptor descriptor)
    {
        _descriptor = descriptor;
        _nativeDevice = nativeDevice;

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