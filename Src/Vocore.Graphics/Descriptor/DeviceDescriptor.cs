using System.Numerics;

namespace Vocore.Graphics;

/// <summary>
/// The descriptor of the GPU device
/// </summary>
public struct DeviceDescriptor
{
    public DeviceDescriptor(
        IGPULoopProvider loopProvider,
        GraphicsBackend backend = GraphicsBackend.Auto,
        PixelFormat preferredSurfaceFormat = PixelFormat.BGRA8UnormSrgb,
        bool debug = false,        
        uint pushConstantsSize = 128,
        string name = "Vocore Graphics Device"
    )
    {
        LoopProvider = loopProvider;
        Debug = debug;
        Backend = backend;
        PushConstantsSize = pushConstantsSize;
        PreferredSurfaceFormat = preferredSurfaceFormat;
        Name = name;

        
        // SurfaceSource = surfaceSource;
        // SurfaceClearColor = surfaceClearColor;
        
        // VSync = vsync;
        
        // InitialSurfaceSizeWidth = initialSurfaceSizeWidth;
        // InitialSurfaceSizeHeight = initialSurfaceSizeHeight;
        // SurfaceFormat = surfaceFormat;
        // DepthFormat = depthFormat;
        
    }
    /// <summary>
    /// The loop provider of the GPU
    /// </summary>
    /// <value>The loop provider of the GPU</value>
    public IGPULoopProvider LoopProvider { get; init; }

    /// <summary>
    /// The backend of the GPU
    /// </summary>
    /// <value></value>
    public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
    
    /// <summary>
    /// Whether to enable the debug mode
    /// </summary>
    /// <value>Whether to enable the debug mode</value>
    public bool Debug { get; init; } = false;
    /// <summary>
    /// The size of the push constants buffer in bytes. Put 0 to disable.
    /// </summary> 
    public uint PushConstantsSize { get; init; } = 256;
    public PixelFormat PreferredSurfaceFormat { get; init; } = PixelFormat.BGRA8UnormSrgb;
    public string Name { get; init; } = "Vocore Graphics Device";
}
