using System.Numerics;

namespace Alco.Graphics;

/// <summary>
/// The descriptor of the GPU device
/// </summary>
public struct DeviceDescriptor
{
    /// <summary>Initializes the descriptor with the host and device options.</summary>
    /// <param name="loopProvider">The application host that owns the device loop.</param>
    /// <param name="backend">The graphics backend, or Auto to let the runtime choose.</param>
    /// <param name="preferredSurfaceFormat">The texture format requested for swapchain surfaces.</param>
    /// <param name="debug">Whether to enable debug validation and extra error reporting.</param>
    /// <param name="pushConstantsSize">The push constants buffer size in bytes; 0 disables it.</param>
    /// <param name="disposeDelay">How many frames a destroyed object is kept alive before release.</param>
    /// <param name="name">The diagnostic device name.</param>
    public DeviceDescriptor(
        IGPUDeviceHost loopProvider,
        GraphicsBackend backend = GraphicsBackend.Auto,
        PixelFormat preferredSurfaceFormat = PixelFormat.BGRA8Unorm,
        bool debug = false,        
        uint pushConstantsSize = 128,
        uint disposeDelay = 0,
        string name = "Alco Graphics Device"
    )
    {
        Host = loopProvider;
        Debug = debug;
        Backend = backend;
        PushConstantsSize = pushConstantsSize;
        PreferredSurfaceFormat = preferredSurfaceFormat;
        Name = name;
        DisposeDelay = disposeDelay;
    }
    /// <summary>
    /// The application host that owns the device loop and receives logging and
    /// lifetime events.
    /// </summary>
    public IGPUDeviceHost Host { get; init; }

    /// <summary>
    /// The graphics backend to create the device on; Auto lets the runtime choose
    /// per platform.
    /// </summary>
    public GraphicsBackend Backend { get; init; } = GraphicsBackend.Auto;

    /// <summary>
    /// Whether to enable debug validation and extra error reporting.
    /// </summary>
    public bool Debug { get; init; } = false;
    /// <summary>
    /// The size of the push constants buffer in bytes. Put 0 to disable.
    /// </summary>
    public uint PushConstantsSize { get; init; } = 128;

    /// <summary>
    /// How many frames a destroyed GPU object is kept alive before its native
    /// resources are released, so in-flight frames can finish using it.
    /// </summary>
    public uint DisposeDelay { get; init; } = 0;
    /// <summary>
    /// The texture format requested for swapchain surfaces.
    /// </summary>
    public PixelFormat PreferredSurfaceFormat { get; init; } = PixelFormat.BGRA8Unorm;
    /// <summary>
    /// Diagnostic name of the device.
    /// </summary>
    public string Name { get; init; } = "Alco Graphics Device";
}
