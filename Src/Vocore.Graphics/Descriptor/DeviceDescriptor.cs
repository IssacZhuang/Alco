using System.Numerics;

namespace Vocore.Graphics;

public struct DeviceDescriptor
{
    public DeviceDescriptor(
        GraphicsBackend backend = GraphicsBackend.Auto,
        PixelFormat preferredSurfaceFormat = PixelFormat.BGRA8UnormSrgb,
        bool debug = false,        
        uint pushConstantsSize = 256,
        string name = "Vocore Graphics Device"
    )
    {
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

    public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
    
    public bool Debug { get; init; } = false;
    /// <summary>
    /// The size of the push constants buffer in bytes. Put 0 to disable.
    /// </summary> 
    public uint PushConstantsSize { get; init; } = 256;
    public PixelFormat PreferredSurfaceFormat { get; init; } = PixelFormat.BGRA8UnormSrgb;
    public string Name { get; init; } = "Vocore Graphics Device";
}
