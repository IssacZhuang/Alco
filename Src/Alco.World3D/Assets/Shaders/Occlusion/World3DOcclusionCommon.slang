#language slang 2025
module World3DOcclusionCommon;

import Core;
import World3DSceneData;

// Projects an instance's axis-aligned cell bounds into Reversed-Z [1, 0] clip space.
// Returns false if any corner intersects or falls behind the near plane (w <= 0 or z >= w).
public bool world3d_project_aabb_reversed_z(
    World3DInstanceSpatialGpu spatial,
    int4 eyeCell,
    float3 eyeLocal,
    row_major float4x4 viewProj,
    out float2 uvMin,
    out float2 uvMax,
    out float maxDepth)
{
    uvMin = float2(1e30, 1e30);
    uvMax = float2(-1e30, -1e30);
    maxDepth = 0.0;

    float3 pivot = world3d_eye_relative_pivot_from_cell(spatial, eyeCell, eyeLocal);
    float3 center = pivot + spatial.BoundsCenterRadius.xyz;
    float3 extent = spatial.BoundsExtent.xyz;

    [unroll]
    for (uint i = 0u; i < 8u; ++i)
    {
        float3 corner = center + float3(
            (i & 1u) != 0u ? extent.x : -extent.x,
            (i & 2u) != 0u ? extent.y : -extent.y,
            (i & 4u) != 0u ? extent.z : -extent.z);
        float4 clip = mul(float4(corner, 1.0), viewProj);
        if (clip.w <= 1e-5 || clip.z >= clip.w)
            return false;
        float invW = rcp(clip.w);
        float2 ndc = clip.xy * invW;
        float2 uv = ndc * float2(0.5, -0.5) + float2(0.5, 0.5);
        float depth = saturate(clip.z * invW);
        uvMin = min(uvMin, uv);
        uvMax = max(uvMax, uv);
        maxDepth = max(maxDepth, depth);
    }
    return true;
}

// Conservative Hi-Z occlusion test against half-resolution R=min, G=max Reversed-Z pyramid.
// Handles arbitrary NPOT viewports via exact conservative 4-corner lookup across straddled texels.
public bool world3d_hiz_is_occluded(
    GPUTextureId hizTexture,
    uint2 viewportSize,
    uint2 hizMip0Size,
    uint hizMipCount,
    World3DInstanceSpatialGpu spatial,
    int4 eyeCell,
    float3 eyeLocal,
    row_major float4x4 viewProj,
    bool requireInsideViewport)
{
    if (hizTexture.index == 0u || hizMipCount == 0u ||
        viewportSize.x == 0u || viewportSize.y == 0u ||
        hizMip0Size.x == 0u || hizMip0Size.y == 0u)
        return false;

    float2 uvMin;
    float2 uvMax;
    float maxDepth;
    if (!world3d_project_aabb_reversed_z(spatial, eyeCell, eyeLocal, viewProj, uvMin, uvMax, maxDepth))
        return false;

    if (requireInsideViewport && (uvMin.x < 0.0 || uvMin.y < 0.0 || uvMax.x > 1.0 || uvMax.y > 1.0))
        return false;

    uvMin = saturate(uvMin);
    uvMax = saturate(uvMax);

    uint2 fullMax = viewportSize - 1u;
    uint2 tMin = min(uint2(floor(uvMin * float2(viewportSize))), fullMax);
    uint2 tMax = min(uint2(floor(uvMax * float2(viewportSize))), fullMax);
    uint2 sMin = tMin >> 1u;
    uint2 sMax = tMax >> 1u;
    uint span = max(sMax.x - sMin.x + 1u, sMax.y - sMin.y + 1u);
    uint mip = span <= 1u ? 0u : min(uint(ceil(log2(float(span)))), hizMipCount - 1u);
    uint2 mipSize = max(uint2(1u, 1u), hizMip0Size >> mip);
    uint2 pMin = min(sMin >> mip, mipSize - 1u);
    uint2 pMax = min(sMax >> mip, mipSize - 1u);

    Texture2D<float2> tex = gpu_texture<Texture2D<float2>>(hizTexture.index);
    float d00 = tex.Load(int3(int(pMin.x), int(pMin.y), int(mip))).r;
    float d10 = tex.Load(int3(int(pMax.x), int(pMin.y), int(mip))).r;
    float d01 = tex.Load(int3(int(pMin.x), int(pMax.y), int(mip))).r;
    float d11 = tex.Load(int3(int(pMax.x), int(pMax.y), int(mip))).r;
    float hizMin = min(min(d00, d10), min(d01, d11));

    return maxDepth + 1e-6 < hizMin;
}
