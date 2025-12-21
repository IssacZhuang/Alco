using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using static Alco.Graphics.InteropUtility;
using System.Runtime.InteropServices;

namespace Alco.Graphics.WebGPU;

internal unsafe sealed class WebGPUSurfaceFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    // use list for the abstraction but only one element inside
    private readonly WGPUSurface _surface;
    private readonly WGPURenderPassDescriptor _descriptor;
    private readonly WebGPUSurfaceTexture[] _colorTextures; // the surface texture has default view
    private readonly WebGPUTextureViewWrapper[] _colorViewsWrapper; // only one element but use list for the abstraction
    private readonly WebGPUAttachmentLayout _attachmentLayout;
    private WebGPUTexture? _depthStencilTexture;
    private WebGPUTextureView? _depthStencilView;
    private WebGPUTextureView? _depthView;
    private WebGPUTextureView? _stencilView;

    private readonly WGPUTextureFormat[] _colors;
    private readonly WGPUTextureFormat? _depth;


    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    // dynamic
    private WGPUSurfaceConfiguration _config;
    private uint _width;
    private uint _height;

    #endregion

    #region Abstract Implementation
    protected override WebGPUDevice Device { get; }
    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }
    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViewsWrapper;
    }

    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }

        Free(_colorAttachments);
        if (_depthAttachment != null)
        {
            Free(_depthAttachment);
        }

        if (disposing)
        {
            _depthStencilTexture?.Dispose();
            _depthStencilView?.Dispose();
            _depthView?.Dispose();
            _stencilView?.Dispose();
        }

    }

    #endregion

    #region WebGPU Implementation

    public override WGPURenderPassDescriptor Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    public override ReadOnlySpan<WGPUTextureFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    public override WGPUTextureFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal WebGPUSurfaceFrameBuffer(WebGPUDevice device, WebGPUAttachmentLayout attachmentLayout, WGPUSurface surface, WGPUSurfaceConfiguration config) : base(
        new FrameBufferDescriptor(
            attachmentLayout,
            config.width,
            config.height,
            "swapchain_frameBuffer"

        )
    )
    {
        Device = device;
        _attachmentLayout = attachmentLayout;

        // configure the surface
        wgpuSurfaceConfigure(surface, &config);
        _surface = surface;
        _config = config;

        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = 1,
            colorAttachments = null,
            depthStencilAttachment = null,
        };
        // reset the pointer
        _colorAttachments = null;
        _depthAttachment = null;

        WebGPUSurfaceTexture surfaceTexture = WebGPUSurfaceTexture.Create(Device, surface);
        _colorTextures = new WebGPUSurfaceTexture[1];
        _colorTextures[0] = surfaceTexture;

        WGPUColorAttachmentInfo colorInfo = attachmentLayout.WebGPUColorInfos[0];

        // pointer attention !!
        _colorAttachments = Alloc<WGPURenderPassColorAttachment>(1);
        *_colorAttachments = new WGPURenderPassColorAttachment
        {
            view = surfaceTexture.DefaultView,
            resolveTarget = WGPUTextureView.Null,
            loadOp = WGPULoadOp.Load,
            storeOp = WGPUStoreOp.Store,
            clearValue = colorInfo.clearColor,
            depthSlice = WGPU_DEPTH_SLICE_UNDEFINED,
        };

        _colorViewsWrapper = new WebGPUTextureViewWrapper[1];
        _colorViewsWrapper[0] = new WebGPUTextureViewWrapper(Device, surfaceTexture, surfaceTexture.DefaultView);

        _descriptor.colorAttachments = _colorAttachments;

        _width = surfaceTexture.Width;
        _height = surfaceTexture.Height;

        if (attachmentLayout.WebGPUDepthInfo.HasValue)
        {
            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);
            WGPUDepthAttachmentInfo depthInfo = attachmentLayout.WebGPUDepthInfo.Value;
            _depthStencilTexture = new WebGPUTexture(
                Device,
                BuildDepthTextureDescriptor(depthInfo.format, _width, _height)
                );

            _depthStencilView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture));
            _depthView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if(PixelFormatUtility.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }

            _descriptor.depthStencilAttachment = _depthAttachment;
        }

        _colors = new WGPUTextureFormat[1];
        _colors[0] = colorInfo.format;

        if (attachmentLayout.WebGPUDepthInfo.HasValue)
        {
            _depth = attachmentLayout.WebGPUDepthInfo.Value.format;
        }
    }

    public void UpdateSurfaceConfig(WGPUSurfaceConfiguration config)
    {
        _config = config;
        _width = config.width;
        _height = config.height;
    }

    public bool RequestSurfaceTexture()
    {
        bool isTextureUsable = _colorTextures[0].GetNewOutputTexture(ref (*_colorAttachments).view, out bool shouldResize);
        if (shouldResize)
        {
            WGPUSurfaceConfiguration config = _config;
            wgpuSurfaceConfigure(_surface, &config);
            ResizeDepthTexture();
        }
        return isTextureUsable;
    }

    public void Present()
    {
        _colorTextures[0].PresentAndDrop();
    }

    private void ResizeDepthTexture()
    {
        if (_attachmentLayout.WebGPUDepthInfo.HasValue)
        {
            //_depthTexture?.Dispose();
            _depthStencilTexture?.Dispose();
            _depthStencilView?.Dispose();
            _depthView?.Dispose();
            _stencilView?.Dispose();
            _depthStencilTexture = new WebGPUTexture(
                Device,
                BuildDepthTextureDescriptor(_attachmentLayout.WebGPUDepthInfo.Value.format, _width, _height)
                );
            _depthStencilView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture));
            _depthView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if(PixelFormatUtility.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (WebGPUTextureView)Device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }
            (*_depthAttachment).view = _depthStencilView.Native;
        }
    }


    #endregion

    internal sealed class WebGPUSurfaceTexture : WebGPUTextureBase
    {
        #region Properties
        private readonly WGPUSurface _surface;
        // Update every frame
        private WGPUTexture _texture;
        private WGPUTextureView _defaultView;
        //Changed when the surface is resized
        private uint _width;
        private uint _height;

        #endregion

        #region Abstract Implementation

        public override uint Width
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _width;
        }

        public override uint Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _height;
        }

        public override uint Depth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1;
        }

        public override PixelFormat PixelFormat { get; }

        protected override void Dispose(bool disposing)
        {
            //wgpuTextureDestroy(_texture);
            if (_texture != WGPUTexture.Null)
            {
                wgpuTextureRelease(_texture);
            }
            if (_defaultView != WGPUTextureView.Null)
            {
                wgpuTextureViewRelease(_defaultView);
            }
            wgpuSurfaceRelease(_surface);
        }

        #endregion

        #region WebGPU Implementation

        public override WGPUTexture Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        public WGPUTextureView DefaultView
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultView;
        }

        public override uint MipLevelCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1;
        }

        protected override GPUDevice Device { get; }

        public static WebGPUSurfaceTexture Create(WebGPUDevice device, WGPUSurface surface)
        {
            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(surface, &surfaceTexture);
            return new WebGPUSurfaceTexture(device, surface, surfaceTexture);
        }

        internal unsafe WebGPUSurfaceTexture(
            WebGPUDevice device,
            WGPUSurface surface,
            WGPUSurfaceTexture surfaceTexture
        ) : base(
            new TextureDescriptor(//just a dummy descriptor
                TextureDimension.Texture2D,
                UtilsWebGPU.PixelFormatToAbstract(wgpuTextureGetFormat(surfaceTexture.texture)),
                wgpuTextureGetWidth(surfaceTexture.texture),
                wgpuTextureGetHeight(surfaceTexture.texture),
                1,
                1,
                TextureUsage.None,//the surface texture cannot be sampled
                1,
                "swapchain_texture"
            )
        )
        {
            Device = device;
            _surface = surface;

            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);

            _defaultView = wgpuTextureCreateView(_texture, null);

            PixelFormat = UtilsWebGPU.PixelFormatToAbstract(wgpuTextureGetFormat(_texture));
        }

        public unsafe void PresentAndDrop()
        {
            wgpuSurfacePresent(_surface);
            wgpuTextureRelease(_texture);
            wgpuTextureViewRelease(_defaultView);
            _defaultView = WGPUTextureView.Null;
            _texture = WGPUTexture.Null;
        }

        //return true if the texture is usable
        public unsafe bool GetNewOutputTexture(ref WGPUTextureView view, out bool shouldResize)
        {
            if (_texture != WGPUTexture.Null)
            {
                //already acquired
                PresentAndDrop();
            }


            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
            WGPUSurfaceGetCurrentTextureStatus status = surfaceTexture.status;
            switch (surfaceTexture.status)
            {
                case WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal:
                case WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal:
                    // All good
                    break;
                case WGPUSurfaceGetCurrentTextureStatus.Timeout:
                case WGPUSurfaceGetCurrentTextureStatus.Outdated:
                case WGPUSurfaceGetCurrentTextureStatus.Lost:
                    // Skip this frame, and re-configure surface.
                    shouldResize = true;
                    return false;
                case WGPUSurfaceGetCurrentTextureStatus.OutOfMemory:
                case WGPUSurfaceGetCurrentTextureStatus.DeviceLost:
                    // Fatal error
                    throw new GraphicsException($"{nameof(wgpuSurfaceGetCurrentTexture)} status = {surfaceTexture.status}");
            }

            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);

            //refresh the view
            
            _defaultView = wgpuTextureCreateView(_texture, null);
            view = _defaultView;

            shouldResize = false;
            return true;
        }
        #endregion
    }

    //on a reference to WGPUTextureView
    //no control of the lifecycle of the wgpuTextureView
    //only used by surface framebuffers to prevent new managed GPUTextureView object at render loop
    internal sealed class WebGPUTextureViewWrapper : WebGPUTextureViewBase
    {
        private WebGPUTextureBase _texture;
        private WGPUTextureView _view;

        public override WGPUTextureView Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _view;
        }

        public override GPUTexture Texture
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        protected override GPUDevice Device { get; }


        public WebGPUTextureViewWrapper(WebGPUDevice device, WebGPUTextureBase texture, WGPUTextureView view) : base(texture.Name)
        {
            Device = device;
            _texture = texture;
            _view = view;
        }

        public void UpdateTextureAndView(WebGPUTextureBase texture, WGPUTextureView view)
        {
            _texture = texture;
            _view = view;
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}