using System;
using System.Collections.Generic;
using Veldrid;
using Vocore.ImageSharp;

namespace Vocore.Engine
{
    public static class TextureExtension
    {
        public static Texture LoadTexture(this GraphicsDevice device, System.IO.Stream stream)
        {
            return (new ImageSharpTexture(stream)).CreateDeviceTexture(device);
        }
    }
}

