#language slang 2025
module World3DLightClusterBin;

import Core;
import World3DSceneData;
import World3DLightClusterCommon;

[GPURoot]
public struct LightClusterBinRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView Candidates;
    public GPUBufferView Lights;
    public GPUBufferView Spheres;
    public GPUBufferView Bins;
    public World3DLightClusterGridGpu Grid;
    public float2 InverseProjectionScale;
    public uint CandidateCount;
    public uint WordsPerBin;
};

// Cover a complete aligned span once rather than stamping it into each fine-grained coarse bin.
uint world3d_cluster_axis_bin(uint first, uint end, uint size, out uint next)
{
    const uint span = WORLD3D_CLUSTER_WIDE_TILE;
    uint spanEnd = min(first + span, size);
    bool useSpan = first % span == 0u && spanEnd <= end;
    next = useSpan ? spanEnd : first + 1u;
    return useSpan ? size + first / span : first;
}

// Quotients of the enclosing axis-aligned box bound the projected sphere even across the near plane.
bool world3d_cluster_try_project(float3 center, float radius, float2 depthRange, float2 inverseProjectionScale,
    float2 viewport, out float2 pixelMin, out float2 pixelMax)
{
    float2 lo = center.xy - radius;
    float2 hi = center.xy + radius;
    float2 ndcMin = (lo / select(lo < 0.0, depthRange.x, depthRange.y)) / inverseProjectionScale;
    float2 ndcMax = (hi / select(hi < 0.0, depthRange.y, depthRange.x)) / inverseProjectionScale;
    if (any(isnan(ndcMin)) || any(isnan(ndcMax)))
    {
        pixelMin = 0.0;
        pixelMax = viewport;
        return true;
    }

    // Allow a pixel of slack for coefficient rounding and rasterization at tile boundaries.
    pixelMin = (float2(ndcMin.x, -ndcMax.y) * 0.5 + 0.5) * viewport - 1.0;
    pixelMax = (float2(ndcMax.x, -ndcMin.y) * 0.5 + 0.5) * viewport + 1.0;
    return all(pixelMax >= 0.0) && all(pixelMin <= viewport);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    LightClusterBinRoot root = gpu_load_root<LightClusterBinRoot>();
    uint candidateIndex = id.x;
    if (candidateIndex >= root.CandidateCount)
        return;

    World3DLightClusterCandidateGpu candidate = gpu_load<World3DLightClusterCandidateGpu>(root.Candidates, candidateIndex);
    World3DLocalLightGpu source = world3d_load_local_light(root.Directory, candidate.Slot);
    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    float3 eyeRelative = float3(source.Cell.xyz - view.EyeCell.xyz) * WORLD3D_CELL_SIZE
        + (source.LocalPositionAndRadius.xyz - view.EyeLocalNear.xyz);
    float radius = max(source.LocalPositionAndRadius.w, 0.01);
    float3 center = float3(dot(eyeRelative, view.CameraInvVP[0].xyz / root.InverseProjectionScale.x),
        dot(eyeRelative, view.CameraInvVP[1].xyz / root.InverseProjectionScale.y),
        world3d_local_light_view_depth(view, eyeRelative));

    // Inflate only the bounds, not the shading radius, to absorb cell conversion and view roundoff.
    float magnitude = max(radius, max(abs(eyeRelative.x), max(abs(eyeRelative.y), abs(eyeRelative.z))));
    float boundsRadius = radius + magnitude * (1.0 / 524288.0);
    float2 depths = float2(max(view.EyeLocalNear.w, center.z - boundsRadius),
        min(root.Grid.FarDepth, center.z + boundsRadius));
    if (depths.y < depths.x)
        return;

    float2 viewport = view.ViewportSizeInvSize.xy;
    float2 pixelMin;
    float2 pixelMax;
    if (!world3d_cluster_try_project(center, boundsRadius, depths, root.InverseProjectionScale, viewport, pixelMin, pixelMax))
        return;

    World3DClusterLightGpu light = (World3DClusterLightGpu)0;
    light.EyeRelativePositionAndRadius = float4(eyeRelative, radius);
    light.ColorAndIntensity = source.ColorAndIntensity;
    light.SourceRadius = source.Shape.x;
    light.ShadowIndexPlusOne = candidate.ShadowIndexPlusOne;
    gpu_store<World3DClusterLightGpu>(root.Lights, candidateIndex, light);
    gpu_store<float4>(root.Spheres, candidateIndex, float4(center, boundsRadius));

    const uint finePerCoarse = WORLD3D_CLUSTER_FINE_PER_AXIS;
    uint2 coarseSize = uint2(root.Grid.CoarseTilesX, root.Grid.CoarseTilesY);
    uint coarseTileSize = root.Grid.TileSize * finePerCoarse;
    uint2 first = min(uint2(clamp(pixelMin, 0.0, viewport)) / coarseTileSize, coarseSize - 1u);
    uint2 end = min(uint2(clamp(pixelMax, 0.0, viewport)) / coarseTileSize + 1u, coarseSize);
    uint minZ = world3d_light_cluster_slice(root.Grid, depths.x) / finePerCoarse;
    uint maxZ = world3d_light_cluster_slice(root.Grid, depths.y) / finePerCoarse;

    uint2 binExtent = world3d_light_cluster_bin_extent(root.Grid);
    uint binLayerWords = binExtent.x * binExtent.y * root.WordsPerBin;
    uint word = candidateIndex / 32u;
    uint bit = 1u << (candidateIndex % 32u);
    for (uint y = first.y; y < end.y;)
    {
        uint nextY;
        uint binY = world3d_cluster_axis_bin(y, end.y, coarseSize.y, nextY);
        y = nextY;
        for (uint x = first.x; x < end.x;)
        {
            uint nextX;
            uint binX = world3d_cluster_axis_bin(x, end.x, coarseSize.x, nextX);
            x = nextX;
            uint offset = (binY * binExtent.x + binX) * root.WordsPerBin + word;
            for (uint z = minZ; z <= maxZ; ++z)
                gpu_atomic_or_u32(gpu_offset_bytes(root.Bins, (z * binLayerWords + offset) * 4u), bit);
        }
    }
}
