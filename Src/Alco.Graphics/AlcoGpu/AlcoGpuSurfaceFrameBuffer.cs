using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuSurfaceFrameBuffer : AlcoGpuFrameBufferBase
{
    #region Properties
    // use list for the abstraction but only one element inside
    private readonly AlcoHandle _surface;
    private readonly AlcoRenderPassDesc _descriptor;
    private readonly AlcoGpuSurfaceTexture[] _colorTextures; // the surface texture has a per-frame view
    private readonly AlcoGpuTextureViewWrapper[] _colorViewsWrapper; // only one element but use list for the abstraction
    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private AlcoGpuTexture? _depthStencilTexture;
    private AlcoGpuTextureView? _depthStencilView;
    private AlcoGpuTextureView? _depthView;
    private AlcoGpuTextureView? _stencilView;

    private readonly PixelFormat[] _colors;
    private readonly PixelFormat? _depth;

    // native memory, need to be manually released
    private readonly AlcoColorAttachment* _colorAttachments;
    private readonly AlcoDepthStencilAttachment* _depthAttachment;

    // dynamic
    private AlcoSurfaceConfig _config;
    private uint _width;
    private uint _height;
    // Set when a non-size configuration change (e.g. present mode) still needs to reach the
    // native surface; consumed at the next safe reconfigure point in RequestSurfaceTexture.
    private bool _isConfigDirty;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }
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

        // The surface must outlive every acquired texture; dropping it last also
        // destroys any texture the caller forgot to release. Skipped when the
        // native device is already gone: it dropped the surface with itself.
        AlcoGpuDevice device = (AlcoGpuDevice)Device;
        if (device.IsNativeAlive)
        {
            AlcoGpuNative.SurfaceDestroy(device.Native, _surface);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    public override AlcoRenderPassDesc Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    public override ReadOnlySpan<PixelFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    public override PixelFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal AlcoGpuSurfaceFrameBuffer(AlcoGpuDevice device, AlcoGpuAttachmentLayout attachmentLayout, AlcoHandle surface, AlcoSurfaceConfig config) : base(
        new FrameBufferDescriptor(
            attachmentLayout,
            config.Width,
            config.Height,
            "swapchain_frameBuffer"
        )
    )
    {
        Device = device;
        _attachmentLayout = attachmentLayout;
        _surface = surface;

        // configure the surface
        AlcoGpuNative.SurfaceConfigure(device.Native, surface, in config);
        _config = config;

        _descriptor = new AlcoRenderPassDesc
        {
            ColorAttachmentCount = 1,
            ColorAttachments = null,
            DepthStencil = null,
        };
        _colorAttachments = null;
        _depthAttachment = null;

        AlcoGpuSurfaceTexture surfaceTexture = AlcoGpuSurfaceTexture.Create(Device, surface);
        _colorTextures = new AlcoGpuSurfaceTexture[1];
        _colorTextures[0] = surfaceTexture;

        AlcoColorAttachmentInfo colorInfo = attachmentLayout.ColorInfos[0];

        // pointer attention !!
        _colorAttachments = Alloc<AlcoColorAttachment>(1);
        AlcoColorAttachment attachment = new()
        {
            View = surfaceTexture.DefaultView,
            ResolveView = AlcoHandle.Null,
            LoadOp = 0, // load
            StoreOp = 0, // store
        };
        attachment.ClearColor[0] = colorInfo.ClearColor.X;
        attachment.ClearColor[1] = colorInfo.ClearColor.Y;
        attachment.ClearColor[2] = colorInfo.ClearColor.Z;
        attachment.ClearColor[3] = colorInfo.ClearColor.W;
        *_colorAttachments = attachment;

        _colorViewsWrapper = new AlcoGpuTextureViewWrapper[1];
        _colorViewsWrapper[0] = new AlcoGpuTextureViewWrapper(Device, surfaceTexture, surfaceTexture.DefaultView);

        _descriptor.ColorAttachments = _colorAttachments;

        _width = surfaceTexture.Width;
        _height = surfaceTexture.Height;

        if (attachmentLayout.DepthInfo.HasValue)
        {
            AlcoDepthAttachmentInfo depthInfo = attachmentLayout.DepthInfo.Value;
            _depthStencilTexture = new AlcoGpuTexture(
                (AlcoGpuDevice)Device,
                BuildDepthTextureDescriptor(depthInfo.Format, _width, _height));

            _depthStencilView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture));
            _depthView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if (PixelFormatUtility.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }

            _depthAttachment = AllocDepthAttachment(_depthStencilView, depthInfo);
            _descriptor.DepthStencil = _depthAttachment;
        }

        _colors = new PixelFormat[1];
        _colors[0] = colorInfo.Format;

        if (attachmentLayout.DepthInfo.HasValue)
        {
            _depth = attachmentLayout.DepthInfo.Value.Format;
        }
    }

    public void UpdateSurfaceConfig(AlcoSurfaceConfig config)
    {
        // Size changes are reconfigured lazily through the texture-size mismatch check in
        // RequestSurfaceTexture; any other change (e.g. present mode) must be flagged explicitly
        // so the next safe point pushes it to the native surface.
        if (_config.PresentMode != config.PresentMode)
        {
            _isConfigDirty = true;
        }

        _config = config;
        _width = config.Width;
        _height = config.Height;
    }

    public bool RequestSurfaceTexture()
    {
        bool isTextureUsable = _colorTextures[0].GetNewOutputTexture(&(*_colorAttachments).View, out bool shouldResize);

        if (isTextureUsable)
        {
            // Keep the managed color-view wrapper pointing at this frame's view: the
            // surface texture (and its default view) is recreated per frame, so without
            // this the wrapper keeps the first frame's (long-released) view.
            _colorViewsWrapper[0].UpdateTextureAndView(_colorTextures[0], _colorTextures[0].DefaultView);
        }

        // Some GPU drivers (e.g. Intel Vulkan) may return SuccessOptimal
        // with a surface texture at the old resolution after a window resize.
        // Detect this mismatch and force a surface reconfigure.
        if (isTextureUsable && (_colorTextures[0].Width != _config.Width || _colorTextures[0].Height != _config.Height))
        {
            _colorTextures[0].Drop();
            shouldResize = true;
            isTextureUsable = false;
        }

        // A pending non-size configuration change (e.g. present mode) also needs a native
        // reconfigure, which is only valid while no surface texture is acquired: drop the freshly
        // acquired texture and skip this frame. The next acquire picks up the new configuration.
        if (isTextureUsable && !shouldResize && _isConfigDirty)
        {
            _colorTextures[0].Drop();
            shouldResize = true;
            isTextureUsable = false;
        }

        if (shouldResize)
        {
            AlcoSurfaceConfig config = _config;
            AlcoGpuNative.SurfaceConfigure(((AlcoGpuDevice)Device).Native, _surface, in config);
            ResizeDepthTexture();
            _isConfigDirty = false;
        }
        return isTextureUsable;
    }

    public void Present()
    {
        _colorTextures[0].PresentAndDrop();
    }

    private void ResizeDepthTexture()
    {
        if (_attachmentLayout.DepthInfo.HasValue)
        {
            _depthStencilTexture?.Dispose();
            _depthStencilView?.Dispose();
            _depthView?.Dispose();
            _stencilView?.Dispose();
            _depthStencilTexture = new AlcoGpuTexture(
                (AlcoGpuDevice)Device,
                BuildDepthTextureDescriptor(_attachmentLayout.DepthInfo.Value.Format, _width, _height));
            _depthStencilView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture));
            _depthView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if (PixelFormatUtility.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (AlcoGpuTextureView)((AlcoGpuDevice)Device).CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }
            (*_depthAttachment).View = _depthStencilView.Native;
        }
    }

    #endregion

    internal sealed unsafe class AlcoGpuSurfaceTexture : AlcoGpuTextureBase
    {
        #region Properties
        private readonly AlcoHandle _surface;
        // Update every frame
        private AlcoHandle _texture;
        private AlcoHandle _defaultView;
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
            // Surface textures are only released (never destroyed); the surface
            // itself is dropped by the owning AlcoGpuSurfaceFrameBuffer.
            if (!_texture.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
            {
                AlcoGpuNative.TextureRelease(((AlcoGpuDevice)Device).Native, _texture);
                _texture = AlcoHandle.Null;
            }
            if (!_defaultView.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
            {
                AlcoGpuNative.TextureViewDestroy(((AlcoGpuDevice)Device).Native, _defaultView);
                _defaultView = AlcoHandle.Null;
            }
        }

        #endregion

        #region AlcoGpu Implementation

        public override AlcoHandle Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        public AlcoHandle DefaultView
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

        public static AlcoGpuSurfaceTexture Create(GPUDevice device, AlcoHandle surface)
        {
            AlcoGpuDevice alcoDevice = (AlcoGpuDevice)device;
            uint acquireStatus;
            AlcoGpuNative.SurfaceGetCurrentTexture(alcoDevice.Native, surface, out AlcoHandle texture, &acquireStatus);
            return new AlcoGpuSurfaceTexture(alcoDevice, surface, texture, (PixelFormat)GetTextureInfo(alcoDevice, texture).Format, acquireStatus);
        }

        private static AlcoTextureInfo GetTextureInfo(AlcoGpuDevice device, AlcoHandle texture)
        {
            AlcoTextureInfo info = default;
            AlcoGpuNative.TextureGetInfo(device.Native, texture, ref info);
            return info;
        }

        internal AlcoGpuSurfaceTexture(
            AlcoGpuDevice device,
            AlcoHandle surface,
            AlcoHandle texture,
            PixelFormat format,
            uint acquireStatus
        ) : base(
            new TextureDescriptor( //just a dummy descriptor
                TextureDimension.Texture2D,
                format,
                1,
                1,
                1,
                1,
                TextureUsage.None, //the surface texture cannot be sampled
                1,
                "swapchain_texture"
            )
        )
        {
            Device = device;
            _surface = surface;
            PixelFormat = format;

            AlcoTextureInfo info = GetTextureInfo(device, texture);
            _texture = texture;
            _width = info.Width;
            _height = info.Height;

            // Create the default full view.
            AlcoGpuNative.TextureCreateView(device.Native, _texture, null, out _defaultView);
        }

        public void PresentAndDrop()
        {
            uint presentStatus;
            AlcoGpuNative.SurfacePresent(((AlcoGpuDevice)Device).Native, _surface, &presentStatus);
            Drop();
        }

        /// <summary>
        /// Releases the current surface texture without presenting it.
        /// </summary>
        public void Drop()
        {
            if (!_texture.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
            {
                AlcoGpuNative.TextureRelease(((AlcoGpuDevice)Device).Native, _texture);
                _texture = AlcoHandle.Null;
            }
            if (!_defaultView.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
            {
                AlcoGpuNative.TextureViewDestroy(((AlcoGpuDevice)Device).Native, _defaultView);
                _defaultView = AlcoHandle.Null;
            }
        }

        /// <summary>
        /// Returns true if the newly acquired texture is usable.
        /// </summary>
        public unsafe bool GetNewOutputTexture(AlcoHandle* view, out bool shouldResize)
        {
            if (!_texture.IsNull)
            {
                //already acquired
                PresentAndDrop();
            }

            AlcoGpuDevice device = (AlcoGpuDevice)Device;
            uint acquireStatus;
            AlcoGpuNative.SurfaceGetCurrentTexture(device.Native, _surface, out AlcoHandle texture, &acquireStatus);
            switch (acquireStatus)
            {
                case AlcoGpuAbi.AcquireStatus.SuccessOptimal:
                case AlcoGpuAbi.AcquireStatus.SuccessSuboptimal:
                    // All good
                    break;
                case AlcoGpuAbi.AcquireStatus.Timeout:
                case AlcoGpuAbi.AcquireStatus.Outdated:
                case AlcoGpuAbi.AcquireStatus.Lost:
                    // Skip this frame, and re-configure surface.
                    shouldResize = true;
                    return false;
                default:
                    // Fatal error
                    throw new GraphicsException($"{nameof(AlcoGpuNative.SurfaceGetCurrentTexture)} status = {acquireStatus}");
            }

            _texture = texture;
            AlcoTextureInfo info = GetTextureInfo(device, _texture);
            _width = info.Width;
            _height = info.Height;

            //refresh the view
            AlcoGpuNative.TextureCreateView(device.Native, _texture, null, out _defaultView);
            *view = _defaultView;

            shouldResize = false;
            return true;
        }
        #endregion
    }

    /// <summary>
    /// Holds only a reference to a native view handle and takes no control of the lifecycle
    /// of the wrapped native view.
    /// </summary>
    /// <remarks>
    /// Only used by surface frame buffers to prevent creating new managed GPUTextureView objects during the render loop.
    /// </remarks>
    internal sealed class AlcoGpuTextureViewWrapper : AlcoGpuTextureViewBase
    {
        private AlcoGpuTextureBase _texture;
        private AlcoHandle _view;

        public override AlcoHandle Native
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

        public AlcoGpuTextureViewWrapper(GPUDevice device, AlcoGpuTextureBase texture, AlcoHandle view) : base(texture.Name)
        {
            Device = device;
            _texture = texture;
            _view = view;
        }

        public void UpdateTextureAndView(AlcoGpuTextureBase texture, AlcoHandle view)
        {
            _texture = texture;
            _view = view;
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
