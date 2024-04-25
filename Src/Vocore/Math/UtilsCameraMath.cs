using System.Numerics;

namespace Vocore;

public static class UtilsCameraMath
{
    public static Ray3D ScreenPointToRay(Vector2 screenPoint, Vector2 screenSize, Matrix4x4 viewProjMatrix, Vector3 cameraPosition)
    {
        // Convert screen point to clip space
        Vector2 clipPoint = new Vector2(
            2.0f * screenPoint.X / screenSize.X - 1.0f,
            1.0f - 2.0f * screenPoint.Y / screenSize.Y
        );

        // Convert clip point to camera space
        Vector4 cameraPoint = new Vector4(clipPoint.X, clipPoint.Y, 1.0f, 1.0f);

        // Convert camera point to world space
        Matrix4x4.Invert(viewProjMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 worldPoint = Vector4.Transform(cameraPoint, inverseViewProjection);

        // Create ray
        Vector3 rayOrigin = cameraPosition;
        Vector3 rayDirection = Vector3.Normalize(new Vector3(worldPoint.X, worldPoint.Y, worldPoint.Z) - rayOrigin);
        return new Ray3D(rayOrigin, rayDirection);
    }
}