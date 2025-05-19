using Alco.Graphics;

namespace Alco.Engine;

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

    /// <summary>
    /// The preferred pixel format for swap chain surface presentation
    /// </summary>
    public PixelFormat PreferredSurfaceFormat { get; set; }

    /// <summary>
    /// The preferred pixel format for standard dynamic range (SDR) rendering targets
    /// </summary>
    public PixelFormat PreferredSDRFormat { get; set; }

    /// <summary>
    /// The preferred pixel format for high dynamic range (HDR) rendering targets
    /// </summary>
    public PixelFormat PreferredHDRFormat { get; set; }

    /// <summary>
    /// The format of the swap chain depth stencil buffer. 
    /// <br/>Set to null to disable depth stencil test.
    /// </summary>
    public PixelFormat PreferredDepthStencilFormat { get; set; }

    /// <summary>
    /// Whether to enable shader cache
    /// </summary>
    public bool IsShaderCacheEnabled { get; set; }

    /// <summary>
    /// The path to the shader cache
    /// </summary>
    public string? ShaderCachePath { get; set; }

    /// <summary>
    /// The default graphics setting
    /// </summary>
    public static readonly GraphicsSetting Default = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = PixelFormat.BGRA8Unorm,
        PreferredSDRFormat = PixelFormat.RGBA8Unorm,
        PreferredHDRFormat = PixelFormat.RGBA16Float,
        PreferredDepthStencilFormat = PixelFormat.Depth24PlusStencil8,
        DebugInfo = false,
        IsShaderCacheEnabled = true,
        ShaderCachePath = ".cache/shader",
    };

    /// <summary>
    /// The debug graphics setting
    /// </summary>
    public static readonly GraphicsSetting Debug = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = PixelFormat.BGRA8Unorm,
        PreferredSDRFormat = PixelFormat.RGBA8Unorm,
        PreferredHDRFormat = PixelFormat.RGBA16Float,
        PreferredDepthStencilFormat = PixelFormat.Depth24PlusStencil8,
        DebugInfo = true,
        IsShaderCacheEnabled = true,
        ShaderCachePath = ".cache/shader",
    };


    /// <summary>
    /// The graphics setting for no GPU interface
    /// </summary>
    public static readonly GraphicsSetting NoGPU = new GraphicsSetting
    {
        Backend = GraphicsBackend.None,
        DebugInfo = false,
        IsShaderCacheEnabled = false,
        ShaderCachePath = null,
    };
}
