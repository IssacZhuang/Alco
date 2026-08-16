using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Pure CPU-side math of the point light shadow atlas: the six cube-face bases,
/// the face view-projection construction and the analytic atlas mapping used by
/// the sampling shaders. The analytic projection mirrors exactly what the folded
/// matrices uploaded to <c>PointLightShadowDepth.hlsl</c> do, so the CPU
/// (matrix build) and GPU (visibility sampling) sides stay consistent by
/// construction — <c>TestPointLightShadow</c> asserts the contract.
/// <br/>All faces use a 90° perspective projection (square frustum), left-handed
/// with the engine's axis convention (X+ forward, Y+ right, Z+ up) and a 0..1
/// depth range, matching <see cref="Matrix4x4.CreatePerspectiveLeftHanded"/>.
/// </summary>
public static class PointLightShadowMath
{
    /// <summary>The number of cube faces per light.</summary>
    public const int FaceCount = 6;

    // Face bases derived once and shared by both sides (index order:
    // 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z). Forward is the face view direction,
    // Right/Up the face-space axes: view coords are (dot(p,Right), dot(p,Up),
    // dot(p,Forward)) and ndc.xy = view.xy / view.z (90° FOV, square aspect).
    private static readonly Vector3[] FaceForwards =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];
    private static readonly Vector3[] FaceRights =
    [
        new(0, 1, 0), new(0, -1, 0),
        new(-1, 0, 0), new(1, 0, 0),
        new(1, 0, 0), new(-1, 0, 0),
    ];
    private static readonly Vector3[] FaceUps =
    [
        new(0, 0, 1), new(0, 0, 1),
        new(0, 0, 1), new(0, 0, 1),
        new(0, 1, 0), new(0, 1, 0),
    ];

    /// <summary>Gets the basis vectors of one cube face (0=+X … 5=-Z).</summary>
    /// <param name="face">The face index (0..5).</param>
    /// <param name="forward">The face view direction (unit axis vector).</param>
    /// <param name="right">The face-space right axis.</param>
    /// <param name="up">The face-space up axis.</param>
    public static void GetFaceBasis(int face, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        forward = FaceForwards[face];
        right = FaceRights[face];
        up = FaceUps[face];
    }

    /// <summary>
    /// Builds the unfolded view-projection matrix of one cube face: a 90°
    /// left-handed perspective projection (0..1 depth) looking from the light
    /// position along the face direction. Composable with <see cref="FoldToAtlas"/>.
    /// </summary>
    /// <param name="lightPosition">The light center in world space.</param>
    /// <param name="near">The near plane distance (clips the emitter housing).</param>
    /// <param name="far">The far plane distance (typically the light range).</param>
    /// <param name="face">The face index (0..5).</param>
    /// <returns>The view-projection matrix (row-vector convention).</returns>
    public static Matrix4x4 BuildFaceViewProjection(Vector3 lightPosition, float near, float far, int face)
    {
        GetFaceBasis(face, out Vector3 forward, out Vector3 right, out Vector3 up);
        // Same construction as the camera / sun cascades (CreateLookAtLeftHanded
        // with Z+ up), so the engine-wide row-vector convention holds: view
        // coords are exactly (dot(p,right), dot(p,up), dot(p,forward)).
        Matrix4x4 view = Matrix4x4.CreateLookAtLeftHanded(
            lightPosition, lightPosition + forward, up);
        // 90° vertical FOV with square aspect: the frustum is 2*near wide/tall at
        // the near plane (tan(45°) = 1).
        Matrix4x4 perspective = Matrix4x4.CreatePerspectiveLeftHanded(2.0f * near, 2.0f * near, near, far);
        return view * perspective;
    }

    /// <summary>
    /// Folds a face view-projection into its sub-rectangle of the atlas so the
    /// depth pass rasterizes directly at the right place: the folded matrix maps
    /// face-space ndc [-1,1]² to the face's atlas ndc sub-rect (depth unchanged).
    /// <br/>NDC +Y points up while atlas tiles grow downward, so the Y
    /// translation is mirrored; the scale itself stays positive, which preserves
    /// triangle winding for back-face culling — the face is simply stored
    /// vertically mirrored inside its tile, exactly as <see cref="ProjectToFace"/>
    /// reconstructs it on the sampling side (uvLocal.y = 0.5 - y/2).
    /// </summary>
    /// <param name="faceViewProjection">The unfolded matrix from <see cref="BuildFaceViewProjection"/>.</param>
    /// <param name="slot">The light slot index (0..<see cref="RGNode_PointLightShadow.MaxSlots"/>-1).</param>
    /// <param name="face">The face index (0..5).</param>
    /// <param name="faceSize">The face tile size in texels.</param>
    /// <param name="slotsPerRow">The number of slot cells per atlas row.</param>
    /// <param name="atlasWidth">The atlas width in texels.</param>
    /// <param name="atlasHeight">The atlas height in texels.</param>
    /// <returns>The folded view-projection matrix.</returns>
    public static Matrix4x4 FoldToAtlas(Matrix4x4 faceViewProjection, int slot, int face,
        uint faceSize, uint slotsPerRow, uint atlasWidth, uint atlasHeight)
    {
        (float originX, float originY) = FacePixelOrigin(slot, face, faceSize, slotsPerRow);
        // ndc' = ndc * scale + translation maps [-1,1] onto the face's half-open
        // pixel rect [origin, origin + faceSize] in atlas ndc.
        float scaleX = faceSize / (float)atlasWidth;
        float scaleY = faceSize / (float)atlasHeight;
        float translationX = 2.0f * (originX + 0.5f * faceSize) / atlasWidth - 1.0f;
        float translationY = 1.0f - 2.0f * (originY + 0.5f * faceSize) / atlasHeight;
        Matrix4x4 fold = Matrix4x4.CreateScale(scaleX, scaleY, 1.0f)
            * Matrix4x4.CreateTranslation(translationX, translationY, 0.0f);
        return faceViewProjection * fold;
    }

    /// <summary>
    /// The pixel-space origin (top-left) of one face tile: each slot cell packs
    /// the six faces in a 3x2 grid (face index: x = face % 3, y = face / 3).
    /// </summary>
    /// <param name="slot">The light slot index.</param>
    /// <param name="face">The face index (0..5).</param>
    /// <param name="faceSize">The face tile size in texels.</param>
    /// <param name="slotsPerRow">The number of slot cells per atlas row.</param>
    /// <returns>The (x, y) pixel origin of the face tile.</returns>
    public static (float X, float Y) FacePixelOrigin(int slot, int face, uint faceSize, uint slotsPerRow)
    {
        float cellX = (slot % slotsPerRow) * 3.0f * faceSize;
        float cellY = (slot / slotsPerRow) * 2.0f * faceSize;
        return (cellX + (face % 3) * faceSize, cellY + (face / 3) * faceSize);
    }

    /// <summary>
    /// Projects a world position into face space analytically — the sampling-side
    /// counterpart of the matrix build. Selects the dominant face automatically.
    /// </summary>
    /// <param name="worldPosition">The receiver position in world space.</param>
    /// <param name="lightPosition">The light center in world space.</param>
    /// <param name="face">The selected face index (dominant axis of the offset).</param>
    /// <param name="uvLocal">The face-local uv in [0,1]² (v down, engine convention).</param>
    /// <param name="linearDepth">The distance along the face forward axis.</param>
    public static void ProjectToFace(Vector3 worldPosition, Vector3 lightPosition,
        out int face, out Vector2 uvLocal, out float linearDepth)
    {
        Vector3 p = worldPosition - lightPosition;
        Vector3 absP = Vector3.Abs(p);
        int dominant = 0;
        float best = absP.X;
        if (absP.Y > best) { dominant = 1; best = absP.Y; }
        if (absP.Z > best) { dominant = 2; best = absP.Z; }
        face = dominant * 2 + (Vector3.Dot(p, FaceForwards[dominant * 2]) >= 0.0f ? 0 : 1);

        GetFaceBasis(face, out Vector3 forward, out Vector3 right, out Vector3 up);
        linearDepth = Vector3.Dot(p, forward);
        float x = Vector3.Dot(p, right) / MathF.Max(linearDepth, 1e-6f);
        float y = Vector3.Dot(p, up) / MathF.Max(linearDepth, 1e-6f);
        uvLocal = new Vector2(x * 0.5f + 0.5f, 0.5f - y * 0.5f);
    }

    /// <summary>Converts a face-space forward distance to the 0..1 projected depth value.</summary>
    /// <param name="z">The forward distance (must be positive).</param>
    /// <param name="near">The near plane distance.</param>
    /// <param name="far">The far plane distance.</param>
    /// <returns>The projected depth in [0,1] (near → 0, far → 1).</returns>
    public static float LinearToProjectedDepth(float z, float near, float far)
    {
        return far * (z - near) / (z * (far - near));
    }

    /// <summary>Converts a projected 0..1 depth value back to the face-space forward distance.</summary>
    /// <param name="projected">The projected depth in [0,1].</param>
    /// <param name="near">The near plane distance.</param>
    /// <param name="far">The far plane distance.</param>
    /// <returns>The forward distance.</returns>
    public static float ProjectedDepthToLinear(float projected, float near, float far)
    {
        return near * far / (far - projected * (far - near));
    }
}
