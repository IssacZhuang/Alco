#language slang 2025
module World3DVoxelCullInstances;

import Core;
import World3DSceneData;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelCullInstancesRoot
{
    public int4 OriginAnchorCell;
    public GPUBufferView Directory;
    public GPUBufferView Cascades;
    public GPUBufferView StaticDirtyMask;
    public GPUBufferView DynamicDesiredMask;
    public GPUBufferView CandidateList;
    public GPUBufferView Counters;
    public uint InstanceCount;
    public uint MaxCandidates;
    public uint BricksPerAxis;
    public uint DynamicLevelCount;
    public uint HasStaticDirty;
    public uint EnableDynamic;
    public uint Reserved0;
    public uint Reserved1;
};

uint selectVoxelLod(GPUBufferView directory, uint geometrySlot, uint cascade)
{
    uint minLod = cascade == 0u ? 1u : 2u;
    uint bestLod = WORLD3D_MAX_GEOMETRY_LODS;
    for (uint lod = 0u; lod < WORLD3D_MAX_GEOMETRY_LODS; ++lod)
    {
        World3DGeometryLodGpu g = world3d_load_geometry_lod(directory, geometrySlot, lod);
        if (g.IndexCount == 0u)
            continue;
        bestLod = lod;
        if (lod >= minLod)
            break;
    }
    return bestLod;
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelCullInstancesRoot root = gpu_load_root<VoxelCullInstancesRoot>();
    uint slot = id.x;
    if (slot >= root.InstanceCount)
        return;

    World3DInstanceGpu inst = world3d_load_instance(root.Directory, slot);
    if ((inst.Flags & WORLD3D_INSTANCE_LIVE) == 0u
        || (inst.Flags & WORLD3D_INSTANCE_AFFECTS_GI) == 0u
        || inst.Fade <= 0.001)
        return;

    bool isDynamic = (inst.Flags & WORLD3D_INSTANCE_DYNAMIC) != 0u;
    if (isDynamic && (root.EnableDynamic == 0u || root.DynamicLevelCount == 0u))
        return;
    if (!isDynamic && root.HasStaticDirty == 0u)
        return;

    float3 anchorPivot = float3(inst.Spatial.Cell.xyz - root.OriginAnchorCell.xyz) * WORLD3D_CELL_SIZE
        + inst.Spatial.ObjectToCell[3].xyz;
    float3 boundsMin = anchorPivot + inst.Spatial.BoundsCenterRadius.xyz - inst.Spatial.BoundsExtent.xyz;
    float3 boundsMax = anchorPivot + inst.Spatial.BoundsCenterRadius.xyz + inst.Spatial.BoundsExtent.xyz;

    int maxBpa = int(root.BricksPerAxis) - 1;
    uint maxC = isDynamic ? min(root.DynamicLevelCount, WORLD3D_VOXEL_CASCADE_COUNT) : WORLD3D_VOXEL_CASCADE_COUNT;

    for (uint c = 0u; c < maxC; ++c)
    {
        World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(root.Cascades, c);
        float brickMeters = cGpu.OriginVoxelSize.w * float(WORLD3D_VOXEL_BRICK_SIZE);
        float invBrickMeters = rcp(brickMeters);

        int3 minB = int3(floor((boundsMin - cGpu.OriginVoxelSize.xyz) * invBrickMeters));
        int3 maxB = int3(floor((boundsMax - cGpu.OriginVoxelSize.xyz) * invBrickMeters));
        if (any(maxB < 0) || any(minB > maxBpa))
            continue;

        minB = clamp(minB, int3(0), int3(maxBpa));
        maxB = clamp(maxB, int3(0), int3(maxBpa));

        bool touched = false;
        if (isDynamic)
        {
            touched = true;
            for (int z = minB.z; z <= maxB.z; ++z)
            {
                for (int y = minB.y; y <= maxB.y; ++y)
                    voxel_or_local_row(root.DynamicDesiredMask, c, minB.x, maxB.x, uint(y), uint(z), root.BricksPerAxis);
            }
        }
        else
        {
            for (int z = minB.z; z <= maxB.z && !touched; ++z)
            {
                for (int y = minB.y; y <= maxB.y; ++y)
                {
                    if (voxel_test_local_row(root.StaticDirtyMask, c, minB.x, maxB.x, uint(y), uint(z), root.BricksPerAxis))
                    {
                        touched = true;
                        break;
                    }
                }
            }
        }

        if (!touched)
            continue;

        uint lod = selectVoxelLod(root.Directory, inst.GeometrySlot, c);
        if (lod >= WORLD3D_MAX_GEOMETRY_LODS)
            continue;

        World3DGeometryLodGpu geom = world3d_load_geometry_lod(root.Directory, inst.GeometrySlot, lod);
        uint triCount = geom.IndexCount / 3u;
        if (triCount == 0u)
            continue;

        uint candSlot = gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, VOXEL_COUNTER_CANDIDATE_INSTANCES * 4u), 1u);
        if (candSlot < root.MaxCandidates)
        {
            World3DVoxelCandidateGpu cand;
            cand.InstanceSlot = slot;
            cand.CascadeIndex = c;
            cand.Lod = lod;
            cand.TriangleCount = triCount;
            gpu_store<World3DVoxelCandidateGpu>(root.CandidateList, candSlot, cand);
        }
    }
}
