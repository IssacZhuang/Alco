using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Represents a 3D orthographic camera for rendering 3D scenes with orthographic projection.
/// </summary>
public class CameraOrthographic : BaseCameraObject<CameraDataOrthographic>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CameraOrthographic"/> class.
    /// </summary>
    /// <param name="data">The camera data containing the camera's configuration.</param>
    public CameraOrthographic() : base(new CameraDataOrthographic())
    {
    }

    /// <summary>
    /// Gets a reference to the camera's 3D transformation data.
    /// </summary>
    /// <value>A reference to the <see cref="Transform3D"/> that defines the camera's position, rotation, and scale.</value>
    public ref Transform3D Transform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Data.Transform;
    }

    /// <summary>
    /// Gets or sets the width of the orthographic view.
    /// </summary>
    /// <value>The width of the orthographic camera's view area in world units.</value>
    public float Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Width;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Width = value;
    }

    /// <summary>
    /// Gets or sets the height of the orthographic view.
    /// </summary>
    /// <value>The height of the orthographic camera's view area in world units.</value>
    public float Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Height;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Height = value;
    }

    /// <summary>
    /// Gets or sets the size of the orthographic view.
    /// </summary>
    /// <value>A <see cref="Vector2"/> representing the width and height of the orthographic camera's view area.</value>
    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new Vector2(Data.Width, Data.Height);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            Data.Width = value.X;
            Data.Height = value.Y;
        }
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
}