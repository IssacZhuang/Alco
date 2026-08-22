using System.Numerics;

namespace Alco.World3D;

/// <summary>
/// Per-cascade shadow data uploaded to the GPU once per frame and consumed by the
/// shadow depth shaders: the quadrant-folded light view-projection matrix of each
/// cascade. Layout must match the <c>_data</c> cbuffer in ShadowDepth.slang exactly.
/// </summary>
public struct ShadowCascadeData
{
    /// <summary>Light view-projection matrix of shadow cascade 0 (nearest).</summary>
    public Matrix4x4 CascadeViewProjection0;
    /// <summary>Light view-projection matrix of shadow cascade 1.</summary>
    public Matrix4x4 CascadeViewProjection1;
    /// <summary>Light view-projection matrix of shadow cascade 2.</summary>
    public Matrix4x4 CascadeViewProjection2;
    /// <summary>Light view-projection matrix of shadow cascade 3 (farthest).</summary>
    public Matrix4x4 CascadeViewProjection3;
}
