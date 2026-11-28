#language slang 2025
module World3DLightClusterRefine;

import Core;
import World3DLightClusterCommon;

[GPURoot]
public struct LightClusterRefineRoot
{
    public GPUBufferView Bins;
    public GPUBufferView Spheres;
    public GPUBufferView Counters;
    public GPUBufferView FrameData;
    public World3DLocalLightingFrameGpu Lighting;
    public float2 InverseProjectionScale;
    public float2 ViewportSize;
    public uint WordsPerBin;
    public uint Reserved0;
};

groupshared uint sPrefixOrFineDrops[WORLD3D_CLUSTER_CHILDREN];
groupshared uint sRetained;
groupshared float4 sSpheres[WORLD3D_CLUSTER_COARSE_LIMIT];

[ForceInline]
uint world3d_cluster_merge_bin_words(LightClusterRefineRoot root, uint3 coarse, uint word)
{
    if (word >= root.WordsPerBin)
        return 0u;

    World3DLightClusterGridGpu grid = root.Lighting.Clusters.Grid;
    uint2 extent = world3d_light_cluster_bin_extent(grid);
    uint2 wide = uint2(grid.CoarseTilesX, grid.CoarseTilesY) + coarse.xy / WORLD3D_CLUSTER_WIDE_TILE;
    uint row = (coarse.z * extent.y + coarse.y) * extent.x;
    uint wideRow = (coarse.z * extent.y + wide.y) * extent.x;
    return gpu_load<uint>(root.Bins, (row + coarse.x) * root.WordsPerBin + word)
        | gpu_load<uint>(root.Bins, (row + wide.x) * root.WordsPerBin + word)
        | gpu_load<uint>(root.Bins, (wideRow + coarse.x) * root.WordsPerBin + word)
        | gpu_load<uint>(root.Bins, (wideRow + wide.x) * root.WordsPerBin + word);
}

[ForceInline]
void world3d_cluster_retain_word(LightClusterRefineRoot root, uint word, uint bits, uint coarseBase, inout uint outputIndex)
{
    while (bits != 0u && outputIndex < WORLD3D_CLUSTER_COARSE_LIMIT)
    {
        uint candidate = word * 32u + uint(firstbitlow(bits));
        bits &= bits - 1u;
        gpu_store<uint>(root.Lighting.Clusters.CoarseLights, coarseBase + outputIndex, candidate);
        sSpheres[outputIndex] = gpu_load<float4>(root.Spheres, candidate);
        ++outputIndex;
    }
}

uint4 world3d_cluster_fine_mask(LightClusterRefineRoot root, uint3 coarse, uint childIndex, out uint dropped)
{
    const uint span = WORLD3D_CLUSTER_FINE_PER_AXIS;
    World3DLightClusterGridGpu grid = root.Lighting.Clusters.Grid;
    uint3 fine = coarse * span + uint3(childIndex % span, (childIndex / span) % span, childIndex / (span * span));
    float2 pixelMin = float2(fine.xy * grid.TileSize);
    dropped = 0u;
    if (any(pixelMin >= root.ViewportSize))
        return uint4(0u);

    float2 pixelMax = min(pixelMin + float(grid.TileSize), root.ViewportSize);
    float2 ndcMinPixel = (pixelMin / root.ViewportSize) * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float2 ndcMaxPixel = (pixelMax / root.ViewportSize) * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 slope = float4(ndcMinPixel.x, ndcMaxPixel.x, ndcMaxPixel.y, ndcMinPixel.y) * root.InverseProjectionScale.xxyy;
    float4 planeLength = sqrt(1.0 + slope * slope);
    const float4 inward = float4(1.0, -1.0, 1.0, -1.0);

    float2 sliceLogBounds = grid.LogNear + float2(fine.z, fine.z + 1u) / grid.SlicesPerLog2Unit;
    float depthMin = fine.z == 0u ? 0.0 : exp2(sliceLogBounds.x);
    float depthMax = (fine.z == WORLD3D_CLUSTER_DEPTH_SLICES - 1u) ? grid.FarDepth : exp2(sliceLogBounds.y);
    depthMin *= 1.0 - (1.0 / 524288.0);
    depthMax *= 1.0 + (1.0 / 524288.0);

    uint hits = 0u;
    uint4 mask = uint4(0u);
    for (uint i = 0u; i < sRetained; ++i)
    {
        float4 sphere = sSpheres[i];
        if (sphere.z + sphere.w < depthMin || sphere.z - sphere.w > depthMax)
            continue;

        // Each side plane rejects only spheres strictly outside it; grazing and corner cases stay conservative.
        float4 signedDistance = (sphere.xxyy - slope * sphere.z) * inward;
        if (any(signedDistance < -sphere.w * planeLength))
            continue;

        if (hits < WORLD3D_CLUSTER_FINE_LIMIT)
            mask[i / 32u] |= 1u << (i % 32u);
        ++hits;
    }
    dropped = hits - min(hits, WORLD3D_CLUSTER_FINE_LIMIT);
    return mask;
}

