using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class ShaderResource : AutoDisposable
{
    private static GPUDevice? _device;

    public static void SetGlobalDevice(GPUDevice device)
    {
        _device = device;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GPUDevice GetDevice()
    {
        if (_device == null)
        {
            throw new InvalidOperationException("The GPU device is not initialized.");
        }
        return _device;
    }
}