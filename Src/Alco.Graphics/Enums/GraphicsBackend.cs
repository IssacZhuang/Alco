namespace Alco.Graphics;

/// <summary>The graphics backend a device should run on.</summary>
public enum GraphicsBackend
{
    /// <summary>No GPU: the virtual device for logic development without real graphics.</summary>
    None = 0,

    /// <summary>wgpu with its own backend choice for the current platform.</summary>
    Auto = 1,

    /// <summary>wgpu running on Vulkan.</summary>
    WGPUVulkan = 2,

    /// <summary>wgpu running on Direct3D 12.</summary>
    WGPUDx12 = 3,

    /// <summary>wgpu running on Metal.</summary>
    WGPUMetal = 4,

    /// <summary>The engine's native Vulkan backend (Alco.Graphics.Vulkan).</summary>
    NativeVulkan = 5,
}
