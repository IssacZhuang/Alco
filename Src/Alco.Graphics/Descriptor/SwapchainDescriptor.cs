using System.Numerics;

namespace Alco.Graphics;

/// <summary>
/// The creation information for a swapchain: the surface it presents to, the
/// attachment formats and sizes, the clear color and vsync behavior.
/// </summary>
public struct SwapchainDescriptor
{
    /// <summary>
    /// Initializes the descriptor with an explicit clear color.
    /// </summary>
    public SwapchainDescriptor(SurfaceSource source, PixelFormat colorFormat, PixelFormat? depthFormat, Vector4 clearColor, uint width, uint height, bool isVSyncEnabled, string name = "unnamed swapchain")
    {
        SurfaceSource = source;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;
        ClearColor = clearColor;
        Width = width;
        Height = height;
        IsVSyncEnabled = isVSyncEnabled;
        Name = name;
    }

    /// <summary>
    /// Initializes the descriptor with the default (opaque black) clear color.
    /// </summary>
    public SwapchainDescriptor(SurfaceSource source, PixelFormat colorFormat, PixelFormat? depthFormat, uint width, uint height, bool isVSyncEnabled, string name = "unnamed swapchain")
    {
        SurfaceSource = source;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;
        ClearColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        Width = width;
        Height = height;
        IsVSyncEnabled = isVSyncEnabled;
        Name = name;
    }

    /// <summary>
    /// The window surface the swapchain presents to.
    /// </summary>
    public SurfaceSource SurfaceSource { get; init; }
    /// <summary>
    /// The format of the swapchain's color textures.
    /// </summary>
    public PixelFormat ColorFormat { get; init; }
    /// <summary>
    /// The format of the swapchain's depth texture, or null for no depth attachment.
    /// </summary>
    public PixelFormat? DepthFormat { get; init; }
    /// <summary>
    /// The RGBA value the swapchain's color attachment is cleared to.
    /// </summary>
    public Vector4 ClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>
    /// The width of the swapchain images in texels.
    /// </summary>
    public uint Width { get; init; }
    /// <summary>
    /// The height of the swapchain images in texels.
    /// </summary>
    public uint Height { get; init; }
    /// <summary>
    /// Whether presentation waits for vertical sync (frames align with the display refresh).
    /// </summary>
    public bool IsVSyncEnabled { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed swapchain";
}