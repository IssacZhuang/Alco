namespace Vocore.Graphics
{
    public struct DeviceDescriptor
    {
        public DeviceDescriptor(
            SurfaceSource surfaceSource,
            GraphicsBackend backend = GraphicsBackend.Auto,
            bool vsync = false,
            bool debug = false,
            string? name = "Vocore Graphics Device"
        )
        {
            SurfaceSource = surfaceSource;
            Backend = backend;
            VSync = vsync;
            Debug = debug;
            Name = name;
        }

        public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
        public SurfaceSource SurfaceSource { get; init; }
        public bool VSync { get; init; } = false;
        public bool Debug { get; init; } = false;
        public string? Name { get; init; } = "Vocore Graphics Device";
    }
}