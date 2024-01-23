using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using static Vocore.Graphics.UtilsInterop;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUSurfaceFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;
    // use list for the abstraction but only one element inside
    private readonly WebGPUSurfaceTexture[] _colorTextures;
    private readonly WebGPUTexture? _depthTexture;
    private readonly WebGPURenderPass _renderPass;
    private readonly WGPURenderPassDescriptor _descriptor;
    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    #endregion

    #region Abstract Implementation
    public override string Name { get; }
    public override GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass;
    }
    public override IReadOnlyList<GPUTexture> Colors
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



    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }

        _depthTexture?.Dispose();
        Free(_colorAttachments);
        Free(_depthAttachment);
    }

    #endregion

    #region WebGPU Implementation

    public override WGPURenderPassDescriptor Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }
    internal WebGPUSurfaceFrameBuffer(WebGPURenderPass renderPass, WGPUSurface surface)
    {
        Name = "SwapChain FrameBuffer";
        _renderPass = renderPass;

        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = 1,
            colorAttachments = null,
            depthStencilAttachment = null,
        };

        WebGPUSurfaceTexture surfaceTexture = new WebGPUSurfaceTexture(surface);
        _colorTextures = new WebGPUSurfaceTexture[1];
        _colorTextures[0] = surfaceTexture;

        WGPUColorAttachmentInfo colorInfo = renderPass.WebGPUColorInfos[0];

        _colorAttachments = Alloc<WGPURenderPassColorAttachment>(1);
        _colorAttachments[0] = new WGPURenderPassColorAttachment
        {
            view = surfaceTexture.DefaultView,
            resolveTarget = WGPUTextureView.Null,
            loadOp = WGPULoadOp.Clear,
            storeOp = WGPUStoreOp.Store,
            clearValue = colorInfo.clearColor,
        };
        _descriptor.colorAttachments = _colorAttachments;

        _width = surfaceTexture.Width;
        _height = surfaceTexture.Height;

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);
            WGPUDepthAttachmentInfo depthInfo = renderPass.WebGPUDepthInfo.Value;
            _depthTexture = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(depthInfo.format, _width, _height),
                "Depth Texture");

            _depthAttachment[0] = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthTexture.DefaultView,
                depthLoadOp = WGPULoadOp.Clear,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = depthInfo.clearDepth,
                stencilLoadOp = WGPULoadOp.Clear,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = depthInfo.clearStencil,
            };

            _descriptor.depthStencilAttachment = _depthAttachment;
        }
    }

    internal void SwapBuffers()
    {
        _colorTextures[0].SwapBuffer();
        _colorAttachments[0].view = _colorTextures[0].DefaultView;
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

    internal class WebGPUSurfaceTexture : WebGPUTextureBase
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

        protected override void Dispose(bool disposing)
        {
            wgpuTextureRelease(_texture);
            wgpuTextureDestroy(_texture);
            wgpuTextureViewRelease(_defaultView);
        }

        #endregion

        #region WebGPU Implementation

        public override WGPUTexture Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _texture;
        }

        public override WGPUTextureView DefaultView
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultView;
        }

        public unsafe WebGPUSurfaceTexture(WGPUSurface surface)
        {
            _surface = surface;

            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);


            _defaultView = wgpuTextureCreateView(_texture, null);
        }

        public unsafe void SwapBuffer()
        {
            wgpuSurfacePresent(_surface);
            //release the texture
            wgpuTextureRelease(_texture);
            //get the new texture
            WGPUSurfaceTexture surfaceTexture = default;
            wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
            _texture = surfaceTexture.texture;
            _width = wgpuTextureGetWidth(_texture);
            _height = wgpuTextureGetHeight(_texture);

            //refresh the view
            wgpuTextureViewRelease(_defaultView);
            _defaultView = wgpuTextureCreateView(_texture, null);
        }


        #endregion
    }
}