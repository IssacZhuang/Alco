

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// A camera object that forwards to an unmanaged <typeparamref name="T"/> camera implementation.
/// </summary>
public abstract class BaseCameraObject<T> : ICamera where T : unmanaged, ICamera
{
    public T Data;

    /// <summary>
    /// Creates the camera object over the given camera data.
    /// </summary>
    /// <param name="data">The camera data to forward to.</param>
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

    /// <summary>
    /// Converts a screen-space point into a world-space ray.
    /// </summary>
    /// <param name="screenPosition">The screen position in pixels.</param>
    /// <param name="screenSize">The screen size in pixels.</param>
    /// <returns>The ray through the screen point.</returns>
    public Ray3D ScreenPointToRay(Vector2 screenPosition, Vector2 screenSize)
    {
        return Data.ScreenPointToRay(screenPosition, screenSize);
    }
}
