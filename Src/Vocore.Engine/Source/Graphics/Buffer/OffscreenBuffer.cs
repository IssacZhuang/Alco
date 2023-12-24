using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore
{
    public class OffscreenBuffer : IDisposable
    {
        private Framebuffer _framebuffer;
        private ResourceFactory _factory;
        private uint _width;
        private uint _height;
        private PixelFormat[] _colors;
        private PixelFormat? _depth;
        public Framebuffer Framebuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _framebuffer;
        }

        public OffscreenBuffer(GraphicsDevice device, uint width, uint height, PixelFormat[] colors, PixelFormat? depth = null)
        {
            _factory = device.ResourceFactory;

            _width = width;
            _height = height;
            _colors = colors;
            _depth = depth;

            _framebuffer = CreateFramebuffer();
        }

        private Framebuffer CreateFramebuffer()
        {
            FramebufferDescription desc = new FramebufferDescription();

            if (_depth.HasValue)
            {
                Texture depthTexture = _factory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, _depth.Value, TextureUsage.DepthStencil));
                desc.DepthTarget = new FramebufferAttachmentDescription(depthTexture, 0);
            }

            FramebufferAttachmentDescription[] colorAttachments = new FramebufferAttachmentDescription[_colors.Length];
            for (int i = 0; i < _colors.Length; i++)
            {
                Texture colorTexture = _factory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, _colors[i], TextureUsage.RenderTarget | TextureUsage.Sampled));
                colorAttachments[i] = new FramebufferAttachmentDescription(colorTexture, 0);
            }
            desc.ColorTargets = colorAttachments;

            return _factory.CreateFramebuffer(desc);
        }

        public void Resize(uint width, uint height)
        {
            _width = width;
            _height = height;

            _framebuffer.Dispose();
            _framebuffer = CreateFramebuffer();
        }

        public void Dispose()
        {
            _framebuffer.Dispose();
        }
    }
}