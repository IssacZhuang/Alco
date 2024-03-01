using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public abstract class ShaderResource : BaseGPUObject
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GPUDevice GetDevice()
    {
        return GameEngine.Instance.GraphicsDevice;
    }
}