using System.Numerics;

namespace Alco;

public static class CameraMathUtility
{
    /// <summary>
    /// Converts a screen point to a 3D ray for perspective projection.
    /// In perspective projection, all rays originate from the camera position.
    /// </summary>
    /// <param name="screenPoint">The screen point in pixels</param>
    /// <param name="screenSize">The size of the screen in pixels</param>
    /// <param name="viewProjMatrix">The combined view-projection matrix</param>
    /// <param name="cameraPosition">The position of the camera in world space</param>
    /// <returns>A ray from the camera position through the screen point</returns>
    public static Ray3D ScreenPointToRayPerspective(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix, Vector3 cameraPosition)
    {
        Vector3 direction = ScreenPointToDirectionPerspective(screenPoint, screenSize, viewProjMatrix);
        return new Ray3D(cameraPosition, direction);
    }

    /// <summary>
    /// Converts a screen point to a world space direction for perspective projection.
    /// </summary>
    /// <param name="screenPoint">The screen point in pixels</param>
    /// <param name="screenSize">The size of the screen in pixels</param>
    /// <param name="viewProjMatrix">The combined view-projection matrix</param>
    /// <returns>The normalized direction vector from camera to the world point</returns>
    public static Vector3 ScreenPointToDirectionPerspective(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix)
    {
        // Convert screen point to clip space
        Vector2 clipPoint = new Vector2(
            2.0f * screenPoint.X / screenSize.X - 1.0f,
            1.0f - 2.0f * screenPoint.Y / screenSize.Y
        );

        // Convert clip point to camera space
        Vector4 cameraPoint = new Vector4(clipPoint, 1.0f, 1.0f);

        // Convert camera point to world space
        Matrix4x4.Invert(viewProjMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 worldPoint = Vector4.Transform(cameraPoint, inverseViewProjection);

        if (worldPoint.W != 0)
        {
            worldPoint /= worldPoint.W;
        }
        return Vector3.Normalize(new Vector3(worldPoint.X, worldPoint.Y, worldPoint.Z));
    }

    public static Ray3D ScreenPointToRay2D(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix, float originZ, float targetZ)
    {
        Vector2  worldPoint = ScreenPointToWorld2D(screenPoint, screenSize, viewProjMatrix);

        Vector3 origin = new Vector3(worldPoint.X, worldPoint.Y, originZ);
        Vector3 target = new Vector3(worldPoint.X, worldPoint.Y, targetZ);

        return Ray3D.CreateWithStartAndEnd(origin, target);
    }

    public static Vector2 ScreenPointToWorld2D(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix)
    {
        // Convert screen point to clip space
        Vector2 clipPoint = new Vector2(
            2.0f * screenPoint.X / screenSize.X - 1.0f,
            1.0f - 2.0f * screenPoint.Y / screenSize.Y
        );

        // Convert clip point to camera space
        Vector4 cameraPoint = new Vector4(clipPoint, 1.0f, 1.0f);

        // Convert camera point to world space
        Matrix4x4.Invert(viewProjMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 worldPoint = Vector4.Transform(cameraPoint, inverseViewProjection);
        if (worldPoint.W != 0)
        {
            worldPoint /= worldPoint.W;
        }

        return new Vector2(worldPoint.X, worldPoint.Y);
    }

    /// <summary>
    /// Converts a screen point to a 3D ray for orthographic projection.
    /// In orthographic projection, all rays are parallel and have the same direction.
    /// </summary>
    /// <param name="screenPoint">The screen point in pixels</param>
    /// <param name="screenSize">The size of the screen in pixels</param>
    /// <param name="viewProjMatrix">The combined view-projection matrix</param>
    /// <param name="cameraForward">The forward direction of the camera</param>
    /// <param name="nearPlane">The near plane distance</param>
    /// <returns>A ray from the screen point in world space</returns>
    public static Ray3D ScreenPointToRayOrthographic(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix, Vector3 cameraForward, float nearPlane)
    {
        // Convert screen point to clip space
        Vector2 clipPoint = new Vector2(
            2.0f * screenPoint.X / screenSize.X - 1.0f,
            1.0f - 2.0f * screenPoint.Y / screenSize.Y
        );

        // For orthographic projection, we need to find the world position on the near plane
        // Convert clip point to camera space at near plane
        Vector4 nearPoint = new Vector4(clipPoint, 0.0f, 1.0f); // Z=0 represents near plane in clip space

        // Convert to world space
        Matrix4x4.Invert(viewProjMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 worldPoint = Vector4.Transform(nearPoint, inverseViewProjection);

        if (worldPoint.W != 0)
        {
            worldPoint /= worldPoint.W;
        }

        Vector3 rayOrigin = new Vector3(worldPoint.X, worldPoint.Y, worldPoint.Z);
        Vector3 rayDirection = Vector3.Normalize(cameraForward);

        return new Ray3D(rayOrigin, rayDirection);
    }
}