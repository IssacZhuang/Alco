using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using System.Runtime.InteropServices;

namespace Alco.Graphics.WebGPU;

internal unsafe sealed class WebGPUSwapchain : GPUSwapchain
{
    private readonly WebGPUDevice _device;
    private readonly WebGPURenderPass _renderPass;
    private readonly WGPUSurface _surface;
    private readonly WebGPUSurfaceFrameBuffer _frameBuffer;

    private readonly WGPUTextureFormat _surfaceFormat;
    private readonly WGPUTextureFormat? _depthFormat;
    private readonly WGPUTextureFormat[] _supportedSurfaceFormats;
    private readonly WGPUPresentMode[] _supportedPresentModes;

    private WGPUSurfaceConfiguration _config;
    private bool _isVSyncEnabled;
    //for custom swapchain
    internal WebGPUSwapchain(WebGPUDevice device, in SwapchainDescriptor descriptor): base(descriptor)
    {
        _device = device;

        WGPUAdapter adapter = device.Adapter;
        _surface = device.Instance.CreateSurface(descriptor.SurfaceSource);

        // check compatibility

        WGPUSurfaceCapabilities surfaceCapabilities = default;
        wgpuSurfaceGetCapabilities(_surface, adapter, &surfaceCapabilities);
        // get supported formats
        _supportedPresentModes = new WGPUPresentMode[surfaceCapabilities.presentModeCount];
        for (uint i = 0; i < surfaceCapabilities.presentModeCount; i++)
        {
            _supportedPresentModes[i] = surfaceCapabilities.presentModes[i];
        }
        //get supported formats
        _supportedSurfaceFormats = new WGPUTextureFormat[surfaceCapabilities.formatCount];
        for (uint i = 0; i < surfaceCapabilities.formatCount; i++)
        {
            _supportedSurfaceFormats[i] = surfaceCapabilities.formats[i];
        }
        

        _surfaceFormat = UtilsWebGPU.PixelFormatToWebGPU(descriptor.ColorFormat);
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
            WGPUTextureFormat oldFormat = _surfaceFormat;
            _surfaceFormat = _supportedSurfaceFormats[0];
            _device.LogInfo($"Surface format {oldFormat} is not supported, using {_surfaceFormat} instead");
        }

        wgpuSurfaceCapabilitiesFreeMembers(surfaceCapabilities);

        //create render pass
        DepthAttachment? depth = null;
        if (descriptor.DepthFormat.HasValue)
        {
            _depthFormat = UtilsWebGPU.PixelFormatToWebGPU(descriptor.DepthFormat.Value);
            depth = new DepthAttachment()
            {
                Format = descriptor.DepthFormat.Value,
                ClearDepth = 1.0f,
                ClearStencil = 0,
            };
        }

        RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor(
            new ColorAttachment[]
            {
                new ColorAttachment()
                {
                    Format = UtilsWebGPU.PixelFormatToAbstract(_surfaceFormat),
                    ClearColor = descriptor.ClearColor,
                },
            },
            depth,
            "surface_render_pass"
        );

        _renderPass = new WebGPURenderPass(device, renderPassDescriptor);


        _config.device = device.Native;
        _config.format = _renderPass.WebGPUColorInfos[0].format;
        _config.usage = WGPUTextureUsage.RenderAttachment;
        _config.presentMode = GetPresentMode(descriptor.IsVSyncEnabled);
        _config.alphaMode = WGPUCompositeAlphaMode.Auto;

        _config.width = descriptor.Width;
        _config.height = descriptor.Height;

        ///the life cycle of the _surface will be managed by the WebGPUSurfaceTexture
        /// because it must be released after the native SurfaceTexture is released
        _frameBuffer = new WebGPUSurfaceFrameBuffer(_device, _renderPass, _surface, _config);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _isVSyncEnabled = value;
            _config.presentMode = GetPresentMode(value);
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
        _config.width = width;
        _config.height = height;
        _frameBuffer.UpdateSurfaceConfig(_config);
    }

    protected override void Dispose(bool disposing)
    {
        _frameBuffer.Dispose();
    }

    #endregion

    #region WebGPU Implementation

    public WGPUPresentMode GetPresentMode(bool vsync)
    {
        if (!vsync)
        {
            if (IsPresentModeSupported(WGPUPresentMode.Immediate))
            {
                return WGPUPresentMode.Immediate;
            }
            else if (IsPresentModeSupported(WGPUPresentMode.Mailbox))
            {
                return WGPUPresentMode.Mailbox;
            }
            else
            {
                _device.LogWarning("VSync is off but no supported present mode found, using FIFO");
            }
        }
        return WGPUPresentMode.Fifo;
    }

    private bool IsPresentModeSupported(WGPUPresentMode mode)
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