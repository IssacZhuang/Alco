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
    /// The preferred swapchain presentation format. <see cref="Format.Undefined"/> lets each backend
    /// pick its default non-sRGB SDR format (<see cref="Format.Bgra8Unorm"/>, or
    /// <see cref="Format.Rgba8Unorm"/> on Vulkan surfaces that lack BGRA8).
    /// </summary>
    public Format PreferredSurfaceFormat { get; set; }

    /// <summary>
    /// The pixel format of the pipeline's main HDR scene target. Does not affect the swapchain,
    /// whose format is configured by <see cref="PreferredSurfaceFormat"/>.
    /// </summary>
    public Format PreferredHDRFormat { get; set; }

    /// <summary>
    /// The depth-stencil format of forward-style scene targets.
    /// </summary>
    public Format PreferredDepthStencilFormat { get; set; }

    /// <summary>
    /// Initial GPU buffer heap pool size in bytes reserved by the rendering system.
    /// </summary>
    public ulong BufferHeapByteCount { get; set; }

    /// <summary>
    /// Initial GPU texture heap pool size in bytes reserved by the rendering system.
    /// </summary>
    public ulong TextureHeapByteCount { get; set; }

    /// <summary>
    /// Whether to enable shader cache
    /// </summary>
    public bool IsShaderCacheEnabled { get; set; }

    /// <summary>
    /// The path to the shader cache
    /// </summary>
    public string? ShaderCachePath { get; set; }

    /// <summary>
    /// Whether to enable the persistent font atlas cache. Cached atlases skip glyph
    /// rasterization on startup; the cache is written asynchronously after the first load.
    /// </summary>
    public bool IsFontCacheEnabled { get; set; }

    /// <summary>
    /// The path to the font atlas cache
    /// </summary>
    public string? FontCachePath { get; set; }

    /// <summary>
    /// Whether to enable the persistent GPU pipeline cache.
    /// </summary>
    public bool IsPipelineCacheEnabled { get; set; }

    /// <summary>
    /// The path to the GPU pipeline cache directory.
    /// </summary>
    public string? PipelineCachePath { get; set; }

    /// <summary>
    /// The default graphics setting
    /// </summary>
    public static readonly GraphicsSetting Default = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = Format.Undefined,
        PreferredHDRFormat = Format.Rgba16Float,
        PreferredDepthStencilFormat = Format.D24UnormS8Uint,
        BufferHeapByteCount = 64ul * 1024 * 1024,
        TextureHeapByteCount = 64ul * 1024 * 1024,
        DebugInfo = false,
        IsShaderCacheEnabled = true,
        ShaderCachePath = ".cache/shader",
        IsFontCacheEnabled = true,
        FontCachePath = ".cache/font",
        IsPipelineCacheEnabled = true,
        PipelineCachePath = ".cache/pipeline",
    };

    /// <summary>
    /// The debug graphics setting
    /// </summary>
    public static readonly GraphicsSetting Debug = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        PreferredSurfaceFormat = Format.Undefined,
        PreferredHDRFormat = Format.Rgba16Float,
        PreferredDepthStencilFormat = Format.D24UnormS8Uint,
        BufferHeapByteCount = 64ul * 1024 * 1024,
        TextureHeapByteCount = 64ul * 1024 * 1024,
        DebugInfo = true,
        IsShaderCacheEnabled = true,
        ShaderCachePath = ".cache/shader",
        IsFontCacheEnabled = true,
        FontCachePath = ".cache/font",
        IsPipelineCacheEnabled = true,
        PipelineCachePath = ".cache/pipeline",
    };


    /// <summary>
    /// The graphics setting for no GPU interface
    /// </summary>
    public static readonly GraphicsSetting NoGPU = new GraphicsSetting
    {
        Backend = GraphicsBackend.NoGPU,
        PreferredSurfaceFormat = Format.Undefined,
        PreferredHDRFormat = Format.Rgba16Float,
        PreferredDepthStencilFormat = Format.D24UnormS8Uint,
        BufferHeapByteCount = 1ul * 1024 * 1024,
        TextureHeapByteCount = 4ul * 1024 * 1024,
        DebugInfo = false,
        IsShaderCacheEnabled = false,
        ShaderCachePath = null,
        IsFontCacheEnabled = false,
        FontCachePath = null,
        IsPipelineCacheEnabled = false,
        PipelineCachePath = null,
    };
}
