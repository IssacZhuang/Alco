#language slang 2025
module World3DCullSelect;

import Core;
import World3DSceneData;

[GPURoot]
public struct CullSelectRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView Selection;
    public uint InstanceCount;
    public uint GroupsX;
    public float MaximumDistance;
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

bool world3d_inside_frustum(World3DView view, float3 center, float3 extent)
{
    float4 x = world3d_column(view.CameraVP, 0u);
    float4 y = world3d_column(view.CameraVP, 1u);
    float4 z = world3d_column(view.CameraVP, 2u);
    float4 w = world3d_column(view.CameraVP, 3u);
    // Infinite reversed depth has no finite far plane; the near half-space is z <= w.
    return !world3d_outside_plane(w + x, center, extent) && !world3d_outside_plane(w - x, center, extent)
        && !world3d_outside_plane(w + y, center, extent) && !world3d_outside_plane(w - y, center, extent)
        && !world3d_outside_plane(w - z, center, extent);
}

uint world3d_select_lod(CullSelectRoot root, World3DView view, uint instanceSlot)
{
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, instanceSlot);
    if ((instance.Flags & WORLD3D_INSTANCE_LIVE) == 0u)
        return NO_LOD;
    World3DInstanceSpatialGpu spatial = instance.Spatial;
    float3 center = world3d_eye_relative_pivot(spatial, view) + spatial.BoundsCenterRadius.xyz;
    if (!world3d_inside_frustum(view, center, spatial.BoundsExtent.xyz))
        return NO_LOD;
    float distanceToCenter = length(center);
    float nearDistance = distanceToCenter - spatial.BoundsCenterRadius.w;
    if (root.MaximumDistance > 0.0 && nearDistance - distanceToCenter * 2e-6 > root.MaximumDistance)
        return NO_LOD;

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
        if (lod.MinimumDistance <= distanceToCenter && (selected == NO_LOD || lod.MinimumDistance > selectedThreshold))
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
    World3DView view = gpu_load<World3DView>(root.Views, group.z);
    gpu_store<uint>(root.Selection, group.z * root.InstanceCount + slot, world3d_select_lod(root, view, slot));
}
