#language slang 2025
module World3DCullSelect;

import Core;
import World3DSceneData;
import World3DOcclusionCommon;

[GPURoot]
public struct CullSelectRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView CullViews;
    public GPUBufferView Selection;
    public GPUBufferView Phase2Selection;
    public GPUBufferView OccludedCandidates;
    public GPUBufferView Counters;
    public GPUTextureId PrevHiZTexture;
    public uint InstanceCount;
    public uint GroupsX;
    public uint OcclusionFlags; // bit 0 = HiZ test enabled, bit 1 = TwoPhase active, bit 2 = HasPrevView
    public uint PrevHiZViewportWidth;
    public uint PrevHiZViewportHeight;
    public uint PrevHiZMip0Width;
    public uint PrevHiZMip0Height;
    public uint PrevHiZMipCount;
    public float MaximumDistance;
    public uint PrevViewSlot;
};

static const uint NO_LOD = 0xffffffffu;

float4 world3d_column(float4x4 matrix, uint index)
{
    return float4(matrix[0][index], matrix[1][index], matrix[2][index], matrix[3][index]);
}

bool world3d_outside_plane(float4 plane, float3 center, float3 extent)
{
    float3 n = abs(plane.xyz);
    float projectedExtent = dot(n, extent);
    float guard = (dot(n, abs(center)) + projectedExtent + abs(plane.w)) * 2e-6;
    return dot(plane, float4(center, 1.0)) < -projectedExtent - guard;
}

bool world3d_inside_frustum(row_major float4x4 vp, bool orthographic, float3 center, float3 extent)
{
    float4 x = world3d_column(vp, 0u);
    float4 y = world3d_column(vp, 1u);
    float4 z = world3d_column(vp, 2u);
    float4 w = world3d_column(vp, 3u);

    if (world3d_outside_plane(w + x, center, extent) || world3d_outside_plane(w - x, center, extent)
        || world3d_outside_plane(w + y, center, extent) || world3d_outside_plane(w - y, center, extent)
        || world3d_outside_plane(w - z, center, extent))
        return false;

    // Forward-Z orthographic views also enforce the z >= 0 near half-space.
    if (orthographic && world3d_outside_plane(z, center, extent))
        return false;

    return true;
}

