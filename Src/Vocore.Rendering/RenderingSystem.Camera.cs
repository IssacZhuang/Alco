using System.Numerics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    /// <summary>
    /// Create a 2D camera.
    /// </summary>
    /// <param name="size">The size of the camera.</param>
    /// <param name="depth">The depth of the camera.</param>
    /// <returns>A new 2D camera.</returns>
    public Camera2D CreateCamera2D(Vector2 size, float depth)
    {
        return new Camera2D(this, "camera_2d")
        {
            Size = size,
            Depth = depth
        };
    }

    /// <summary>
    /// Create a 2D camera.
    /// </summary>
    /// <param name="width">The width of the camera.</param>
    /// <param name="height">The height of the camera.</param>
    /// <param name="depth">The depth of the camera.</param>
    /// <returns>A new 2D camera.</returns>
    public Camera2D CreateCamera2D(float width, float height, float depth)
    {
        return CreateCamera2D(new Vector2(width, height), depth);
    }


    /// <summary>
    /// Create a 3D perspective camera.
    /// </summary>
    /// <param name="fov">The field of view of the camera.</param>
    /// <param name="aspectRatio">The aspect ratio of the camera. Usually the aspect ratio of the window.</param>
    /// <param name="near">The near plane of the camera.</param>
    /// <param name="far">The far plane of the camera.</param>
    /// <returns>A new perspective camera.</returns>
    public CameraPerspective CreateCameraPerspective(float fov, float aspectRatio, float near, float far)
    {
        return new CameraPerspective(this, "camera_perspective")
        {
            FieldOfView = fov,
            AspectRatio = aspectRatio,
            Near = near,
            Far = far
        };
    }


    /// <summary>
    /// Create a 3D orthographic camera.
    /// </summary>
    /// <param name="width">The width of the camera.</param>
    /// <param name="height">The height of the camera.</param>
    /// <param name="near">The near plane of the camera.</param>
    /// <param name="far">The far plane of the camera.</param>
    /// <returns>A new orthographic camera.</returns>
    public CameraOrthographic CreateCameraOrthographic(float width, float height, float near, float far)
    {
        return new CameraOrthographic(this, "camera_orthographic")
        {
            Size = new Vector2(width, height),
            Near = near,
            Far = far
        };
    }

    /// <summary>
    /// Create a 3D orthographic camera.
    /// </summary>
    /// <param name="size">The size of the camera.</param>
    /// <param name="near">The near plane of the camera.</param>
    /// <param name="far">The far plane of the camera.</param>
    /// <returns>A new orthographic camera.</returns>
    public CameraOrthographic CreateCameraOrthographic(Vector2 size, float near, float far)
    {
        return new CameraOrthographic(this, "camera_orthographic")
        {
            Size = size,
            Near = near,
            Far = far
        };
    }
}