#language slang 2025
module World3DPointShadowCull;

import Core;
import World3DSceneData;

public struct PointShadowCandidateGpu
{
    public uint InstanceSlot;
    public uint BatchIndex;
};

public struct PointShadowBatchGpu
{
    public uint PageId;
    public uint ViewIndex;
    public uint FirstCommand;
    public uint Capacity;
};

struct PointShadowIndexedDrawArgs
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    int VertexOffset;
    uint FirstInstance;
};

struct PointShadowDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

[GPURoot]
public struct World3DPointShadowCullRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView Candidates;
    public GPUBufferView Batches;
    public GPUBufferView Args;
    public GPUBufferView DrawRefs;
    public GPUBufferView Counters;
    public uint CandidateCount;
    public uint BatchCount;
    public uint Clear;
    public uint Reserved;
};

float4 pointShadowColumn(row_major float4x4 matrix, uint index)
{
    return float4(matrix[0][index], matrix[1][index], matrix[2][index], matrix[3][index]);
}

bool pointShadowOutsidePlane(float4 plane, float3 center, float3 extent)
{
    float3 n = abs(plane.xyz);
    float support = dot(n, extent);
    float guard = (dot(n, abs(center)) + support + abs(plane.w)) * 2e-6;
    return dot(plane, float4(center, 1.0)) < -support - guard;
}

bool pointShadowInsideFace(World3DView view, float3 center, float3 extent)
{
    float4 x = pointShadowColumn(view.CameraVP, 0u);
    float4 y = pointShadowColumn(view.CameraVP, 1u);
    float4 z = pointShadowColumn(view.CameraVP, 2u);
    float4 w = pointShadowColumn(view.CameraVP, 3u);
    return !pointShadowOutsidePlane(w + x, center, extent) && !pointShadowOutsidePlane(w - x, center, extent)
        && !pointShadowOutsidePlane(w + y, center, extent) && !pointShadowOutsidePlane(w - y, center, extent)
        && !pointShadowOutsidePlane(z, center, extent) && !pointShadowOutsidePlane(w - z, center, extent);
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void MainCS(uint3 thread : SV_DispatchThreadID)
{
    World3DPointShadowCullRoot root = gpu_load_root<World3DPointShadowCullRoot>();
    uint i = thread.x;
    if (i >= root.CandidateCount)
        return;

    // The graphics API submits each packet at its bounded capacity. Clear the unused tail of each range.
    if (root.Clear != 0u)
    {
        gpu_store<PointShadowIndexedDrawArgs>(root.Args, i, (PointShadowIndexedDrawArgs)0);
        if (i < root.BatchCount)
            gpu_store<uint>(root.Counters, i, 0u);
        return;
    }

    PointShadowCandidateGpu candidate = gpu_load<PointShadowCandidateGpu>(root.Candidates, i);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, candidate.InstanceSlot);
    uint requiredFlags = WORLD3D_INSTANCE_LIVE | WORLD3D_INSTANCE_CAST_SHADOW;
    if ((instance.Flags & requiredFlags) != requiredFlags)
        return;

    PointShadowBatchGpu batch = gpu_load<PointShadowBatchGpu>(root.Batches, candidate.BatchIndex);
    World3DView view = gpu_load<World3DView>(root.Views, batch.ViewIndex);
    float3 pivot = world3d_eye_relative_pivot(instance.Spatial, view);
    float3 offset = instance.Spatial.BoundsCenterRadius.xyz;
    float3 center = pivot + offset;
    float3 extent = instance.Spatial.BoundsExtent.xyz;
    float3 positionGuard = (abs(pivot) + abs(offset) + extent) * 2e-6 + 1e-4;
    if (!pointShadowInsideFace(view, center, extent + positionGuard))
        return;

    // Select the resident LOD using the lamp's distance, not a potentially culled primary camera view.
    float distanceToCenter = length(center);
    uint nearest = WORLD3D_MAX_GEOMETRY_LODS;
    uint selected = WORLD3D_MAX_GEOMETRY_LODS;
    float nearestThreshold = 0.0;
    float selectedThreshold = 0.0;
    World3DGeometryLodGpu nearestLod = (World3DGeometryLodGpu)0;
    World3DGeometryLodGpu selectedLod = (World3DGeometryLodGpu)0;
    [unroll]
    for (uint lodIndex = 0u; lodIndex < WORLD3D_MAX_GEOMETRY_LODS; ++lodIndex)
    {
        World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, lodIndex);
        if (lod.IndexCount == 0u)
            continue;
        if (nearest == WORLD3D_MAX_GEOMETRY_LODS || lod.MinimumDistance < nearestThreshold)
        {
            nearest = lodIndex;
            nearestThreshold = lod.MinimumDistance;
            nearestLod = lod;
        }
        if (lod.MinimumDistance <= distanceToCenter && (selected == WORLD3D_MAX_GEOMETRY_LODS || lod.MinimumDistance > selectedThreshold))
        {
            selected = lodIndex;
            selectedThreshold = lod.MinimumDistance;
            selectedLod = lod;
        }
    }
    if (selected == WORLD3D_MAX_GEOMETRY_LODS)
    {
        selected = nearest;
        selectedLod = nearestLod;
    }
    if (selectedLod.IndexCount == 0u || selectedLod.PageId != batch.PageId)
        return;

    uint offsetInBatch = gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, candidate.BatchIndex * 4u), 1u);
    if (offsetInBatch >= batch.Capacity)
        return;

    uint command = batch.FirstCommand + offsetInBatch;
    PointShadowIndexedDrawArgs args = (PointShadowIndexedDrawArgs)0;
    args.IndexCount = selectedLod.IndexCount;
    args.InstanceCount = 1u;
    args.FirstIndex = selectedLod.IndexByteOffset / 4u;
    PointShadowDrawRef drawRef;
    drawRef.InstanceSlot = candidate.InstanceSlot;
    drawRef.Lod = selected;
    gpu_store<PointShadowIndexedDrawArgs>(root.Args, command, args);
    gpu_store<PointShadowDrawRef>(root.DrawRefs, command, drawRef);
}
