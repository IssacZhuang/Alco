using System.Numerics;

namespace Vocore.Graphics
{
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
            Name = name;
        }

        public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
        public SurfaceSource SurfaceSource { get; init; }
        public bool VSync { get; init; } = false;
        public bool Debug { get; init; } = false;
        public uint InitialSurfaceSizeWidth { get; init; }
        public uint InitialSurfaceSizeHeight { get; init; }
        public Vector4 SurfaceClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        public string Name { get; init; } = "Vocore Graphics Device";
    }
}