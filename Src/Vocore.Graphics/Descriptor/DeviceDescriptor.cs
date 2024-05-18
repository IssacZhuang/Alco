using System.Numerics;

namespace Vocore.Graphics;

public struct DeviceDescriptor
{
    public DeviceDescriptor(
        SurfaceSource surfaceSource,
        Vector4 surfaceClearColor,

        GraphicsBackend backend = GraphicsBackend.Auto,
        bool vsync = false,
        bool debug = false,
        uint initialSurfaceSizeWidth = 640,
        uint initialSurfaceSizeHeight = 360,
        PixelFormat surfaceFormat = PixelFormat.RGBA8Unorm,
        PixelFormat? depthFormat = null,
        uint pushConstantsSize = 256,
        string name = "Vocore Graphics Device"
    )
    {
        SurfaceSource = surfaceSource;
        SurfaceClearColor = surfaceClearColor;
        Backend = backend;
        VSync = vsync;
        Debug = debug;
        InitialSurfaceSizeWidth = initialSurfaceSizeWidth;
        InitialSurfaceSizeHeight = initialSurfaceSizeHeight;
        SurfaceFormat = surfaceFormat;
        DepthFormat = depthFormat;
        PushConstantsSize = pushConstantsSize;
        Name = name;
    }

    public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
    public SurfaceSource SurfaceSource { get; init; }
    public bool VSync { get; init; } = false;
    public bool Debug { get; init; } = false;
    /// <summary>
    /// The size of the push constants buffer in bytes. Put 0 to disable.
    /// </summary> 
    public uint PushConstantsSize { get; init; } = 256;

    public uint InitialSurfaceSizeWidth { get; init; }
    public uint InitialSurfaceSizeHeight { get; init; }
    public Vector4 SurfaceClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
    public PixelFormat SurfaceFormat { get; init; } = PixelFormat.RGBA8Unorm;
    public PixelFormat? DepthFormat { get; init; } = null;
    public string Name { get; init; } = "Vocore Graphics Device";
}
