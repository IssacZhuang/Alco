namespace Alco.Graphics;

/// <summary>The graphics backend a device should run on.</summary>
public enum GraphicsBackend
{
    /// <summary>Uses the NoGPU null backend — a virtual device for developing game logic without real graphics output.</summary>
    None = 0,

    /// <summary>Automatically selects the backend for the current platform.</summary>
    Auto = 1,

    /// <summary>The Vulkan API.</summary>
    Vulkan = 2,

    /// <summary>The Direct3D 12 API.</summary>
    Dx12 = 3,

    /// <summary>The Metal API.</summary>
    Metal = 4,
}