uint world3d_select_lod(CullSelectRoot root, World3DCullViewGpu cullView, World3DInstanceGpu instance)
{
    if ((instance.Flags & WORLD3D_INSTANCE_LIVE) == 0u)
        return NO_LOD;

    if ((cullView.Flags & WORLD3D_CULL_VIEW_SHADOW_CASTER_ONLY) != 0u
        && (instance.Flags & WORLD3D_INSTANCE_CAST_SHADOW) == 0u)
        return NO_LOD;

    World3DInstanceSpatialGpu spatial = instance.Spatial;
    if (cullView.MinObjectRadius > 0.0 && spatial.BoundsCenterRadius.w < cullView.MinObjectRadius)
        return NO_LOD;

    float3 center = world3d_eye_relative_pivot_from_cell(spatial, cullView.EyeCell, cullView.EyeLocalNear.xyz)
        + spatial.BoundsCenterRadius.xyz;
    bool orthographic = (cullView.Flags & WORLD3D_CULL_VIEW_ORTHOGRAPHIC) != 0u;
    if (!world3d_inside_frustum(cullView.ViewProj, orthographic, center, spatial.BoundsExtent.xyz))
        return NO_LOD;

    float distanceToCenter = length(center);
    float nearDistance = distanceToCenter - spatial.BoundsCenterRadius.w;
    bool isPrimary = (cullView.Flags & WORLD3D_CULL_VIEW_IS_PRIMARY) != 0u;
    float maxDistance = isPrimary ? root.MaximumDistance
        : ((cullView.Flags & WORLD3D_CULL_VIEW_USE_MAX_DISTANCE) != 0u ? cullView.MaxDistance : 0.0);
    if (maxDistance > 0.0 && nearDistance - distanceToCenter * 2e-6 > maxDistance)
        return NO_LOD;

    float lodDistance = distanceToCenter + max(cullView.MinTexelLodBiasMeters, 0.0);
    uint nearest = NO_LOD;
    uint selected = NO_LOD;
    float nearestThreshold = 0.0;
    float selectedThreshold = 0.0;
    [unroll]
    for (uint i = 0u; i < WORLD3D_MAX_GEOMETRY_LODS; ++i)
    {
        World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, i);
        if (lod.IndexCount == 0u)
            continue;
        if (nearest == NO_LOD || lod.MinimumDistance < nearestThreshold)
        {
            nearest = i;
            nearestThreshold = lod.MinimumDistance;
        }
        if (lod.MinimumDistance <= lodDistance && (selected == NO_LOD || lod.MinimumDistance > selectedThreshold))
        {
            selected = i;
            selectedThreshold = lod.MinimumDistance;
        }
    }
    return selected == NO_LOD ? nearest : selected;
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void MainCS(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    CullSelectRoot root = gpu_load_root<CullSelectRoot>();
    uint slot = (group.y * root.GroupsX + group.x) * 64u + lane;
    if (slot >= root.InstanceCount)
        return;

    World3DCullViewGpu cullView = gpu_load<World3DCullViewGpu>(root.CullViews, group.z);
    if ((cullView.Flags & WORLD3D_CULL_VIEW_ENABLED) == 0u)
        return;

    bool isPrimary = (cullView.Flags & WORLD3D_CULL_VIEW_IS_PRIMARY) != 0u;
    bool twoPhase = (root.OcclusionFlags & 2u) != 0u;
    if (isPrimary && twoPhase)
        gpu_store<uint>(root.Phase2Selection, slot, NO_LOD);

    World3DInstanceGpu instance = world3d_load_instance(root.Directory, slot);
    uint selectedLod = world3d_select_lod(root, cullView, instance);

    if (selectedLod != NO_LOD && isPrimary && (root.OcclusionFlags & 1u) != 0u)
    {
        bool occluded = false;
        if ((cullView.Flags & WORLD3D_CULL_VIEW_HIZ_OCCLUSION) != 0u)
        {
            if ((root.OcclusionFlags & 4u) != 0u)
            {
                World3DView prevView = gpu_load<World3DView>(root.Views, root.PrevViewSlot);
                occluded = world3d_hiz_is_occluded(
                    root.PrevHiZTexture,
                    uint2(root.PrevHiZViewportWidth, root.PrevHiZViewportHeight),
                    uint2(root.PrevHiZMip0Width, root.PrevHiZMip0Height),
                    root.PrevHiZMipCount,
                    instance.Spatial,
                    prevView.EyeCell,
                    prevView.EyeLocalNear.xyz,
                    prevView.CameraVP,
                    true);
            }
            else
            {
                occluded = world3d_hiz_is_occluded(
                    root.PrevHiZTexture,
                    uint2(root.PrevHiZViewportWidth, root.PrevHiZViewportHeight),
                    uint2(root.PrevHiZMip0Width, root.PrevHiZMip0Height),
                    root.PrevHiZMipCount,
                    instance.Spatial,
                    cullView.EyeCell,
                    cullView.EyeLocalNear.xyz,
                    cullView.ViewProj,
                    true);
            }
        }

        if (occluded)
        {
            if (twoPhase)
            {
                uint idx = gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 0u), 1u);
                if (idx < root.InstanceCount)
                {
                    World3DOccludedCandidate oc;
                    oc.InstanceSlot = slot;
                    oc.Lod = selectedLod;
                    gpu_store<World3DOccludedCandidate>(root.OccludedCandidates, idx, oc);
                }
            }
            selectedLod = NO_LOD;
        }
        else if (twoPhase)
        {
            gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 4u), 1u);
        }
    }
    else if (selectedLod != NO_LOD && isPrimary && (root.OcclusionFlags & 2u) != 0u)
    {
        gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 4u), 1u);
    }

    gpu_store<uint>(root.Selection, cullView.TargetViewSlot * root.InstanceCount + slot, selectedLod);
}
