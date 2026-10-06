using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// Swapchain that creates the native surface from a platform window source and drives
/// the per-frame surface frame buffer that owns the surface.
/// </summary>
internal sealed unsafe class AlcoGpuSwapchain : GPUSwapchain
{
    private readonly AlcoGpuDevice _device;
    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private AlcoGPU.SurfaceHandle _surface;
    private readonly AlcoGpuSurfaceFrameBuffer _frameBuffer;

    private readonly PixelFormat _surfaceFormat;
    private readonly PixelFormat? _depthFormat;
    private readonly PixelFormat[] _supportedSurfaceFormats;
    private readonly uint[] _supportedPresentModes;

    private AlcoGPU.SurfaceConfig _config;
    private bool _isVSyncEnabled;

    /// <summary>
    /// Creates a custom swapchain from the supplied descriptor.
    /// </summary>
    internal AlcoGpuSwapchain(AlcoGpuDevice device, in SwapchainDescriptor descriptor) : base(descriptor)
    {
        try
        {
            try
            {
                _device = device;

                _surface = CreateSurface(device, descriptor.SurfaceSource);

                // check compatibility
                AlcoGPU.SurfaceCapabilities caps = default;
                AlcoGpuNative.SurfaceGetCapabilities(_surface, ref caps);

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

                _config.Format = _attachmentLayout.ColorInfos[0].Format;
                // DX12 surface textures cannot be sampled: capture copies them into a sampleable
                // texture instead. Other backends retain the direct surface-sampling path.
                _config.Usage = (TextureUsage.ColorAttachment |
                    (device.Backend == GraphicsBackend.WGPUDx12 ? TextureUsage.Read : TextureUsage.TextureBinding));
                _config.PresentMode = GetPresentMode(descriptor.IsVSyncEnabled);
                _isVSyncEnabled = descriptor.IsVSyncEnabled;
                _config.AlphaMode = AlcoGPU.AlphaMode.Auto;

                _config.Width = descriptor.Width;
                _config.Height = descriptor.Height;
                _config.DesiredFrameLatency = 2;

                // the life cycle of the surface is managed by the AlcoGpuSurfaceFrameBuffer
                // because it must be dropped after the last surface texture is released
                _frameBuffer = new AlcoGpuSurfaceFrameBuffer(this, _device, _attachmentLayout, ref _surface, _config);
            }
            catch
            {
                try { Destroy(false); }
                catch { /* Preserve the construction failure. */ }
                throw;
            }
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(device);
        }
    }

    private static AlcoGPU.SurfaceHandle CreateSurface(AlcoGpuDevice device, SurfaceSource surface)
    {
        try
        {
            AlcoGPU.SurfaceDesc desc = default;
            switch (surface)
            {
                case Win32SurfaceSource win32Surface:
                    desc.Tag = AlcoGPU.SurfaceTag.Win32;
                    desc.Handle = (ulong)win32Surface.Hwnd;
                    desc.Display = (ulong)win32Surface.HInstance;
                    break;
                case MetalLayerSurfaceHandle metalLayerSurface:
                    desc.Tag = AlcoGPU.SurfaceTag.MetalLayer;
                    desc.Handle = (ulong)metalLayerSurface.Layer;
                    break;
                case WaylandSurfaceSource waylandSurface:
                    desc.Tag = AlcoGPU.SurfaceTag.Wayland;
                    desc.Display = (ulong)waylandSurface.Display;
                    desc.Handle = (ulong)waylandSurface.Surface;
                    break;
                case XcbWindowSurfaceSource xcbWindowSurface:
                    desc.Tag = AlcoGPU.SurfaceTag.Xcb;
                    desc.Display = (ulong)xcbWindowSurface.Connection;
                    desc.Handle = xcbWindowSurface.Window;
                    break;
                case XlibWindowSurfaceSource xlibWindowSurface:
                    desc.Tag = AlcoGPU.SurfaceTag.Xlib;
                    desc.Display = (ulong)xlibWindowSurface.Display;
                    desc.Handle = xlibWindowSurface.Window;
                    break;
                case AndroidWindowSurfaceSource androidWindowSurface:
                    desc.Tag = AlcoGPU.SurfaceTag.Android;
                    desc.Handle = (ulong)androidWindowSurface.Window;
                    break;
                default:
                    throw new GraphicsException($"Unsupported surface source {surface?.GetType().Name} for the alco-gpu backend");
            }

            ReadOnlySpan<byte> name = "swapchain_surface\u0000"u8;
            fixed (byte* ptrName = name)
            {
                desc.Name = ptrName;
                AlcoGpuNative.SurfaceCreate(device.Native, in desc, out AlcoGPU.SurfaceHandle handle);
                return handle;
            }
        }
        finally
        {
            GC.KeepAlive(device);
        }
    }

    #region Abstract Implementation

    /// <inheritdoc />
    public override GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer;
    }

    /// <inheritdoc />
    public override bool IsVSyncEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isVSyncEnabled;
        set
        {
            try
            {
                if (_isVSyncEnabled == value) { return; }
                _isVSyncEnabled = value;
                _config.PresentMode = GetPresentMode(value);
                _frameBuffer.UpdateSurfaceConfig(_config);
            }
            finally { GC.KeepAlive(this); }
        }
    }

    protected override GPUDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    /// <inheritdoc />
    public override bool RequestSurfaceTexture()
    {
        try { return _frameBuffer.RequestSurfaceTexture(); }
        finally { GC.KeepAlive(this); }
    }

    /// <inheritdoc />
    public override void Present()
    {
        try { _frameBuffer.Present(); }
        finally { GC.KeepAlive(this); }
    }

    /// <inheritdoc />
    public override void Resize(uint width, uint height)
    {
        try
        {
            _config.Width = width;
            _config.Height = height;
            _frameBuffer.UpdateSurfaceConfig(_config);
        }
        finally { GC.KeepAlive(this); }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            try
            {
                _frameBuffer?.Destroy(disposing);
            }
            finally
            {
                try { _attachmentLayout?.Destroy(disposing); }
                finally
                {
                    AlcoGPU.SurfaceHandle surface = _surface;
                    _surface = AlcoGPU.SurfaceHandle.Null;
                    if (!surface.IsNull) { AlcoGpuNative.SurfaceDestroy(surface); }
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <summary>
    /// Selects the best supported present mode for the requested vsync setting: FIFO for
    /// vsync, otherwise Immediate or Mailbox, falling back to FIFO (with a warning) when
    /// neither is supported.
    /// </summary>
    internal uint GetPresentMode(bool vsync)
    {
        if (!vsync)
        {
            if (IsPresentModeSupported(AlcoGPU.PresentMode.Immediate))
            {
                return AlcoGPU.PresentMode.Immediate;
            }
            else if (IsPresentModeSupported(AlcoGPU.PresentMode.Mailbox))
            {
                return AlcoGPU.PresentMode.Mailbox;
            }
            else
            {
                _device.LogWarning("VSync is off but no supported present mode found, using FIFO");
            }
        }
        return AlcoGPU.PresentMode.Fifo;
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
