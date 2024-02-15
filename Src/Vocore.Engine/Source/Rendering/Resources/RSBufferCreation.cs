using Vocore.Graphics;

namespace Vocore.Engine;

public static partial class RenderingService
{
    public static VRamBuffer CreateVRamBuffer(
        uint size,
        string name = "uniform_buffer"
    )
    {
        return new VRamBuffer(
            GraphicsDevice,
            GraphicsDevice.CreateBuffer(
                new BufferDescriptor
                {
                    Name = name,
                    Size = size,
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                }
            )
        );
    }

    public unsafe static VRamBuffer<T> CreateTypedVRamBuffer<T>(
        string name = "uniform_buffer"
    ) where T : unmanaged
    {
        return new VRamBuffer<T>(
            GraphicsDevice,
            GraphicsDevice.CreateBuffer(
                new BufferDescriptor
                {
                    Name = name,
                    Size = (ulong)sizeof(T),
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                }
            ),
            default(T)
        );
    }
    public unsafe static VRamBuffer<T> CreateTypedVRamBuffer<T>(
        T value,
        string name = "uniform_buffer"
    ) where T : unmanaged
    {
        return new VRamBuffer<T>(
            GraphicsDevice,
            GraphicsDevice.CreateBuffer(
                new BufferDescriptor
                {
                    Name = name,
                    Size = (ulong)sizeof(T),
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                },
                value
            ),
            value
        );
    }
}