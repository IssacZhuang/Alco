using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Represents a camera interface for 3D rendering operations.
/// Provides essential transformation matrices for view and projection calculations.
/// </summary>
public interface ICamera
{
    /// <summary>
    /// Gets the view matrix that transforms world coordinates to camera/view coordinates.
    /// This matrix represents the camera's position and orientation in the world.
    /// </summary>
    Matrix4x4 ViewMatrix { get; }

    /// <summary>
    /// Gets the projection matrix that transforms view coordinates to clip coordinates.
    /// This matrix defines the camera's field of view, aspect ratio, and near/far clipping planes.
    /// </summary>
    Matrix4x4 ProjectionMatrix { get; }

    /// <summary>
    /// Gets the combined view-projection matrix (ViewMatrix * ProjectionMatrix).
    /// This matrix transforms world coordinates directly to clip coordinates in a single operation.
    /// </summary>
    Matrix4x4 ViewProjectionMatrix { get; }

    /// <summary>
    /// Get the normalized ray from the camera to the screen position
    /// </summary>
    /// <param name="screenPosition">The screen position in pixels</param>
    /// <returns>The ray from the camera to the screen position</returns>
    Ray3D ScreenPointToRay(Vector2 screenPosition, Vector2 screenSize);
}