using System.Numerics;

namespace Alco;

public static class UtilsCameraMath
{
    public static Ray3D ScreenPointToRay(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix, Vector3 cameraPosition)
    {
        Vector3 direction = ScreenPointToDirection(screenPoint, screenSize, viewProjMatrix);
        return new Ray3D(cameraPosition, direction);
    }

    public static Vector3 ScreenPointToDirection(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix)
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
}