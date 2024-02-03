using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public struct RenderingSetting
    {
        public PixelFormat[]? GBufferColors;
        public PixelFormat? Depth;

        public readonly bool IsDeferred => GBufferColors != null;

        // Currently not in used because not PBR rendering in this engine
        public static readonly RenderingSetting Deferred3D = new RenderingSetting
        {
            GBufferColors = new PixelFormat[]{
                PixelFormat.RGBA8Unorm, // Albedo
                PixelFormat.RGBA8Snorm, // Specular(RGB) Occlusion(A)
                PixelFormat.RGBA16Float, // Normal(RGB) Smoothness(A)
                PixelFormat.RGBA16Float, // World Position
                PixelFormat.RGB10A2Unorm, // Emissive/GI
            },
            Depth = PixelFormat.Depth24PlusStencil8
        };

        public static readonly RenderingSetting Deferred2D = new RenderingSetting
        {
            GBufferColors = new PixelFormat[]{
                PixelFormat.RGBA8Unorm, // Albedo
                PixelFormat.RGBA16Float, // World Position
                PixelFormat.RGB10A2Unorm, // Emissive/GI
            },
            Depth = PixelFormat.Depth24PlusStencil8
        };

        public static readonly RenderingSetting Forward = new RenderingSetting
        {
            GBufferColors = null,
            Depth = PixelFormat.Depth24PlusStencil8
        };

        public static readonly RenderingSetting ForwardNoDepth = new RenderingSetting
        {
            GBufferColors = null,
            Depth = null
        };
    }
}