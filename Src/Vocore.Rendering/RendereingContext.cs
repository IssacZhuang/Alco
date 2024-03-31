using Vocore.Graphics;

namespace Vocore.Rendering;

public static class RendereringContext
{
#pragma warning disable CS8618
    private static GPUDevice _device;
#pragma warning restore CS8618
    public static GPUDevice Device
    {
        get
        {
            if (_device == null)
            {
                throw new Exception("No GPU device has been set. Use RendereringContext.SetDevice() to set GPU device.");
            }
            return _device;
        }
    }

    public static void SetDevice(GPUDevice device)
    {
        _device = device;
    }
}