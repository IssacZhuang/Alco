using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    /// <summary>
    /// The rendering setting
    /// </summary>
    public struct RenderingSetting
    {
        /// <summary>
        /// The format of GBffer for deferred rendering. Put null to disable deferred rendering.
        /// <br/> !!! Unsupported currently.
        /// </summary>
        public PixelFormat[]? GBufferColors; 
        /// <summary>
        /// The texture format for the depth buffer. Put null to disable depth stencil test.
        /// </summary>
        public PixelFormat? Depth;

        /// <summary>
        /// True if the GBuffers color passes are required
        /// </summary>
        public readonly bool IsDeferred => GBufferColors != null;

        /// <summary>
        /// The GBuffer passes for 3D deferred rendering
        /// </summary>
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

        /// <summary>
        /// The GBuffer passes for 2D deferred rendering
        /// </summary>
        public static readonly RenderingSetting Deferred2D = new RenderingSetting
        {
            GBufferColors = new PixelFormat[]{
                PixelFormat.RGBA8Unorm, // Albedo
                PixelFormat.RGBA16Float, // World Position
                PixelFormat.RGB10A2Unorm, // Emissive/GI
            },
            Depth = PixelFormat.Depth24PlusStencil8
        };

        /// <summary>
        /// The default rendering setting
        /// </summary>
        public static readonly RenderingSetting Forward = new RenderingSetting
        {
            GBufferColors = null,
            Depth = PixelFormat.Depth24PlusStencil8
        };

        /// <summary>
        /// The rendering setting for no depth stencil test
        /// </summary>
        public static readonly RenderingSetting ForwardNoDepth = new RenderingSetting
        {
            GBufferColors = null,
            Depth = null
        };
    }
}