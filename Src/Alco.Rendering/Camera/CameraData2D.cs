using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;


/// <summary>
/// The mathmatical representation of a 2D camera. Can be used in 2D scenes and UI.
/// It can also consider as a orthographic camera looking along the Z axis.
/// </summary>
public struct CameraData2D : ICamera
{
    /// <summary>
    /// The 2D transform data containing position, rotation and scale.
    /// </summary>
    public Transform2D Transform;

    /// <summary>
    /// The near clipping plane distance.
    /// </summary>
    public float Near;

    /// <summary>
    /// The far clipping plane distance.
    /// </summary>
    public float Far;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraData2D"/> struct with default values.
    /// </summary>
    public CameraData2D()
    {
        Transform = Transform2D.Identity;
        Near = -1;
        Far = 1;
    }

    /// <summary>
    /// Gets or sets the size of the camera view.
    /// </summary>
    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Transform.Scale;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Transform.Scale = value;
    }

    /// <summary>
    /// Gets the view matrix for the camera.
    /// </summary>
    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Vector3 position = new Vector3(Transform.Position, 0);
            return Matrix4x4.CreateLookAtLeftHanded(position, position + Vector3.UnitZ, Vector3.UnitY);
        }
    }

    /// <summary>
    /// Gets the orthographic projection matrix for the camera.
    /// </summary>
    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Vector2 halfSize = Transform.Scale * 0.5f;
            return Matrix4x4.CreateOrthographicOffCenterLeftHanded(
                -halfSize.X, halfSize.X,    // left, right
                -halfSize.Y, halfSize.Y,    // bottom, top
                Near, Far                    // near, far 
            );
        }
    }

    /// <summary>
    /// Gets the combined view-projection matrix for the camera.
    /// </summary>
    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ViewMatrix * ProjectionMatrix;
    }

    public Ray3D ScreenPointToRay(Vector2 screenPosition, Vector2 screenSize)
    {
        return UtilsCameraMath.ScreenPointToRay2D(screenPosition, screenSize, ViewProjectionMatrix, Near, Near + 1);
    }

    /// <summary>
    /// Gets the viewport rectangle representing the area visible by the camera in world coordinates.
    /// </summary>
    /// <returns>A <see cref="Rect"/> representing the camera's viewport in world space.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rect GetViewport()
    {
        Vector2 halfSize = Transform.Scale * 0.5f;
        Vector2 origin = Transform.Position - halfSize;
        return new Rect(origin, Transform.Scale);
    }
}