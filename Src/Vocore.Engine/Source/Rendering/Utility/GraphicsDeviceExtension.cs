using System;
using Veldrid;

namespace Vocore.Engine
{
    public static class GraphicsDeviceExtension
    {
        public static Framebuffer CreateFramebufferSingleTarget(this ResourceFactory factory, uint width, uint height, bool isHDR = false)
        {
            Texture depth = factory.CreateTexture(new TextureDescription
            {
                Width = width,
                Height = height,
                Type = TextureType.Texture2D,
                Usage = TextureUsage.DepthStencil,
                Format = CompatibilityHelper.GetPlatformDepthTestingFormat()
            });



            Texture color = factory.CreateTexture(new TextureDescription
            {
                Width = width,
                Height = height,
                Type = TextureType.Texture2D,
                Usage = TextureUsage.RenderTarget,
                Format = CompatibilityHelper.GetPlatformColorFormat(isHDR)
            });

            return factory.CreateFramebuffer(new FramebufferDescription(
                depth,
                new Texture[] { color }
            ));
        }
    }
}