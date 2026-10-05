using Alco.Graphics.NoGPU;

#if USE_ALCO_GPU
using Alco.Graphics.AlcoGpu;
#endif

namespace Alco.Graphics;

public static class GraphicsDeviceFactory
{
    /// <summary>
    /// The virtual GPU device that does not support any GPU operations but keep the object not null. Can be used for the development of the game logic without the need for a real GPU.
    /// </summary>
    public static GPUDevice GetNoGPUDevice()
    {
        return new NoDevice();
    }

    /// <summary>
    /// Creates the alco-gpu device: the self-maintained Rust layer over wgpu-core
    /// exposing the alco_* C ABI.
    /// </summary>
    public static GPUDevice CreateAlcoGpuDevice(DeviceDescriptor descriptor)
    {
#if USE_ALCO_GPU
        return new AlcoGpuDevice(descriptor);
#else
        throw new PlatformNotSupportedException("alco-gpu is not supported");
#endif
    }
}
