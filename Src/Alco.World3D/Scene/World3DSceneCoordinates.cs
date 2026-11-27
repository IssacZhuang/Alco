using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>A double-precision position in the left-handed World3D coordinate system.</summary>
/// <param name="X">The forward axis coordinate.</param>
/// <param name="Y">The right axis coordinate.</param>
/// <param name="Z">The up axis coordinate.</param>
public readonly record struct World3DPosition(double X, double Y, double Z);

/// <summary>An instance transform with a double-precision world pivot.</summary>
/// <param name="Position">The world position of the object-space origin.</param>
/// <param name="Rotation">A finite, nonzero rotation quaternion, normalized when packed.</param>
/// <param name="Scale">The signed local-axis scale; all components must be finite and nonzero.</param>
public readonly record struct World3DTransform(World3DPosition Position, Quaternion Rotation, Vector3 Scale);

/// <summary>A World3D perspective camera with a double-precision eye position.</summary>
/// <param name="Eye">The eye position in world coordinates.</param>
/// <param name="Rotation">A finite, nonzero rotation quaternion, normalized when creating a view.</param>
/// <param name="Fov">The vertical field of view in radians.</param>
/// <param name="Near">The positive near plane distance.</param>
public readonly record struct World3DCamera(World3DPosition Eye, Quaternion Rotation, float Fov, float Near);

/// <summary>A grid cell index stored as four signed 32-bit GPU lanes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly record struct World3DCell
{
    /// <summary>The X cell index.</summary>
    [FieldOffset(0)] public readonly int X;
    /// <summary>The Y cell index.</summary>
    [FieldOffset(4)] public readonly int Y;
    /// <summary>The Z cell index.</summary>
    [FieldOffset(8)] public readonly int Z;
    /// <summary>Reserved; always zero.</summary>
    [FieldOffset(12)] public readonly int W;

    /// <summary>Creates a cell index with zero in the reserved lane.</summary>
    /// <param name="X">The X cell index.</param>
    /// <param name="Y">The Y cell index.</param>
    /// <param name="Z">The Z cell index.</param>
    public World3DCell(int X, int Y, int Z)
    {
        this.X = X;
        this.Y = Y;
        this.Z = Z;
        W = 0;
    }
}

/// <summary>The 112-byte, camera-independent instance spatial payload.</summary>
[StructLayout(LayoutKind.Explicit, Size = 112)]
public readonly struct World3DInstanceSpatialGpu
{
    /// <summary>The row-major object transform with the cell-local pivot in its last row.</summary>
    [FieldOffset(0)] public readonly Matrix4x4 ObjectToCell;
    /// <summary>The stable cell containing the instance pivot.</summary>
    [FieldOffset(64)] public readonly World3DCell Cell;
    /// <summary>The world-axis bounds center relative to the pivot (xyz) and enclosing sphere radius (w).</summary>
    [FieldOffset(80)] public readonly Vector4 BoundsCenterRadius;
    /// <summary>The world-axis bounds half extents (xyz); w is zero.</summary>
    [FieldOffset(96)] public readonly Vector4 BoundsExtent;

    internal World3DInstanceSpatialGpu(in Matrix4x4 objectToCell, World3DCell cell, Vector4 boundsCenterRadius, Vector4 boundsExtent)
    {
        ObjectToCell = objectToCell;
        Cell = cell;
        BoundsCenterRadius = boundsCenterRadius;
        BoundsExtent = boundsExtent;
    }
}

