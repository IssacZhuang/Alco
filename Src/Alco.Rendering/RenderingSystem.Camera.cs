using System.Numerics;

namespace Alco.Rendering;

// camera factory

public partial class RenderingSystem
{
    /// <summary>
    /// Create a 2D camera.
    /// </summary>
    /// <param name="size">The size of the camera.</param>
    /// <param name="depth">The depth of the camera.</param>
    /// <returns>A new 2D camera.</returns>
    public Camera2DBuffer CreateCamera2D(Vector2 size, float depth, string name = "camera_2d")
    {
        return new Camera2DBuffer(this, size, -depth, depth, name);
    }

    /// <summary>
    /// Create a 2D camera.
    /// </summary>
    /// <param name="width">The width of the camera.</param>
    /// <param name="height">The height of the camera.</param>
    /// <param name="depth">The depth of the camera.</param>
    /// <returns>A new 2D camera.</returns>
    public Camera2DBuffer CreateCamera2D(float width, float height, float depth, string name = "camera_2d")
    {
        return new Camera2DBuffer(this, new Vector2(width, height), -depth, depth, name);
    }

    /// <summary>
    /// Create a 2D camera.
    /// </summary>
    /// <param name="width">The width of the camera.</param>
    /// <param name="height">The height of the camera.</param>
    /// <param name="near">The near plane of the camera.</param>
    /// <param name="far">The far plane of the camera.</param>
    /// <param name="name">The name of the camera.</param>
    /// <returns></returns>
    public Camera2DBuffer CreateCamera2D(float width, float height, float near, float far, string name = "camera_2d")
    {
        return new Camera2DBuffer(this, new Vector2(width, height), near, far, name);
    }


    /// <summary>
    /// Create a 3D perspective camera.
    /// </summary>
    /// <param name="fov">The field of view of the camera.</param>
    /// <param name="aspectRatio">The aspect ratio of the camera. Usually the aspect ratio of the view.</param>
    /// <param name="near">The near plane of the camera.</param>
    /// <param name="far">The far plane of the camera.</param>
    /// <returns>A new perspective camera.</returns>
    public CameraPerspectiveBuffer CreateCameraPerspective(float fov, float aspectRatio, float near, float far, string name = "camera_perspective")
    {
        return new CameraPerspectiveBuffer(this, name)
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
    public CameraOrthographicBuffer CreateCameraOrthographic(float width, float height, float near, float far, string name = "camera_orthographic")
    {
        return new CameraOrthographicBuffer(this, name)
        {
            ViewSize = new Vector2(width, height),
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
    public CameraOrthographicBuffer CreateCameraOrthographic(Vector2 size, float near, float far, string name = "camera_orthographic")
    {
        return new CameraOrthographicBuffer(this, name)
        {
            ViewSize = size,
            Near = near,
            Far = far
        };
    }
}