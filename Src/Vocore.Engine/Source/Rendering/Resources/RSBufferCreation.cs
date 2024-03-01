using Vocore.Graphics;

namespace Vocore.Engine;

public static partial class RenderingService
{
    public static GraphicsBuffer CreateVRamBuffer(
        uint size,
        string name = "uniform_buffer"
    )
    {
        return new GraphicsBuffer(
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

    public unsafe static GraphicsBuffer<T> CreateTypedVRamBuffer<T>(
        string name = "uniform_buffer"
    ) where T : unmanaged
    {
        return new GraphicsBuffer<T>(
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
    public unsafe static GraphicsBuffer<T> CreateTypedVRamBuffer<T>(
        T value,
        string name = "uniform_buffer"
    ) where T : unmanaged
    {
        return new GraphicsBuffer<T>(
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
    
    public unsafe static GraphicsArrayBuffer<T> CreateGraphicsArrayBuffer<T>(
        int length,
        string name = "array_buffer"
    ) where T : unmanaged
    {
        return new GraphicsArrayBuffer<T>(
            GraphicsDevice,
            GraphicsDevice.CreateBuffer(
                new BufferDescriptor
                {
                    Name = name,
                    Size = (ulong)(sizeof(T) * length),
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                },
                length
            ),
            length
        );
    }
}