using Alco.Graphics.NoGPU;

#if USE_ALCO_GPU
using Alco.Graphics.AlcoGpu;
#endif

namespace Alco.Graphics;

/// <summary>
/// Provides factory methods to create GPU devices.
/// </summary>
public static class GraphicsDeviceFactory
{
    /// <summary>
    /// The virtual GPU device that does not support any GPU operations but keeps a non-null device instance, for developing game logic without the need for a real GPU.
    /// </summary>
    /// <returns>The null GPU device.</returns>
    public static GPUDevice GetNoGPUDevice()
    {
        return new NoDevice();
    }

    /// <summary>
    /// Creates a GPU device backed by alco-gpu, the self-maintained Rust layer over wgpu-core.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU device.</param>
    /// <returns>The created alco-gpu GPU device.</returns>
    public static GPUDevice CreateAlcoGpuDevice(DeviceDescriptor descriptor)
    {
#if USE_ALCO_GPU
        return new AlcoGpuDevice(descriptor);
#else
        throw new PlatformNotSupportedException("alco-gpu is not supported");
#endif
    }
}
