using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUSurfaceFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;
    // use list for the abstraction but only one element inside
    private readonly WebGPUSurfaceTexture[] _colorTextures;
    private readonly WebGPUTexture? _depthTexture;
    private readonly WebGPURenderPass _renderPass;

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

    public override WGPURenderPassDescriptor Native => throw new NotImplementedException();

    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }


        _depthTexture?.Dispose();
    }

    #endregion

    #region WebGPU Implementation

    internal WebGPUSurfaceFrameBuffer(WebGPURenderPass renderPass, WGPUSurface surface)
    {
        Name = "SwapChain FrameBuffer";
        _renderPass = renderPass;


        WebGPUSurfaceTexture surfaceTexture = new WebGPUSurfaceTexture(surface);
        _colorTextures = new WebGPUSurfaceTexture[1];
        _colorTextures[0] = surfaceTexture;

        _width = surfaceTexture.Width;
        _height = surfaceTexture.Height;

        if (renderPass.Depth.HasValue)
        {
            _depthTexture = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(UtilsWebGPU.PixelFormatToWebGPU(renderPass.Depth.Value.Format), _width, _height),
                "Depth Texture");
        }
    }

    internal void SwapBuffers()
    {
        _colorTextures[0].SwapBuffer();
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
            _width = wgpuTextureGetHeight(_texture);
            _height = wgpuTextureGetWidth(_texture);

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
            _width = wgpuTextureGetHeight(_texture);
            _height = wgpuTextureGetWidth(_texture);

            //refresh the view
            wgpuTextureViewRelease(_defaultView);
            _defaultView = wgpuTextureCreateView(_texture, null);
        }


        #endregion
    }
}