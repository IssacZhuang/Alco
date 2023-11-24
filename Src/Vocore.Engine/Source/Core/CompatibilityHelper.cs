using System;
using System.Runtime.InteropServices;
using Veldrid;


namespace Vocore.Engine
{
    public static class CompatibilityHelper
    {
        public static GraphicsBackend GetPlatformDefaultBackend()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GraphicsBackend.Direct3D11;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return GraphicsDevice.IsBackendSupported(GraphicsBackend.Metal)
                    ? GraphicsBackend.Metal
                    : GraphicsBackend.OpenGL;
            }
            else
            {
                return GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan)
                    ? GraphicsBackend.Vulkan
                    : GraphicsBackend.OpenGL;
            }
        }


        public static PixelFormat GetPlatformDepthTestingFormat()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return PixelFormat.D24_UNorm_S8_UInt;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return PixelFormat.D32_Float_S8_UInt;
            }
            else
            {
                return PixelFormat.D24_UNorm_S8_UInt;
            }
        }
    }
}