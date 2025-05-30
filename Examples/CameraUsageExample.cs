using System.Numerics;
using Alco.Rendering;
using Alco;

namespace Examples;

/// <summary>
/// Demonstrates how to use the CameraPerspective and CameraOrthographic classes.
/// </summary>
public static class CameraUsageExample
{
    /// <summary>
    /// Example of creating and using a perspective camera.
    /// </summary>
    public static void PerspectiveCameraExample()
    {
        // Create perspective camera data with default values
        var perspectiveData = new CameraDataPerspective(
            fov: CameraDataPerspective.DefaultFov,           // Field of view in radians
            aspectRatio: 16f / 9f,                           // Aspect ratio (width/height)
            near: CameraDataPerspective.DefaultNear,         // Near clipping plane
            far: CameraDataPerspective.DefaultFar            // Far clipping plane
        );

        // Create the perspective camera object
        var perspectiveCamera = new CameraPerspective(perspectiveData);

        // Access and modify camera properties
        perspectiveCamera.Transform.Position = new Vector3(0, 5, -10);     // Set camera position
        perspectiveCamera.Transform.Rotation = Quaternion.Identity;        // Set camera rotation
        perspectiveCamera.FieldOfView = 0.9f;                              // Adjust field of view
        perspectiveCamera.AspectRatio = 1920f / 1080f;                     // Set aspect ratio
        perspectiveCamera.Near = 0.1f;                                     // Set near plane
        perspectiveCamera.Far = 1000f;                                     // Set far plane

        // Access inherited matrix properties from BaseCameraObject
        Matrix4x4 viewMatrix = perspectiveCamera.ViewMatrix;
        Matrix4x4 projectionMatrix = perspectiveCamera.ProjectionMatrix;
        Matrix4x4 viewProjectionMatrix = perspectiveCamera.ViewProjectionMatrix;

        // Make the camera look at a specific point
        Vector3 target = Vector3.Zero;
        perspectiveCamera.Transform.LookAt(target);
    }

    /// <summary>
    /// Example of creating and using an orthographic camera.
    /// </summary>
    public static void OrthographicCameraExample()
    {
        // Create orthographic camera data with default values
        var orthographicData = new CameraDataOrthographic(
            width: CameraDataOrthographic.DefaultWidth,      // View width
            height: CameraDataOrthographic.DefaultHeight,    // View height
            near: CameraDataOrthographic.DefaultNear,        // Near clipping plane
            far: CameraDataOrthographic.DefaultFar           // Far clipping plane
        );

        // Create the orthographic camera object
        var orthographicCamera = new CameraOrthographic(orthographicData);

        // Access and modify camera properties
        orthographicCamera.Transform.Position = new Vector3(0, 10, 0);     // Set camera position
        orthographicCamera.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2); // Look down
        orthographicCamera.Width = 20f;                                    // Set view width
        orthographicCamera.Height = 15f;                                   // Set view height
        orthographicCamera.Size = new Vector2(20f, 15f);                   // Or set both at once
        orthographicCamera.Near = 0.1f;                                    // Set near plane
        orthographicCamera.Far = 100f;                                     // Set far plane

        // Access inherited matrix properties from BaseCameraObject
        Matrix4x4 viewMatrix = orthographicCamera.ViewMatrix;
        Matrix4x4 projectionMatrix = orthographicCamera.ProjectionMatrix;
        Matrix4x4 viewProjectionMatrix = orthographicCamera.ViewProjectionMatrix;

        // Position the camera for a top-down view
        orthographicCamera.Transform.Position = new Vector3(0, 20, 0);
        orthographicCamera.Transform.LookAt(Vector3.Zero);
    }

    /// <summary>
    /// Example comparing 2D, Perspective, and Orthographic cameras.
    /// </summary>
    public static void ComparisonExample()
    {
        // 2D Camera - for 2D scenes and UI
        var camera2D = new Camera2D(new CameraData2D())
        {
            Size = new Vector2(800, 600),
            Near = -10,
            Far = 10
        };
        camera2D.Transform.Position = new Vector2(400, 300);

        // Perspective Camera - for 3D scenes with depth perspective
        var cameraPerspective = new CameraPerspective(new CameraDataPerspective())
        {
            FieldOfView = 1.0f,
            AspectRatio = 800f / 600f,
            Near = 0.1f,
            Far = 1000f
        };
        cameraPerspective.Transform.Position = new Vector3(0, 0, -5);

        // Orthographic Camera - for 3D scenes without perspective distortion
        var cameraOrthographic = new CameraOrthographic(new CameraDataOrthographic())
        {
            Width = 20f,
            Height = 15f,
            Near = 0.1f,
            Far = 100f
        };
        cameraOrthographic.Transform.Position = new Vector3(0, 0, -5);

        // All cameras provide access to the same matrix properties through ICamera interface
        ProcessCamera(camera2D);
        ProcessCamera(cameraPerspective);
        ProcessCamera(cameraOrthographic);
    }

    /// <summary>
    /// Example of a method that can work with any camera type through the ICamera interface.
    /// </summary>
    /// <param name="camera">Any camera implementing ICamera interface.</param>
    private static void ProcessCamera(ICamera camera)
    {
        Matrix4x4 viewMatrix = camera.ViewMatrix;
        Matrix4x4 projectionMatrix = camera.ProjectionMatrix;
        Matrix4x4 viewProjectionMatrix = camera.ViewProjectionMatrix;

        // Use the matrices for rendering or other calculations
        // ...
    }
} 