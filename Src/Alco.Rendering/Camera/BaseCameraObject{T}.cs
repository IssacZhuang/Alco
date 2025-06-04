

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

public abstract class BaseCameraObject<T> : ICamera where T : unmanaged, ICamera
{
    public T Data;

    public BaseCameraObject(T data)
    {
        Data = data;
    }

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.ViewMatrix;
    }

    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.ProjectionMatrix;
    }

    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.ViewProjectionMatrix;
    }
}
