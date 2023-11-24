using System;
using System.Collections.Generic;
using System.IO;
using Veldrid;
using Vocore.ImageSharp;

namespace Vocore.Engine
{
    public static class TextureExtension
    {
        public static Texture LoadTexture(this GraphicsDevice device, Stream stream)
        {
            return (new ImageSharpTexture(stream, true, true)).CreateDeviceTexture(device);
        }
    }
}

