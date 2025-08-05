using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Represents a 2D camera for rendering 2D scenes with orthographic projection.
/// </summary>
public class Camera2D : BaseCameraObject<CameraData2D>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Camera2D"/> class.
    /// </summary>
    /// <param name="data">The camera data containing the camera's configuration.</param>
    public Camera2D() : base(new CameraData2D())
    {
    }

    /// <summary>
    /// Gets a reference to the camera's 2D transformation data.
    /// </summary>
    /// <value>A reference to the <see cref="Transform2D"/> that defines the camera's position, rotation, and scale.</value>
    public ref Transform2D Transform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Data.Transform;
    }

    /// <summary>
    /// Gets or sets the size of the camera's viewport in world units.
    /// </summary>
    /// <value>A <see cref="Vector2"/> representing the width and height of the camera's viewport.</value>
    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Size;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Size = value;
    }

    /// <summary>
    /// Gets or sets the near clipping plane distance for the camera.
    /// </summary>
    /// <value>The distance to the near clipping plane. Objects closer than this distance will not be rendered.</value>
    public float Near
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Near;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Near = value;
    }

    /// <summary>
    /// Gets or sets the far clipping plane distance for the camera.
    /// </summary>
    /// <value>The distance to the far clipping plane. Objects farther than this distance will not be rendered.</value>
    public float Far
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Far;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Far = value;
    }

    /// <summary>
    /// Gets the viewport rectangle representing the area visible by the camera in world coordinates.
    /// </summary>
    /// <returns>A <see cref="Rect"/> representing the camera's viewport in world space.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rect GetViewport()
    {
        return Data.GetViewport();
    }
}