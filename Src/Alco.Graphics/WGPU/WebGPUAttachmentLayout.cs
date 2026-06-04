
using System.Numerics;
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUAttachmentLayout : GPUAttachmentLayout
{

    #region Properties
    private readonly WGPUDevice _nativeDevice;

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

    public unsafe WebGPUAttachmentLayout(WebGPUDevice device, in AttachmentLayoutDescriptor descriptor) : base(descriptor)
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
                format = WebGPUUtility.PixelFormatToWebGPU(color.Format),
                clearColor = WebGPUUtility.ConvertColor(color.ClearColor),
            };
        }

        if (descriptor.Depth.HasValue)
        {

            DepthAttachment depth = descriptor.Depth.Value;
            _depthInfo = new WGPUDepthAttachmentInfo
            {
                format = WebGPUUtility.PixelFormatToWebGPU(depth.Format),
                clearDepth = depth.ClearDepth,
                isDepthReadOnly = false,
                clearStencil = depth.ClearStencil,
                isStencilReadOnly = false,
            };
        }
    }

    #endregion
}