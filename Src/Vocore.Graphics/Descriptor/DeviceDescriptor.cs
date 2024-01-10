namespace Vocore.Graphics
{
    public struct DeviceDescriptor
    {
        public DeviceDescriptor(
            GraphicsBackend backend
        )
        {
            Backend = backend;
        }

        public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;
    }
}