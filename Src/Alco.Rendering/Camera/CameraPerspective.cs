using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Represents a 3D perspective camera for rendering 3D scenes with perspective projection.
/// </summary>
public class CameraPerspective : BaseCameraObject<CameraDataPerspective>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CameraPerspective"/> class.
    /// </summary>
    /// <param name="data">The camera data containing the camera's configuration.</param>
    public CameraPerspective() : base(new CameraDataPerspective())
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
    /// Gets or sets the field of view of the camera in radians.
    /// </summary>
    /// <value>The field of view angle in radians. A wider field of view shows more of the scene.</value>
    public float FieldOfView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.Fov;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.Fov = value;
    }

    /// <summary>
    /// Gets or sets the aspect ratio of the camera's viewport.
    /// </summary>
    /// <value>The aspect ratio (width/height) of the camera's viewport. Typically matches the render target's aspect ratio.</value>
    public float AspectRatio
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.AspectRatio;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data.AspectRatio = value;
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