[shader("compute")]
[numthreads(64, 1, 1)]
void MainCS(uint3 coarse : SV_GroupID, uint lane : SV_GroupIndex)
{
    LightClusterRefineRoot root = gpu_load_root<LightClusterRefineRoot>();
    World3DLightClusterFrameGpu clusters = root.Lighting.Clusters;
    uint coarseIndex = world3d_light_cluster_coarse_index(clusters.Grid, coarse);
    uint fineIndex = coarseIndex * WORLD3D_CLUSTER_CHILDREN + lane;
    if (fineIndex == 0u)
        gpu_store<World3DLocalLightingFrameGpu>(root.FrameData, 0u, root.Lighting);

    uint evenWord = lane * 2u;
    uint evenBits = world3d_cluster_merge_bin_words(root, coarse, evenWord);
    uint oddBits = world3d_cluster_merge_bin_words(root, coarse, evenWord + 1u);
    sPrefixOrFineDrops[lane] = countbits(evenBits) + countbits(oddBits);
    GroupMemoryBarrierWithGroupSync();

    // Prefix the <=64 pairs of word populations without assuming a hardware subgroup width.
    if (lane == 0u)
    {
        uint total = 0u;
        uint pairs = (root.WordsPerBin + 1u) / 2u;
        for (uint i = 0u; i < pairs; ++i)
        {
            uint count = sPrefixOrFineDrops[i];
            sPrefixOrFineDrops[i] = total;
            total += count;
        }
        sRetained = min(total, WORLD3D_CLUSTER_COARSE_LIMIT);
        if (total > WORLD3D_CLUSTER_COARSE_LIMIT)
        {
            gpu_atomic_add_u32(root.Counters, 1u); // CoarseOverflowClusters
            gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 4u), total - WORLD3D_CLUSTER_COARSE_LIMIT);
        }
    }
    GroupMemoryBarrierWithGroupSync();

    if (sRetained == 0u)
    {
        gpu_store<uint4>(clusters.FineMasks, fineIndex, uint4(0u));
        return;
    }

    uint coarseBase = coarseIndex * WORLD3D_CLUSTER_COARSE_LIMIT;
    uint outputIndex = sPrefixOrFineDrops[lane];
    world3d_cluster_retain_word(root, evenWord, evenBits, coarseBase, outputIndex);
    world3d_cluster_retain_word(root, evenWord + 1u, oddBits, coarseBase, outputIndex);
    GroupMemoryBarrierWithGroupSync();

    uint fineDrops;
    uint4 mask = world3d_cluster_fine_mask(root, coarse, lane, fineDrops);
    gpu_store<uint4>(clusters.FineMasks, fineIndex, mask);
    if (sRetained <= WORLD3D_CLUSTER_FINE_LIMIT)
        return;

    sPrefixOrFineDrops[lane] = fineDrops;
    GroupMemoryBarrierWithGroupSync();
    if (lane != 0u)
        return;

    uint fineOverflows = 0u;
    uint totalFineDrops = 0u;
    for (uint i = 0u; i < WORLD3D_CLUSTER_CHILDREN; ++i)
    {
        uint dropped = sPrefixOrFineDrops[i];
        fineOverflows += uint(dropped != 0u);
        totalFineDrops += dropped;
    }
    if (fineOverflows != 0u)
    {
        gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 8u), fineOverflows);
        gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 12u), totalFineDrops);
    }
}
