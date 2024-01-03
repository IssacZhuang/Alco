using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct RenderingSetting
    {
        public PixelFormat[]? GBufferColors;
        public PixelFormat Depth;

        public readonly bool IsDeferred => GBufferColors != null;

        // Currently not in used because not PBR rendering in this engine
        public static readonly RenderingSetting Deferred3D = new RenderingSetting
        {
            GBufferColors = new PixelFormat[]{
                PixelFormat.R8_G8_B8_A8_UNorm, // Albedo
                PixelFormat.R8_G8_B8_A8_UNorm, // Specular(RGB) Occlusion(A)
                PixelFormat.R16_G16_B16_A16_Float, // Normal(RGB) Smoothness(A)
                PixelFormat.R32_G32_B32_A32_Float, // World Position
                PixelFormat.R10_G10_B10_A2_UNorm, // Emissive/GI
            },
            Depth = CompatibilityHelper.GetPlatformDepthTestingFormat()
        };

        public static readonly RenderingSetting Deferred2D = new RenderingSetting
        {
            GBufferColors = new PixelFormat[]{
                PixelFormat.R8_G8_B8_A8_UNorm, // Albedo
                PixelFormat.R32_G32_B32_A32_Float, // World Position
                PixelFormat.R10_G10_B10_A2_UNorm, // Emissive/GI
            },
            Depth = CompatibilityHelper.GetPlatformDepthTestingFormat()
        };

        public static readonly RenderingSetting Forward = new RenderingSetting
        {
            GBufferColors = null,
            Depth = CompatibilityHelper.GetPlatformDepthTestingFormat()
        };
    }
}