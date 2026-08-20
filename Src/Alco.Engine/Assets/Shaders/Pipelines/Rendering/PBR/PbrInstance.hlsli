#ifndef PBR_INSTANCE_HLSLI
#define PBR_INSTANCE_HLSLI

// Per-instance draw data shared by the G-buffer (GBuffer.hlsl), CSM shadow
// (ShadowDepth.hlsl) and RSM (Rsm.hlsl) passes. The layout must match
// Alco.Rendering.PbrInstanceData exactly. Vertex shaders fetch by
// SV_InstanceID; pixel shaders re-read per-instance scalars through the
// instance id interpolant (the SpriteInstanced.hlsl pattern).
struct PbrInstance
{
    float4x4 model;
    float4 baseColor;           // rgb = linear tint, a = alpha multiplier
    float4 metallicRoughnessAO; // x=metallic y=roughness z=ao (G-buffer only)
    float4 params_;             // x = alphaCutoff (0 disables the test)
    float4 emissive;            // rgb = linear emissive factor (G-buffer only)
    float4 boundsCenter;        // world AABB center, w = sphere radius (reserved for GPU culling)
    float4 boundsExtents;       // world AABB half extents (reserved for GPU culling)
};

#endif // PBR_INSTANCE_HLSLI
