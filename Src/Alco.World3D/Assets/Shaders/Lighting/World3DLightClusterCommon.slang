#language slang 2025
module World3DLightClusterCommon;

import Core;
import World3DSceneData;
import World3DPointShadowSample;

public static const uint WORLD3D_CLUSTER_FINE_PER_AXIS = 4u;
public static const uint WORLD3D_CLUSTER_DEPTH_SLICES = 24u;
public static const uint WORLD3D_CLUSTER_CHILDREN = 64u;
public static const uint WORLD3D_CLUSTER_WIDE_TILE = 8u;
public static const uint WORLD3D_CLUSTER_COARSE_LIMIT = 128u;
public static const uint WORLD3D_CLUSTER_FINE_LIMIT = 32u;

public struct World3DLightClusterGridGpu
{
    public uint TileSize; // Power of two in pixels, or zero to disable cluster access.
    public uint CoarseTilesX;
    public uint CoarseTilesY;
    public float LogNear;
    public float SlicesPerLog2Unit;
    public float FarDepth;
};

public struct World3DLightClusterFrameGpu
{
    public GPUBufferView Lights;
    public GPUBufferView CoarseLights; // 128 candidate indices per coarse cluster, in score order.
    public GPUBufferView FineMasks;    // One uint4 per child, indexed in coarse-major order.
    public World3DLightClusterGridGpu Grid;
};

// Prefix of the coarse buffer, populated once per frame by Refine.
public struct World3DLocalLightingFrameGpu
{
    public World3DLightClusterFrameGpu Clusters;
    public World3DPointShadowFrameGpu PointShadows;
};

public struct World3DLightClusterCandidateGpu
{
    public uint Slot;
    public uint ShadowIndexPlusOne;
};

public struct World3DClusterLightGpu
{
    public float4 EyeRelativePositionAndRadius;
    public float4 ColorAndIntensity;
    public float SourceRadius;
    public uint ShadowIndexPlusOne;
    public uint Reserved0;
    public uint Reserved1;
};

public struct World3DLightClusterCountersGpu
{
    public uint CoarseOverflowClusters;
    public uint CoarseDroppedLights;
    public uint FineOverflowClusters;
    public uint FineDroppedLights; // Counts fine losses among the retained coarse candidates only.
};

public struct World3DLightClusterIterator
{
    uint4 Mask;
    uint Word;
    uint CoarseBase;
};

[ForceInline]
public float world3d_local_light_view_depth(World3DView view, float3 eyeRelativePosition)
{
    // The main perspective view's clip W is signed distance along the camera's forward axis.
    return mul(float4(eyeRelativePosition, 0.0), view.CameraVP).w;
}

[ForceInline]
public uint world3d_light_cluster_slice(World3DLightClusterGridGpu grid, float depth)
{
    float slice = (log2(depth) - grid.LogNear) * grid.SlicesPerLog2Unit;
    return uint(clamp(slice, 0.0, float(WORLD3D_CLUSTER_DEPTH_SLICES - 1u)));
}

[ForceInline]
public uint world3d_light_cluster_coarse_index(World3DLightClusterGridGpu grid, uint3 coarse)
{
    return (coarse.z * grid.CoarseTilesY + coarse.y) * grid.CoarseTilesX + coarse.x;
}

[ForceInline]
public uint2 world3d_light_cluster_bin_extent(World3DLightClusterGridGpu grid)
{
    uint2 size = uint2(grid.CoarseTilesX, grid.CoarseTilesY);
    return size + (size + WORLD3D_CLUSTER_WIDE_TILE - 1u) / WORLD3D_CLUSTER_WIDE_TILE;
}

[ForceInline]
public World3DLightClusterIterator world3d_begin_local_lights(World3DLightClusterFrameGpu frame, uint2 pixel, float viewDepth)
{
    World3DLightClusterIterator iterator;
    iterator.Mask = uint4(0u);
    iterator.Word = 4u;
    iterator.CoarseBase = 0u;
    if (frame.Grid.TileSize == 0u || !(viewDepth > 0.0 && viewDepth <= frame.Grid.FarDepth))
        return iterator;

    const uint span = WORLD3D_CLUSTER_FINE_PER_AXIS;
    uint tileShift = uint(firstbithigh(frame.Grid.TileSize));
    uint3 fine = uint3(pixel >> tileShift, world3d_light_cluster_slice(frame.Grid, viewDepth));
    uint3 coarse = fine / span;
    uint3 child = fine % span;
    uint coarseIndex = world3d_light_cluster_coarse_index(frame.Grid, coarse);
    uint childIndex = (child.z * span + child.y) * span + child.x;
    iterator.Mask = gpu_load<uint4>(frame.FineMasks, coarseIndex * WORLD3D_CLUSTER_CHILDREN + childIndex);
    iterator.Word = any(iterator.Mask != 0u) ? 0u : 4u;
    iterator.CoarseBase = coarseIndex * WORLD3D_CLUSTER_COARSE_LIMIT;
    return iterator;
}

[ForceInline]
public bool world3d_try_next_local_light(World3DLightClusterFrameGpu frame, inout World3DLightClusterIterator iterator,
    out World3DClusterLightGpu light)
{
    for (; iterator.Word < 4u; ++iterator.Word)
    {
        uint bits = iterator.Mask[iterator.Word];
        if (bits == 0u)
            continue;

        uint index = iterator.CoarseBase + iterator.Word * 32u + uint(firstbitlow(bits));
        iterator.Mask[iterator.Word] = bits & (bits - 1u);
        uint candidate = gpu_load<uint>(frame.CoarseLights, index);
        light = gpu_load<World3DClusterLightGpu>(frame.Lights, candidate);
        return true;
    }
    light = (World3DClusterLightGpu)0;
    return false;
}
