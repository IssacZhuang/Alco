#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// One Jacobi step spreads outgoing surface radiance into nearby empty cache
// cells. Surface cells remain fixed sources. Repeating this every frame turns
// the persistent cache into a low-frequency world-space radiance field while
// the screen-space gather supplies the missing near-field detail.

DEFINE_STORAGE(1, float4, _cacheRadianceIn);
DEFINE_STORAGE(1, float4, _cacheGeometryIn);
DEFINE_STORAGE(2, float4, _cacheRadianceOut);
DEFINE_STORAGE(2, float4, _cacheGeometryOut);

[shader("compute")]
[numthreads(256, 1, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    uint totalCellCount = (uint)cacheParams.z;
    if (index >= totalCellCount)
    {
        return;
    }

    uint cellsPerCascade = CacheCellsPerCascade();
    uint cascade = index / cellsPerCascade;
    uint3 cell = CacheCellFromLocalIndex(index - cascade * cellsPerCascade);
    float4 centerRadiance = _cacheRadianceIn[index];
    float4 centerGeometry = _cacheGeometryIn[index];
    if (centerGeometry.w > 0.05)
    {
        _cacheRadianceOut[index] = centerRadiance;
        _cacheGeometryOut[index] = centerGeometry;
        return;
    }

    static const int3 neighborOffsets[6] =
    {
        int3(1, 0, 0), int3(-1, 0, 0),
        int3(0, 1, 0), int3(0, -1, 0),
        int3(0, 0, 1), int3(0, 0, -1)
    };
    int resolution = (int)CacheResolution();
    float3 radianceSum = 0.0;
    float confidenceSum = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int neighbor = 0; neighbor < 6; neighbor++)
    {
        int3 neighborCell = (int3)cell + neighborOffsets[neighbor];
        if (any(neighborCell < 0) || any(neighborCell >= resolution))
        {
            continue;
        }
        float4 value = _cacheRadianceIn[CacheLinearIndex((uint3)neighborCell, cascade)];
        float weight = max(value.a, 0.05);
        radianceSum += value.rgb * weight;
        confidenceSum += value.a;
        weightSum += weight;
    }

    float3 propagated = weightSum > 0.0 ? radianceSum / weightSum : 0.0;
    float propagatedConfidence = confidenceSum / 6.0;
    float propagation = saturate(traceParams.z);
    float3 outputRadiance = lerp(centerRadiance.rgb, propagated * 0.92, propagation);
    float outputConfidence = max(centerRadiance.a * 0.98, propagatedConfidence * propagation * 0.92);
    _cacheRadianceOut[index] = float4(outputRadiance, outputConfidence);
    _cacheGeometryOut[index] = centerGeometry;
}
