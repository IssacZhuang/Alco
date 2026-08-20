using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Per-instance draw data shared by the G-buffer (<see cref="GBufferRenderer"/>),
/// CSM shadow and RSM (<see cref="ShadowRenderer"/>) passes. Every instanced
/// pass fetches this from the <c>_instances</c> storage buffer by
/// <c>SV_InstanceID</c>; the layout must match the <c>PbrInstance</c> struct in
/// PbrInstance.hlsli exactly. The bounds fields are dormant (zero today) and
/// reserved for the future GPU culling pass.
/// </summary>
public struct PbrInstanceData
{
    /// <summary>The world transform of the instance.</summary>
    public Matrix4x4 Model;
    /// <summary>Linear base color (rgb tints the albedo, w multiplies its alpha).</summary>
    public Vector4 BaseColor;
    /// <summary>x=metallic, y=roughness, z=ambient occlusion, w unused (G-buffer only).</summary>
    public Vector4 MetallicRoughnessAO;
    /// <summary>x=alpha test cutoff (0 disables the test), yzw unused.</summary>
    public Vector4 Params;
    /// <summary>Linear emissive color (rgb), w unused (G-buffer only).</summary>
    public Vector4 Emissive;
    /// <summary>World-space bounds center, w = bounding sphere radius. Reserved for GPU culling.</summary>
    public Vector4 BoundsCenter;
    /// <summary>World-space bounds half extents. Reserved for GPU culling.</summary>
    public Vector4 BoundsExtents;
}
