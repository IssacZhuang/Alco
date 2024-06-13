using System.Numerics;

namespace Vocore.Graphics;

public struct DeviceDescriptor
{
    public DeviceDescriptor(
        // SurfaceSource surfaceSource,
        // Vector4 surfaceClearColor,
        GraphicsBackend backend = GraphicsBackend.Auto,
        PixelFormat preferredSDRFormat = PixelFormat.RGBA8Unorm,
        PixelFormat preferredHDRFormat = PixelFormat.RGBA16Float,
        PixelFormat preferredSurfaceFormat = PixelFormat.BGRA8UnormSrgb,
        PixelFormat depthFormat = PixelFormat.Depth24PlusStencil8,
        // bool vsync = false,
        bool debug = false,
        // uint initialSurfaceSizeWidth = 640,
        // uint initialSurfaceSizeHeight = 360,
        // PixelFormat surfaceFormat = PixelFormat.RGBA8Unorm,
        // PixelFormat? depthFormat = null,
        
        uint pushConstantsSize = 256,
        string name = "Vocore Graphics Device"
    )
    {
        Debug = debug;
        Backend = backend;
        PushConstantsSize = pushConstantsSize;
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
    public PixelFormat _preferredSDRFormat { get; init; } = PixelFormat.RGBA8Unorm;
    public PixelFormat _preferredHDRFormat { get; init; } = PixelFormat.RGBA16Float;
    public PixelFormat _preferredSurfaceFormat { get; init; } = PixelFormat.BGRA8UnormSrgb;
    public PixelFormat _depthFormat { get; init; } = PixelFormat.Depth24PlusStencil8;

    // public SurfaceSource SurfaceSource { get; init; }
    // public bool VSync { get; init; } = false;
    // public uint InitialSurfaceSizeWidth { get; init; }
    // public uint InitialSurfaceSizeHeight { get; init; }
    // public Vector4 SurfaceClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
    // public PixelFormat SurfaceFormat { get; init; } = PixelFormat.RGBA8Unorm;
    public PixelFormat? DepthFormat { get; init; } = null;
    public string Name { get; init; } = "Vocore Graphics Device";
}