/// <summary>The 192-byte view payload for a camera-relative, reversed infinite-depth perspective view.</summary>
[StructLayout(LayoutKind.Explicit, Size = 192)]
public readonly struct World3DView
{
    /// <summary>The row-major transform from eye-relative world space to clip space.</summary>
    [FieldOffset(0)] public readonly Matrix4x4 CameraVP;
    /// <summary>The row-major inverse of <see cref="CameraVP"/>.</summary>
    [FieldOffset(64)] public readonly Matrix4x4 CameraInvVP;
    /// <summary>The cell containing the camera eye.</summary>
    [FieldOffset(128)] public readonly World3DCell EyeCell;
    /// <summary>The hysteretic origin anchor, independent of the eye-relative matrices.</summary>
    [FieldOffset(144)] public readonly World3DCell OriginAnchorCell;
    /// <summary>The eye position relative to <see cref="EyeCell"/> (xyz) and the near plane distance (w).</summary>
    [FieldOffset(160)] public readonly Vector4 EyeLocalNear;
    /// <summary>The viewport width and height in pixels (xy) and their reciprocals (zw).</summary>
    [FieldOffset(176)] public readonly Vector4 ViewportSizeInvSize;

    private World3DView(in Matrix4x4 cameraVP, in Matrix4x4 cameraInvVP, World3DCell eyeCell, World3DCell originAnchorCell,
        Vector4 eyeLocalNear, Vector4 viewportSizeInvSize)
    {
        CameraVP = cameraVP;
        CameraInvVP = cameraInvVP;
        EyeCell = eyeCell;
        OriginAnchorCell = originAnchorCell;
        EyeLocalNear = eyeLocalNear;
        ViewportSizeInvSize = viewportSizeInvSize;
    }

    /// <summary>Creates a view and rebases its anchor to the eye cell when any axis reaches two cells from the previous anchor origin.</summary>
    /// <param name="camera">The camera to validate and project.</param>
    /// <param name="extent">The nonempty viewport size.</param>
    /// <param name="previousOriginAnchor">The previous view's anchor, or null to start at the eye cell.</param>
    /// <param name="view">The view on success; otherwise default.</param>
    /// <returns>Whether the camera, extent, and anchor can produce a valid view.</returns>
    public static bool TryCreate(in World3DCamera camera, Extent2D extent, World3DCell? previousOriginAnchor, out World3DView view)
    {
        view = default;
        if (extent.X == 0 || extent.Y == 0 || !(camera.Fov > 0f && camera.Fov < MathF.PI)
            || !float.IsFinite(camera.Near) || camera.Near <= 0f
            || !World3DSceneCoordinates.TrySplitPosition(camera.Eye, out World3DCell eyeCell, out Vector3 eyeLocal)
            || !World3DSpatialMath.TryNormalizeRotation(camera.Rotation, out Quaternion rotation))
            return false;

        const int minCell = World3DSceneCoordinates.MinCell;
        const int maxCell = World3DSceneCoordinates.MaxCell;
        World3DCell originAnchorCell = eyeCell;
        if (previousOriginAnchor is World3DCell previous)
        {
            if (previous.X is < minCell or > maxCell || previous.Y is < minCell or > maxCell || previous.Z is < minCell or > maxCell)
                return false;

            if (IsWithinAnchorAxis(camera.Eye.X, previous.X) && IsWithinAnchorAxis(camera.Eye.Y, previous.Y)
                && IsWithinAnchorAxis(camera.Eye.Z, previous.Z))
                originAnchorCell = previous;
        }

        float width = extent.X;
        float height = extent.Y;
        var perspective = new CameraDataPerspective(camera.Fov, width / height, camera.Near)
        {
            Transform = new Transform3D(Vector3.Zero, rotation, Vector3.One),
            ReverseInfiniteDepth = true,
        };
        Matrix4x4 viewRotation = perspective.ViewMatrix;
        Matrix4x4 projection = perspective.ProjectionMatrix;
        Matrix4x4 cameraVP = viewRotation * projection;
        // Invert the reversed infinite projection and orthonormal view separately to avoid a full VP determinant.
        Matrix4x4 inverseProjection = new(
            1f / projection.M11, 0f, 0f, 0f,
            0f, 1f / projection.M22, 0f, 0f,
            0f, 0f, 0f, 1f / projection.M43,
            0f, 0f, 1f, 0f);
        Matrix4x4 cameraInvVP = inverseProjection * Matrix4x4.Transpose(viewRotation);
        if (!IsFinite(cameraVP) || !IsFinite(cameraInvVP))
            return false;

        view = new World3DView(cameraVP, cameraInvVP, eyeCell, originAnchorCell,
            new Vector4(eyeLocal, camera.Near), new Vector4(width, height, 1f / width, 1f / height));
        return true;
    }

    /// <summary>Creates a view payload for a cell-relative orthographic shadow or capture pass.</summary>
    /// <param name="viewProj">The row-major linear Forward-Z [0, 1] orthographic matrix relative to <paramref name="eyeCell"/> and <paramref name="eyeLocal"/>.</param>
    /// <param name="eyeCell">The snapped cell anchor for this shadow/capture frustum.</param>
    /// <param name="eyeLocal">The cell-local anchor offset (xyz).</param>
    /// <param name="resolution">The square target resolution in texels.</param>
    /// <param name="view">Receives the constructed view payload when <paramref name="viewProj"/> is invertible and finite.</param>
    /// <returns>True when the orthographic transform is finite and invertible.</returns>
    public static bool TryCreateOrthographic(in Matrix4x4 viewProj, World3DCell eyeCell, Vector3 eyeLocal, int resolution,
        out World3DView view)
    {
        view = default;
        if (resolution <= 0 || !IsFinite(viewProj) || !Matrix4x4.Invert(viewProj, out Matrix4x4 invVP) || !IsFinite(invVP))
            return false;
        float size = resolution;
        float invSize = 1f / size;
        view = new World3DView(viewProj, invVP, eyeCell, eyeCell,
            new Vector4(eyeLocal, 0f), new Vector4(size, size, invSize, invSize));
        return true;
    }

    private static bool IsWithinAnchorAxis(double value, int anchorCell)
    {
        const int cellSize = World3DSceneCoordinates.CellSize;
        const double rebaseDistance = 2 * cellSize;
        double anchor = (double)anchorCell * cellSize;
        return value > anchor - rebaseDistance && value < anchor + rebaseDistance;
    }

    private static bool IsFinite(in Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}

/// <summary>Splits world coordinates and packs camera-independent instance spatial data.</summary>
public static class World3DSceneCoordinates
{
    /// <summary>The size of a grid cell in world units.</summary>
    public const int CellSize = 512;
    /// <summary>The lowest supported cell index; any two supported cell indices can be subtracted in a signed 32-bit lane.</summary>
    public const int MinCell = -(1 << 30);
    /// <summary>The highest supported cell index, inclusive.</summary>
    public const int MaxCell = (1 << 30) - 1;

    /// <summary>Splits a position into floor-quantized cells and residuals rounded to float after double subtraction.</summary>
    /// <param name="position">The world position to split.</param>
    /// <param name="cell">The grid cell on success; otherwise default.</param>
    /// <param name="local">The cell-local position on success; otherwise default. A residual may round up to <see cref="CellSize"/>.</param>
    /// <returns>Whether each coordinate is finite and in the supported cell range.</returns>
    public static bool TrySplitPosition(World3DPosition position, out World3DCell cell, out Vector3 local)
    {
        cell = default;
        local = default;
        if (!TrySplitAxis(position.X, out int x, out float localX)
            || !TrySplitAxis(position.Y, out int y, out float localY)
            || !TrySplitAxis(position.Z, out int z, out float localZ))
            return false;

        cell = new World3DCell(x, y, z);
        local = new Vector3(localX, localY, localZ);
        return true;
    }

    /// <summary>Packs a transform and conservative bounds independently of any view or origin anchor.</summary>
    /// <param name="transform">The object-to-world transform.</param>
    /// <param name="localMin">The minimum corner of the union of the object's geometry bounds.</param>
    /// <param name="localMax">The maximum corner of the union of the object's geometry bounds.</param>
    /// <param name="spatial">The packed data on success; otherwise default.</param>
    /// <returns>Whether the transform and ordered bounds can be represented in the spatial payload.</returns>
    public static bool TryPackInstance(in World3DTransform transform, Vector3 localMin, Vector3 localMax, out World3DInstanceSpatialGpu spatial)
    {
        spatial = default;
        Vector3 scale = transform.Scale;
        if (!IsFinite(scale) || scale.X == 0f || scale.Y == 0f || scale.Z == 0f
            || !IsFinite(localMin) || !IsFinite(localMax) || localMin.X > localMax.X || localMin.Y > localMax.Y || localMin.Z > localMax.Z
            || !TrySplitPosition(transform.Position, out World3DCell cell, out Vector3 local)
            || !World3DSpatialMath.TryNormalizeRotation(transform.Rotation, out Quaternion rotation))
            return false;

        Matrix4x4 objectToCell = math.matrix4trs(local, rotation, scale);
        if (!TryPackBoundsAxis(localMin, localMax, objectToCell.M11, objectToCell.M21, objectToCell.M31, out float cx, out float ex)
            || !TryPackBoundsAxis(localMin, localMax, objectToCell.M12, objectToCell.M22, objectToCell.M32, out float cy, out float ey)
            || !TryPackBoundsAxis(localMin, localMax, objectToCell.M13, objectToCell.M23, objectToCell.M33, out float cz, out float ez))
            return false;

        float radius = RoundUp(Math.Sqrt((double)ex * ex + (double)ey * ey + (double)ez * ez));
        if (!float.IsFinite(radius))
            return false;

        spatial = new World3DInstanceSpatialGpu(objectToCell, cell, new Vector4(cx, cy, cz, radius), new Vector4(ex, ey, ez, 0f));
        return true;
    }

    private static bool TrySplitAxis(double value, out int cell, out float local)
    {
        cell = default;
        local = default;
        double index = Math.Floor(value / CellSize);
        if (!(index >= MinCell && index <= MaxCell))
            return false;

        // A negative subnormal can underflow to negative zero when divided by CellSize.
        if (index == 0 && value < 0)
            index = -1;

        cell = (int)index;
        local = value == 0 ? 0 : (float)(value - index * CellSize);
        return true;
    }

    private static bool TryPackBoundsAxis(Vector3 localMin, Vector3 localMax, float x, float y, float z, out float center, out float extent)
    {
        // Select the extrema by sign using the packed linear transform, without its pivot translation.
        double min = (double)x * (x >= 0f ? localMin.X : localMax.X)
            + (double)y * (y >= 0f ? localMin.Y : localMax.Y)
            + (double)z * (z >= 0f ? localMin.Z : localMax.Z);
        double max = (double)x * (x >= 0f ? localMax.X : localMin.X)
            + (double)y * (y >= 0f ? localMax.Y : localMin.Y)
            + (double)z * (z >= 0f ? localMax.Z : localMin.Z);
        center = (float)((min + max) * 0.5);
        // Enclose the center's rounding and the GPU's three float products and two sums.
        double magnitude = Math.Abs((double)x) * Math.Max(Math.Abs(localMin.X), Math.Abs(localMax.X))
            + Math.Abs((double)y) * Math.Max(Math.Abs(localMin.Y), Math.Abs(localMax.Y))
            + Math.Abs((double)z) * Math.Max(Math.Abs(localMin.Z), Math.Abs(localMax.Z));
        const double dotProductRoundoff = 1.0 / (1 << 21);
        extent = RoundUp(Math.Max(center - min, max - center) + dotProductRoundoff * magnitude);
        return magnitude <= float.MaxValue && float.IsFinite(center) && float.IsFinite(extent);
    }

    private static float RoundUp(double value)
    {
        float rounded = (float)value;
        return rounded < value ? MathF.BitIncrement(rounded) : rounded;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

file static class World3DSpatialMath
{
    /// <summary>Normalizes a finite, nonzero rotation without overflowing or underflowing its length.</summary>
    public static bool TryNormalizeRotation(Quaternion rotation, out Quaternion normalized)
    {
        normalized = default;
        float max = MathF.Max(MathF.Max(MathF.Abs(rotation.X), MathF.Abs(rotation.Y)), MathF.Max(MathF.Abs(rotation.Z), MathF.Abs(rotation.W)));
        if (!float.IsFinite(max) || max == 0f)
            return false;

        normalized = Quaternion.Normalize(new Quaternion(rotation.X / max, rotation.Y / max, rotation.Z / max, rotation.W / max));
        return true;
    }
}
