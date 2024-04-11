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

    /// <summary>
    /// The frame rate will be locked to the monitor refresh rate if this is true.
    /// </summary>
    public bool VSync { get; set; }

    /// <summary>
    /// The format of the swap chain depth stencil buffer. 
    /// <br/>Set to null to disable depth stencil test.
    /// </summary>
    public PixelFormat? SwapChainDepthFormat { get; set; }


    /// <summary>
    /// The default graphics setting
    /// </summary>
    public static readonly GraphicsSetting Default = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        SwapChainDepthFormat = PixelFormat.Depth24PlusStencil8,
        DebugInfo = false,
        VSync = false
    };

    /// <summary>
    /// The debug graphics setting
    /// </summary>
    public static readonly GraphicsSetting Debug = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        SwapChainDepthFormat = PixelFormat.Depth24PlusStencil8,
        DebugInfo = true,
        VSync = false
    };


    /// <summary>
    /// The graphics setting for no GPU interface
    /// </summary>
    public static readonly GraphicsSetting NoGPU = new GraphicsSetting
    {
        Backend = GraphicsBackend.None,
        DebugInfo = false,
        VSync = false
    };
}
