// module "pbr_instance"
//
// Per-instance draw data shared by the G-buffer (gbuffer.slang), CSM shadow
// (shadow_depth.slang) and RSM (rsm.slang) pass templates. The layout must
// match Alco.Rendering.PbrInstanceData exactly. Vertex shaders fetch by
// SV_InstanceID; pixel shaders re-read per-instance scalars through the
// instance id interpolant (the SpriteInstanced pattern).

public struct PbrInstance
{
    public float4x4 model;
    public float4 baseColor;           // rgb = linear tint, a = alpha multiplier
    public float4 metallicRoughnessAO; // x=metallic y=roughness z=ao (G-buffer only)
    public float4 params_;             // x = alphaCutoff (0 disables the test)
    public float4 emissive;            // rgb = linear emissive factor (G-buffer only)
    public float4 boundsCenter;        // world AABB center, w = sphere radius (reserved for GPU culling)
    public float4 boundsExtents;       // world AABB half extents (reserved for GPU culling)
}
