using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using static Vocore.Graphics.UtilsInterop;
using System.Runtime.InteropServices;

namespace Vocore.Graphics.WebGPU;

internal unsafe sealed class WebGPUSurfaceFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    // use list for the abstraction but only one element inside
    private readonly WGPUSurface _surface;
    private readonly WGPURenderPassDescriptor _descriptor;
    private readonly WebGPUSurfaceTexture[] _colorTextures; // the surface texture has default view
    private readonly WebGPUTextureViewWrapper[] _colorViewsWrapper; // only one element but use list for the abstraction
    private readonly WebGPURenderPass _renderPass;
    private WebGPUTexture? _depthTexture;
    private WGPUTextureView _depthView = WGPUTextureView.Null;
    private readonly WebGPUTextureViewWrapper? _depthViewWrapper;

    private readonly WGPUTextureFormat[] _colors;
    private readonly WGPUTextureFormat? _depth;


    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    // dynamic
    private WGPUSurfaceConfiguration _config;
    private bool _isResized;
    private uint _width;
    private uint _height;

    #endregion

    #region Abstract Implementation
    public override string Name { get; }
    protected override WebGPUDevice Device { get; }
    public override GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass;
    }
    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override GPUTexture? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthTexture;
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

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthViewWrapper;
    }

    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }

        _depthTexture?.Dispose();

        if(_depthView != WGPUTextureView.Null)
        {
            wgpuTextureViewRelease(_depthView);
        }
        
        Free(_colorAttachments);
        if (_depthAttachment != null)
        {
            Free(_depthAttachment);
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

    internal WebGPUSurfaceFrameBuffer(WebGPUDevice device, WebGPURenderPass renderPass, WGPUSurface surface, WGPUSurfaceConfiguration config)
    {
        Device = device;
        Name = "swapchain_frameBuffer";
        _renderPass = renderPass;

        // configure the surface
        wgpuSurfaceConfigure(surface, &config);
        _surface = surface;
        _config = config;
        _isResized = false;

        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = 1,
            colorAttachments = null,
            depthStencilAttachment = null,
        };
        // reset the pointer
        _colorAttachments = null;
        _depthAttachment = null;

        WebGPUSurfaceTexture surfaceTexture = new WebGPUSurfaceTexture(Device, surface);
        _colorTextures = new WebGPUSurfaceTexture[1];
        _colorTextures[0] = surfaceTexture;

        WGPUColorAttachmentInfo colorInfo = renderPass.WebGPUColorInfos[0];

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

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);
            WGPUDepthAttachmentInfo depthInfo = renderPass.WebGPUDepthInfo.Value;
            _depthTexture = new WebGPUTexture(
                Device,
                BuildTextureDescriptor(depthInfo.format, _width, _height),
                "Depth Texture");

            _depthView = wgpuTextureCreateView(_depthTexture.Native, null);

            // pointer attention !!
            *_depthAttachment = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthView,
                depthLoadOp = WGPULoadOp.Load,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = depthInfo.clearDepth,
                stencilLoadOp = WGPULoadOp.Load,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = depthInfo.clearStencil,
            };

            _depthViewWrapper = new WebGPUTextureViewWrapper(Device, _depthTexture, _depthView);

            _descriptor.depthStencilAttachment = _depthAttachment;
        }

        _colors = new WGPUTextureFormat[1];
        _colors[0] = colorInfo.format;

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depth = renderPass.WebGPUDepthInfo.Value.format;
        }
    }

    public void UpdateSurfaceConfig(WGPUSurfaceConfiguration config)
    {
        _config = config;
        _width = config.width;
        _height = config.height;
        _isResized = true;
    }

    public void SwapBuffers()
    {
        _colorTextures[0].PresentAnDrop();
        if (_isResized)
        {
            
            WGPUSurfaceConfiguration config = _config;
            wgpuSurfaceConfigure(_surface, &config);
            ResizeDepthTexture();
            _isResized = false;
        }
        _colorTextures[0].GetNewOutputTexture(ref (*_colorAttachments).view);
    }

    private void ResizeDepthTexture()
    {
        if (_renderPass.WebGPUDepthInfo.HasValue)
        {
            //_depthTexture?.Dispose();
            _depthTexture?.Destroy();
            wgpuTextureViewRelease(_depthView);
            _depthTexture = new WebGPUTexture(
                Device,
                BuildTextureDescriptor(_renderPass.WebGPUDepthInfo.Value.format, _width, _height),
                "Depth Texture");
            _depthView = wgpuTextureCreateView(_depthTexture.Native, null);
            (*_depthAttachment).view = _depthView;
        }
    }


    private static WGPUTextureDescriptor BuildTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new WGPUTextureDescriptor
        {
            // the texture could be used as a render target, copied from, or sampled from a shader
            usage = WGPUTextureUsage.RenderAttachment | WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopySrc,
            dimension = WGPUTextureDimension._2D,
            size = new WGPUExtent3D
            {
                width = width,
                height = height,
                depthOrArrayLayers = 1,
            },
            format = format,
            mipLevelCount = 1,
            sampleCount = 1,
            viewFormatCount = 0,
        };
    }

    #endregion

    internal sealed class WebGPUSurfaceTexture : WebGPUTextureBase
    {
        #region Properties
        private const string NAME = "WebGPU Surface Texture";
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

        public override string Name
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => NAME;
        }

        public override PixelFormat PixelFormat { get; }

        protected override void Dispose(bool disposing)
        {
            //wgpuTextureDestroy(_texture);
            wgpuTextureRelease(_texture);
            wgpuTextureViewRelease(_defaultView);
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



        public unsafe WebGPUSurfaceTexture(WebGPUDevice device, WGPUSurface surface)
        {
            Device = device;
            _surface = surface;

            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);

            _defaultView = wgpuTextureCreateView(_texture, null);

            PixelFormat = UtilsWebGPU.PixelFormatToAbstract(wgpuTextureGetFormat(_texture));
        }

        public unsafe void PresentAnDrop()
        {
            wgpuSurfacePresent(_surface);
            wgpuTextureRelease(_texture);
            wgpuTextureViewRelease(_defaultView);
        }

        public unsafe void GetNewOutputTexture(ref WGPUTextureView view)
        {
            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);

            //refresh the view
            
            _defaultView = wgpuTextureCreateView(_texture, null);
            view = _defaultView;
        }
        #endregion
    }
}