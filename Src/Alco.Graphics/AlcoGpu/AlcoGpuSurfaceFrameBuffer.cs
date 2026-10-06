using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuSurfaceFrameBuffer.</summary>
internal sealed unsafe class AlcoGpuSurfaceFrameBuffer : AlcoGpuFrameBufferBase
{
    #region Properties
    // use list for the abstraction but only one element inside
    private readonly AlcoGpuSwapchain _owner;
    private AlcoGPU.SurfaceHandle _surface;
    private readonly AlcoGPU.RenderPassDesc _descriptor;
    private readonly AlcoGpuSurfaceTexture[] _colorTextures = []; // the surface texture has a per-frame view
    private readonly AlcoGpuTextureViewWrapper[] _colorViewsWrapper = []; // only one element but use list for the abstraction
    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private AlcoGpuTexture? _depthStencilTexture;
    private AlcoGpuTextureView? _depthStencilView;
    private AlcoGpuTextureView? _depthView;
    private AlcoGpuTextureView? _stencilView;

    private readonly PixelFormat[] _colors;
    private readonly PixelFormat? _depth;

    // native memory, need to be manually released
    private AlcoGPU.ColorAttachment* _colorAttachments;
    private AlcoGPU.DepthStencilAttachment* _depthAttachment;

    // dynamic
    private AlcoGPU.SurfaceConfig _config;
    private uint _width;
    private uint _height;
    // Set when a non-size configuration change (e.g. present mode) still needs to reach the
    // native surface; consumed at the next safe reconfigure point in RequestSurfaceTexture.
    private bool _isConfigDirty;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }
    /// <inheritdoc />
    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }
    /// <inheritdoc />
    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    /// <inheritdoc />
    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

    /// <inheritdoc />
    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    /// <inheritdoc />
    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    /// <inheritdoc />
    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViewsWrapper;
    }

    /// <inheritdoc />
    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    /// <inheritdoc />
    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    /// <inheritdoc />
    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            void Release(BaseGPUObject? resource)
            {
                try { resource?.Destroy(disposing); }
                catch (Exception error) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
            }
            for (int i = 0; i < _colorTextures.Length; i++) { Release(_colorTextures[i]); }
            for (int i = 0; i < _colorViewsWrapper.Length; i++) { Release(_colorViewsWrapper[i]); }
            Release(_depthStencilView);
            Release(_depthView);
            Release(_stencilView);
            Release(_depthStencilTexture);

            AlcoGPU.ColorAttachment* colors = _colorAttachments;
            _colorAttachments = null;
            Free(colors);
            AlcoGPU.DepthStencilAttachment* depth = _depthAttachment;
            _depthAttachment = null;
            Free(depth);

            // Acquired texture/view wrappers must be consumed before the owning surface.
            AlcoGPU.SurfaceHandle surface = _surface;
            _surface = AlcoGPU.SurfaceHandle.Null;
            if (!surface.IsNull)
            {
                try { AlcoGpuNative.SurfaceDestroy(surface); }
                catch (Exception error) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
            }
            failure?.Throw();
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <inheritdoc />
    public override AlcoGPU.RenderPassDesc Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    /// <inheritdoc />
    public override ReadOnlySpan<PixelFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    /// <inheritdoc />
    public override PixelFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal AlcoGpuSurfaceFrameBuffer(AlcoGpuSwapchain owner, AlcoGpuDevice device, AlcoGpuAttachmentLayout attachmentLayout, ref AlcoGPU.SurfaceHandle surface, AlcoGPU.SurfaceConfig config) : base(
        new FrameBufferDescriptor(
            attachmentLayout,
            config.Width,
            config.Height,
            "swapchain_frameBuffer"
        )
    )
    {
        try
        {
            try
            {
                _owner = owner;
                Device = device;
                _attachmentLayout = attachmentLayout;
                _surface = surface;
                surface = AlcoGPU.SurfaceHandle.Null;

                // Configure only after ownership transfers into this wrapper.
                AlcoGpuNative.SurfaceConfigure(_surface, in config);
                _config = config;

                _descriptor = new AlcoGPU.RenderPassDesc
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = null,
                    DepthStencil = null,
                };
                _colorAttachments = null;
                _depthAttachment = null;

                _colorTextures = new AlcoGpuSurfaceTexture[1];
                AlcoGpuSurfaceTexture surfaceTexture = new AlcoGpuSurfaceTexture(this, device, _surface, config.Format, config.Usage);
                _colorTextures[0] = surfaceTexture;

                ColorAttachmentInfo colorInfo = attachmentLayout.ColorInfos[0];

                // pointer attention !!
                _colorAttachments = Alloc<AlcoGPU.ColorAttachment>(1);
                AlcoGPU.ColorAttachment attachment = new()
                {
                    View = surfaceTexture.DefaultView,
                    ResolveView = AlcoGPU.TextureViewHandle.Null,
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
                    DepthAttachmentInfo depthInfo = attachmentLayout.DepthInfo.Value;
                    _depthStencilTexture = new AlcoGpuTexture(
                        (AlcoGpuDevice)Device,
                        BuildDepthTextureDescriptor(depthInfo.Format, _width, _height), this);

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
            GC.KeepAlive(attachmentLayout);
        }
    }

    /// <summary>Provides the UpdateSurfaceConfig operation.</summary>
    public void UpdateSurfaceConfig(AlcoGPU.SurfaceConfig config)
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

    /// <summary>Provides the RequestSurfaceTexture operation.</summary>
    public bool RequestSurfaceTexture()
    {
        try
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
                (*_colorAttachments).View = AlcoGPU.TextureViewHandle.Null;
                shouldResize = true;
                isTextureUsable = false;
            }

            // A pending non-size configuration change (e.g. present mode) also needs a native
            // reconfigure, which is only valid while no surface texture is acquired: drop the freshly
            // acquired texture and skip this frame. The next acquire picks up the new configuration.
            if (isTextureUsable && !shouldResize && _isConfigDirty)
            {
                _colorTextures[0].Drop();
                (*_colorAttachments).View = AlcoGPU.TextureViewHandle.Null;
                shouldResize = true;
                isTextureUsable = false;
            }

            if (shouldResize)
            {
                AlcoGPU.SurfaceConfig config = _config;
                AlcoGpuNative.SurfaceConfigure(_surface, in config);
                ResizeDepthTexture();
                _isConfigDirty = false;
            }
            return isTextureUsable;
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <summary>Provides the Present operation.</summary>
    public void Present()
    {
        _colorTextures[0].PresentAndDrop();
    }

    private void ResizeDepthTexture()
    {
        if (!_attachmentLayout.DepthInfo.HasValue)
        {
            return;
        }

        AlcoGpuTexture? texture = null;
        AlcoGpuTextureView? fullView = null;
        AlcoGpuTextureView? depthView = null;
        AlcoGpuTextureView? stencilView = null;
        try
        {
            AlcoGpuDevice device = (AlcoGpuDevice)Device;
            texture = new AlcoGpuTexture(device,
                BuildDepthTextureDescriptor(_attachmentLayout.DepthInfo.Value.Format, _width, _height), this);
            fullView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(texture));
            depthView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.DepthOnly));
            if (PixelFormatUtility.HasStencil(texture.PixelFormat))
            {
                stencilView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(texture, aspect: TextureAspect.StencilOnly));
            }
        }
        catch
        {
            void Release(BaseGPUObject? resource)
            {
                try { resource?.Destroy(false); }
                catch { /* Preserve the replacement creation failure. */ }
            }
            Release(fullView);
            Release(depthView);
            Release(stencilView);
            Release(texture);
            throw;
        }

        AlcoGpuTexture? previousTexture = _depthStencilTexture;
        AlcoGpuTextureView? previousFullView = _depthStencilView;
        AlcoGpuTextureView? previousDepthView = _depthView;
        AlcoGpuTextureView? previousStencilView = _stencilView;
        _depthStencilTexture = texture;
        _depthStencilView = fullView;
        _depthView = depthView;
        _stencilView = stencilView;
        (*_depthAttachment).View = fullView.Native;

        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        void Retire(BaseGPUObject? resource)
        {
            try { resource?.Dispose(); }
            catch (Exception error) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        }
        // Normal replacement remains deferred so already submitted frames keep their resources.
        Retire(previousFullView);
        Retire(previousDepthView);
        Retire(previousStencilView);
        Retire(previousTexture);
        GC.KeepAlive(this);
        failure?.Throw();
    }

    #endregion

    /// <summary>Describes AlcoGpuSurfaceTexture.</summary>
    internal sealed unsafe class AlcoGpuSurfaceTexture : AlcoGpuTextureBase
    {
        #region Properties
        private AlcoGPU.SurfaceHandle _surface;
        // Update every frame
        private AlcoGPU.TextureHandle _texture;
        private AlcoGPU.TextureViewHandle _defaultView;
        //Changed when the surface is resized
        private uint _width;
        private uint _height;

        #endregion

        #region Abstract Implementation

        /// <inheritdoc />
        public override uint Width
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _width;
        }

        /// <inheritdoc />
        public override uint Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _height;
        }

        /// <inheritdoc />
        public override uint Depth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1;
        }

        /// <inheritdoc />
        public override PixelFormat PixelFormat { get; }

        protected override void Dispose(bool disposing)
        {
            Drop();
        }

        #endregion

        #region AlcoGpu Implementation

        /// <inheritdoc />
        public override AlcoGPU.TextureHandle Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        /// <summary>Gets or stores the native ABI value.</summary>
        public AlcoGPU.TextureViewHandle DefaultView
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultView;
        }

        /// <inheritdoc />
        public override uint MipLevelCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1;
        }

        protected override GPUDevice Device { get; }

        private readonly AlcoGpuSurfaceFrameBuffer _owner;

        private static AlcoGPU.TextureInfo GetTextureInfo(AlcoGPU.TextureHandle texture)
        {
            AlcoGPU.TextureInfo info = default;
            AlcoGpuNative.TextureGetInfo(texture, ref info);
            return info;
        }

        internal AlcoGpuSurfaceTexture(
            AlcoGpuSurfaceFrameBuffer owner,
            AlcoGpuDevice device,
            AlcoGPU.SurfaceHandle surface,
            PixelFormat format,
            TextureUsage usage
        ) : base(new TextureDescriptor(TextureDimension.Texture2D, format, 1, 1, 1, 1, usage, 1, "swapchain_texture"))
        {
            try
            {
                Device = device;
                _owner = owner; // Root the surface owner while externally borrowed textures exist.
                _surface = surface;
                PixelFormat = format;
                try
                {
                    uint acquireStatus;
                    AlcoGpuNative.SurfaceGetCurrentTexture(_surface, out _texture, &acquireStatus);
                    if (_texture.IsNull)
                    {
                        throw new GraphicsException($"Initial surface texture acquisition returned status {acquireStatus}.");
                    }
                    AlcoGPU.TextureInfo info = GetTextureInfo(_texture);
                    _width = info.Width;
                    _height = info.Height;
                    AlcoGpuNative.TextureCreateView(_texture, null, out _defaultView);
                }
                catch
                {
                    try { Destroy(false); }
                    catch { /* Preserve the construction failure. */ }
                    throw;
                }
                finally
                {
                }
            }
            finally
            {
                GC.KeepAlive(this);
                GC.KeepAlive(owner);
                GC.KeepAlive(device);
            }
        }

        /// <summary>Presents and then releases the acquired texture and its default view.</summary>
        public void PresentAndDrop()
        {
            try
            {
                try
                {
                    uint presentStatus;
                    AlcoGpuNative.SurfacePresent(_surface, &presentStatus);
                }
                finally
                {
                    Drop();
                }
            }
            finally
            {
                GC.KeepAlive(this);
            }
        }

        /// <summary>Releases the current acquired texture and view without presenting.</summary>
        public void Drop()
        {
            try
            {
                AlcoGPU.TextureViewHandle view = _defaultView;
                _defaultView = AlcoGPU.TextureViewHandle.Null;
                AlcoGPU.TextureHandle texture = _texture;
                _texture = AlcoGPU.TextureHandle.Null;
                try
                {
                    if (!view.IsNull) { AlcoGpuNative.TextureViewDestroy(view); }
                }
                finally
                {
                    try
                    {
                        if (!texture.IsNull) { AlcoGpuNative.TextureRelease(texture); }
                    }
                    finally
                    {
                        GC.KeepAlive(_owner);
                    }
                }
            }
            finally
            {
                GC.KeepAlive(this);
            }
        }

        /// <summary>
        /// Returns true if the newly acquired texture is usable.
        /// </summary>
        public unsafe bool GetNewOutputTexture(AlcoGPU.TextureViewHandle* view, out bool shouldResize)
        {
            try
            {
                if (!_texture.IsNull)
                {
                    //already acquired
                    PresentAndDrop();
                }

                uint acquireStatus;
                AlcoGpuNative.SurfaceGetCurrentTexture(_surface, out _texture, &acquireStatus);
                switch (acquireStatus)
                {
                    case AlcoGPU.AcquireStatus.SuccessOptimal:
                    case AlcoGPU.AcquireStatus.SuccessSuboptimal:
                        // All good
                        break;
                    case AlcoGPU.AcquireStatus.Timeout:
                    case AlcoGPU.AcquireStatus.Outdated:
                    case AlcoGPU.AcquireStatus.Lost:
                        // Skip this frame and reconfigure without leaving a stale view pointer.
                        Drop();
                        *view = AlcoGPU.TextureViewHandle.Null;
                        shouldResize = true;
                        return false;
                    default:
                        // Consume any acquired wrapper before reporting an unexpected status.
                        Drop();
                        *view = AlcoGPU.TextureViewHandle.Null;
                        throw new GraphicsException($"{nameof(AlcoGpuNative.SurfaceGetCurrentTexture)} status = {acquireStatus}");
                }

                try
                {
                    AlcoGPU.TextureInfo info = GetTextureInfo(_texture);
                    _width = info.Width;
                    _height = info.Height;
                    AlcoGpuNative.TextureCreateView(_texture, null, out _defaultView);
                    *view = _defaultView;
                }
                catch
                {
                    try { Drop(); }
                    catch { /* Preserve the acquisition failure. */ }
                    throw;
                }
                finally
                {
                }

                shouldResize = false;
                return true;
            }
            finally
            {
                GC.KeepAlive(this);
            }
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
        private AlcoGPU.TextureViewHandle _view;

        /// <inheritdoc />
        public override AlcoGPU.TextureViewHandle Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture is AlcoGpuSurfaceTexture surface ? surface.DefaultView : _view;
        }

        /// <inheritdoc />
        public override GPUTexture Texture
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        protected override GPUDevice Device { get; }

        /// <summary>Provides the AlcoGpuTextureViewWrapper operation.</summary>
        public AlcoGpuTextureViewWrapper(GPUDevice device, AlcoGpuTextureBase texture, AlcoGPU.TextureViewHandle view) : base(texture.Name)
        {
            Device = device;
            _texture = texture;
            _view = view;
        }

        /// <summary>Provides the UpdateTextureAndView operation.</summary>
        public void UpdateTextureAndView(AlcoGpuTextureBase texture, AlcoGPU.TextureViewHandle view)
        {
            _texture = texture;
            _view = view;
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
