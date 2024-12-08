using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// The setting for GPU interface
/// </summary>
public struct GraphicsSetting
{
    /// <summary>
    /// The graphics backend
    /// </summary>
    public GraphicsBackend Backend { get; set; }

    /// <summary>
    /// The GPU device will output debug info if this is true. <br/>
    /// Attention: This will reduce the performance.
    /// </summary>
    public bool DebugInfo { get; set; }

    public PixelFormat PreferredSurfaceFormat { get; set; }
    public PixelFormat PreferredSDRFormat { get; set; }
    public PixelFormat PreferredHDRFormat { get; set; }

    /// <summary>
    /// The format of the swap chain depth stencil buffer. 
    /// <br/>Set to null to disable depth stencil test.
    /// </summary>
    public PixelFormat PreferredDepthStencilFormat { get; set; }

    /// <summary>
    /// The number of render threads.
    /// </summary>
    public int RenderThreadCount { get; set; }


    /// <summary>
    /// The default graphics setting
    /// </summary>
    public static readonly GraphicsSetting Default = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = PixelFormat.BGRA8UnormSrgb,
        PreferredSDRFormat = PixelFormat.RGBA8Unorm,
        PreferredHDRFormat = PixelFormat.RGBA16Float,
        PreferredDepthStencilFormat = PixelFormat.Depth24PlusStencil8,
        RenderThreadCount = Environment.ProcessorCount,
        DebugInfo = false,
    };

    /// <summary>
    /// The debug graphics setting
    /// </summary>
    public static readonly GraphicsSetting Debug = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = PixelFormat.BGRA8UnormSrgb,
        PreferredSDRFormat = PixelFormat.RGBA8Unorm,
        PreferredHDRFormat = PixelFormat.RGBA16Float,
        PreferredDepthStencilFormat = PixelFormat.Depth24PlusStencil8,
        RenderThreadCount = Environment.ProcessorCount,
        DebugInfo = true,
    };


    /// <summary>
    /// The graphics setting for no GPU interface
    /// </summary>
    public static readonly GraphicsSetting NoGPU = new GraphicsSetting
    {
        Backend = GraphicsBackend.None,
        RenderThreadCount = Environment.ProcessorCount,
        DebugInfo = false,
    };
}
