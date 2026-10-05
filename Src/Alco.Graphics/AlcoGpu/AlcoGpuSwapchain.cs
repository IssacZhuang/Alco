using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuSwapchain : GPUSwapchain
{
    private readonly AlcoGpuDevice _device;
    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private readonly AlcoHandle _surface;
    private readonly AlcoGpuSurfaceFrameBuffer _frameBuffer;

    private readonly PixelFormat _surfaceFormat;
    private readonly PixelFormat? _depthFormat;
    private readonly PixelFormat[] _supportedSurfaceFormats;
    private readonly uint[] _supportedPresentModes;

    private AlcoSurfaceConfig _config;
    private bool _isVSyncEnabled;

    /// <summary>
    /// Creates a custom swapchain from the supplied descriptor.
    /// </summary>
    internal AlcoGpuSwapchain(AlcoGpuDevice device, in SwapchainDescriptor descriptor) : base(descriptor)
    {
        _device = device;

        _surface = CreateSurface(device, descriptor.SurfaceSource);

        // check compatibility
        AlcoSurfaceCaps caps = default;
        AlcoGpuNative.SurfaceGetCapabilities(device.Native, _surface, ref caps);

        // get supported present modes (ABI present-mode values)
        _supportedPresentModes = new uint[caps.PresentModeCount];
        for (uint i = 0; i < caps.PresentModeCount; i++)
        {
            _supportedPresentModes[i] = caps.PresentModes[i];
        }

        // get supported formats
        _supportedSurfaceFormats = new PixelFormat[caps.FormatCount];
        for (uint i = 0; i < caps.FormatCount; i++)
        {
            _supportedSurfaceFormats[i] = (PixelFormat)caps.Formats[i];
        }

        _surfaceFormat = descriptor.ColorFormat;
        bool isFormatSupported = false;
        for (int i = 0; i < _supportedSurfaceFormats.Length; i++)
        {
            if (_supportedSurfaceFormats[i] == _surfaceFormat)
            {
                isFormatSupported = true;
                break;
            }
        }
        if (!isFormatSupported)
        {
            PixelFormat oldFormat = _surfaceFormat;
            _surfaceFormat = _supportedSurfaceFormats[0];
            _device.LogInfo($"Surface format {oldFormat} is not supported, using {_surfaceFormat} instead");
        }

        //create attachment layout
        DepthAttachment? depth = null;
        if (descriptor.DepthFormat.HasValue)
        {
            _depthFormat = descriptor.DepthFormat.Value;
            depth = new DepthAttachment()
            {
                Format = descriptor.DepthFormat.Value,
                ClearDepth = 1.0f,
                ClearStencil = 0,
            };
        }

        AttachmentLayoutDescriptor attachmentLayoutDescriptor = new AttachmentLayoutDescriptor(
            new ColorAttachment[]
            {
                new ColorAttachment()
                {
                    Format = _surfaceFormat,
                    ClearColor = descriptor.ClearColor,
                },
            },
            depth,
            "surface_render_pass"
        );

        _attachmentLayout = new AlcoGpuAttachmentLayout(device, attachmentLayoutDescriptor);

        _config.Format = (uint)_attachmentLayout.ColorInfos[0].Format;
        // TextureBinding lets frame capture sample the presented surface into a staging
        // texture with a blit (e.g. editor/agent screenshots). ColorAttachment maps to
        // the native RENDER_ATTACHMENT usage.
        _config.Usage = (uint)(TextureUsage.ColorAttachment | TextureUsage.TextureBinding);
        _config.PresentMode = GetPresentMode(descriptor.IsVSyncEnabled);
        _isVSyncEnabled = descriptor.IsVSyncEnabled;
        _config.AlphaMode = AlcoGpuAbi.AlphaModeAbi.Auto;

        _config.Width = descriptor.Width;
        _config.Height = descriptor.Height;
        _config.DesiredFrameLatency = 2;

        // the life cycle of the surface is managed by the AlcoGpuSurfaceFrameBuffer
        // because it must be dropped after the last surface texture is released
        _frameBuffer = new AlcoGpuSurfaceFrameBuffer(_device, _attachmentLayout, _surface, _config);
    }

    private static AlcoHandle CreateSurface(AlcoGpuDevice device, SurfaceSource surface)
    {
        AlcoSurfaceDesc desc = default;
        switch (surface)
        {
            case Win32SurfaceSource win32Surface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.Win32;
                desc.Handle = (ulong)win32Surface.Hwnd;
                desc.Display = (ulong)win32Surface.HInstance;
                break;
            case MetalLayerSurfaceHandle metalLayerSurface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.MetalLayer;
                desc.Handle = (ulong)metalLayerSurface.Layer;
                break;
            case WaylandSurfaceSource waylandSurface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.Wayland;
                desc.Display = (ulong)waylandSurface.Display;
                desc.Handle = (ulong)waylandSurface.Surface;
                break;
            case XcbWindowSurfaceSource xcbWindowSurface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.Xcb;
                desc.Display = (ulong)xcbWindowSurface.Connection;
                desc.Handle = xcbWindowSurface.Window;
                break;
            case XlibWindowSurfaceSource xlibWindowSurface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.Xlib;
                desc.Display = (ulong)xlibWindowSurface.Display;
                desc.Handle = xlibWindowSurface.Window;
                break;
            case AndroidWindowSurfaceSource androidWindowSurface:
                desc.Tag = AlcoGpuAbi.SurfaceTag.Android;
                desc.Handle = (ulong)androidWindowSurface.Window;
                break;
            default:
                throw new GraphicsException($"Unsupported surface source {surface?.GetType().Name} for the alco-gpu backend");
        }

        ReadOnlySpan<byte> name = "swapchain_surface".Utf8Z();
        fixed (byte* ptrName = name)
        {
            desc.Name = ptrName;
            AlcoGpuNative.SurfaceCreate(device.Native, in desc, out AlcoHandle handle);
            return handle;
        }
    }

    #region Abstract Implementation

    public override GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer;
    }

    public override bool IsVSyncEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isVSyncEnabled;
        set
        {
            if (_isVSyncEnabled == value)
            {
                return;
            }

            _isVSyncEnabled = value;
            _config.PresentMode = GetPresentMode(value);
            _frameBuffer.UpdateSurfaceConfig(_config);
        }
    }

    protected override GPUDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public override bool RequestSurfaceTexture()
    {
        return _frameBuffer.RequestSurfaceTexture();
    }

    public override void Present()
    {
        _frameBuffer.Present();
    }

    public override void Resize(uint width, uint height)
    {
        _config.Width = width;
        _config.Height = height;
        _frameBuffer.UpdateSurfaceConfig(_config);
    }

    protected override void Dispose(bool disposing)
    {
        // The frame buffer owns the native surface (dropped after its textures).
        _frameBuffer.Dispose();
    }

    #endregion

    #region AlcoGpu Implementation

    internal uint GetPresentMode(bool vsync)
    {
        if (!vsync)
        {
            if (IsPresentModeSupported(AlcoGpuAbi.PresentModeAbi.Immediate))
            {
                return AlcoGpuAbi.PresentModeAbi.Immediate;
            }
            else if (IsPresentModeSupported(AlcoGpuAbi.PresentModeAbi.Mailbox))
            {
                return AlcoGpuAbi.PresentModeAbi.Mailbox;
            }
            else
            {
                _device.LogWarning("VSync is off but no supported present mode found, using FIFO");
            }
        }
        return AlcoGpuAbi.PresentModeAbi.Fifo;
    }

    private bool IsPresentModeSupported(uint mode)
    {
        for (int i = 0; i < _supportedPresentModes.Length; i++)
        {
            if (_supportedPresentModes[i] == mode)
            {
                return true;
            }
        }
        return false;
    }

    #endregion
}
