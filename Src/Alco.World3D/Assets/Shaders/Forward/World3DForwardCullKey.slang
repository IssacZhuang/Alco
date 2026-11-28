#language slang 2025
module World3DForwardCullKey;

import Core;
import World3DSceneData;
import World3DShadowSample;
import World3DOcclusionCommon;

[GPURoot]
public struct ForwardCullKeyRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUBufferView Candidates;
    public GPUBufferView SortKeys;
    public GPUBufferView Counters;
    public uint CandidateCount;
    public uint PaddedCount;
    public uint Flags;             // bit 0 = HiZ occlusion enabled, bit 1 = SortFrontToBack
    public float MaximumDistance;
};

static const uint FORWARD_NO_LOD = 0xFFFFFFFFu;

uint selectForwardLod(GPUBufferView directory, uint geometrySlot, float lodDistance)
{
    uint nearest = FORWARD_NO_LOD;
    uint selected = FORWARD_NO_LOD;
    float nearestThreshold = 0.0;
    float selectedThreshold = 0.0;
    [unroll]
    for (uint i = 0u; i < WORLD3D_MAX_GEOMETRY_LODS; ++i)
    {
        World3DGeometryLodGpu lod = world3d_load_geometry_lod(directory, geometrySlot, i);
        if (lod.IndexCount == 0u)
            continue;
        if (nearest == FORWARD_NO_LOD || lod.MinimumDistance < nearestThreshold)
        {
            nearest = i;
            nearestThreshold = lod.MinimumDistance;
        }
        if (lod.MinimumDistance <= lodDistance && (selected == FORWARD_NO_LOD || lod.MinimumDistance > selectedThreshold))
        {
            selected = i;
            selectedThreshold = lod.MinimumDistance;
        }
    }
    return selected == FORWARD_NO_LOD ? nearest : selected;
}

float4 forwardCol(float4x4 m, uint col)
{
    return float4(m[0][col], m[1][col], m[2][col], m[3][col]);
}

bool forwardOutsidePlane(float4 plane, float3 center, float3 extent)
{
    float3 n = abs(plane.xyz);
    float projectedExtent = dot(n, extent);
    float guard = (dot(n, abs(center)) + projectedExtent + abs(plane.w)) * 2e-6;
    return dot(plane, float4(center, 1.0)) < -projectedExtent - guard;
}

bool forwardInsideFrustum(row_major float4x4 vp, float3 center, float3 extent)
{
    float4 x = forwardCol(vp, 0u);
    float4 y = forwardCol(vp, 1u);
    float4 z = forwardCol(vp, 2u);
    float4 w = forwardCol(vp, 3u);

    return !forwardOutsidePlane(w + x, center, extent)
        && !forwardOutsidePlane(w - x, center, extent)
        && !forwardOutsidePlane(w + y, center, extent)
        && !forwardOutsidePlane(w - y, center, extent)
        && !forwardOutsidePlane(w - z, center, extent);
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    ForwardCullKeyRoot root = gpu_load_root<ForwardCullKeyRoot>();
    uint slot = id.x;
    if (slot >= root.PaddedCount)
        return;

    if (slot >= root.CandidateCount)
    {
        gpu_store<uint2>(root.SortKeys, slot, uint2(0xFFFFFFFFu, 0xFFFFFFFFu));
        return;
    }

    uint2 cand = gpu_load<uint2>(root.Candidates, slot);
    uint instanceSlot = cand.x;
    uint bucketIndex = min(cand.y, 254u);
    uint culledKey = (bucketIndex << 24u) | 0x00FFFFFFu;

    World3DInstanceGpu instance = world3d_load_instance(root.Directory, instanceSlot);
    if ((instance.Flags & WORLD3D_INSTANCE_LIVE) == 0u || instance.Fade <= 0.001)
    {
        gpu_store<uint2>(root.SortKeys, slot, uint2(culledKey, 0xFFFFFFFFu));
        return;
    }

    World3DView view = gpu_load<World3DView>(root.Views, 0);
    World3DInstanceSpatialGpu spatial = instance.Spatial;
    float3 center = world3d_eye_relative_pivot_from_cell(spatial, view.EyeCell, view.EyeLocalNear.xyz)
        + spatial.BoundsCenterRadius.xyz;

    if (!forwardInsideFrustum(view.CameraVP, center, spatial.BoundsExtent.xyz))
    {
        gpu_store<uint2>(root.SortKeys, slot, uint2(culledKey, 0xFFFFFFFFu));
        return;
    }

    float distanceToCenter = length(center);
    float nearDistance = distanceToCenter - spatial.BoundsCenterRadius.w;
    if (root.MaximumDistance > 0.0 && nearDistance - distanceToCenter * 2e-6 > root.MaximumDistance)
    {
        gpu_store<uint2>(root.SortKeys, slot, uint2(culledKey, 0xFFFFFFFFu));
        return;
    }

    uint selectedLod = selectForwardLod(root.Directory, instance.GeometrySlot, distanceToCenter);
    if (selectedLod == FORWARD_NO_LOD)
    {
        gpu_store<uint2>(root.SortKeys, slot, uint2(culledKey, 0xFFFFFFFFu));
        return;
    }

    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0);
    if ((root.Flags & 1u) != 0u && (shadowFrame.Flags & WORLD3D_SHADOW_FLAG_HIZ_ACTIVE) != 0u)
    {
        bool occluded = world3d_hiz_is_occluded(
            shadowFrame.HiZTex,
            uint2(view.ViewportSizeInvSize.xy),
            uint2(shadowFrame.HiZMip0Width, shadowFrame.HiZMip0Height),
            shadowFrame.HiZMipCount,
            spatial,
            view.EyeCell,
            view.EyeLocalNear.xyz,
            view.CameraVP,
            false);
        if (occluded)
        {
            gpu_store<uint2>(root.SortKeys, slot, uint2(culledKey, 0xFFFFFFFFu));
            return;
        }
    }

    uint distQuant = min(asuint(max(distanceToCenter, 0.0)) >> 7u, 0x00FFFFFEu);
    uint depth24 = (root.Flags & 2u) != 0u ? distQuant : (0x00FFFFFEu - distQuant);
    uint sortKey = (bucketIndex << 24u) | depth24;
    uint payload = (instanceSlot << 2u) | (selectedLod & 3u);
    gpu_store<uint2>(root.SortKeys, slot, uint2(sortKey, payload));

    gpu_atomic_add_u32(root.Counters, 1u);
}
