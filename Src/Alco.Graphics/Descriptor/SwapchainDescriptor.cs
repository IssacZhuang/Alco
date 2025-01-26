namespace Alco.Graphics;

public struct SwapchainDescriptor
{
    public SwapchainDescriptor(SurfaceSource source, PixelFormat colorFormat, PixelFormat? depthFormat, ColorFloat clearColor, uint width, uint height, bool isVSyncEnabled, string name = "unnamed swapchain")
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

    public SwapchainDescriptor(SurfaceSource source, PixelFormat colorFormat, PixelFormat? depthFormat, uint width, uint height, bool isVSyncEnabled, string name = "unnamed swapchain")
    {
        SurfaceSource = source;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;
        ClearColor = new ColorFloat(0.0f, 0.0f, 0.0f, 1.0f);
        Width = width;
        Height = height;
        IsVSyncEnabled = isVSyncEnabled;
        Name = name;
    }

    public SurfaceSource SurfaceSource { get; init; }
    public PixelFormat ColorFormat { get; init; }
    public PixelFormat? DepthFormat { get; init; }
    public ColorFloat ClearColor { get; init; } = new ColorFloat(0.0f, 0.0f, 0.0f, 1.0f);

    public uint Width { get; init; }
    public uint Height { get; init; }
    public bool IsVSyncEnabled { get; init; }
    public string Name { get; init; } = "unnamed swapchain";
}