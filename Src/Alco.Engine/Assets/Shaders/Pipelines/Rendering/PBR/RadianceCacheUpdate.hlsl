#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// Reprojects the previous camera-following cache into the new snapped grids,
// resolves this frame's screen seeds, and retains off-screen entries. Clamping
// old radiance around the new measurement avoids the long light trails that an
// otherwise high DDGI-style hysteresis would produce after lighting changes.

DEFINE_STORAGE(1, uint, _accumRadiance);
DEFINE_STORAGE(1, uint, _accumNormal);
DEFINE_STORAGE(2, float4, _cacheRadianceIn);
DEFINE_STORAGE(2, float4, _cacheGeometryIn);
DEFINE_STORAGE(3, float4, _cacheRadianceOut);
DEFINE_STORAGE(3, float4, _cacheGeometryOut);

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
    float3 worldPosition = CacheCellWorldPosition(cell, cascade);

    float4 previousRadiance = 0.0;
    float4 previousGeometry = 0.0;
    int3 previousCell;
    if (traceParams.y > 0.5 && PreviousCacheWorldToCell(worldPosition, cascade, previousCell))
    {
        uint previousIndex = CacheLinearIndex((uint3)previousCell, cascade);
        previousRadiance = _cacheRadianceIn[previousIndex];
        previousGeometry = _cacheGeometryIn[previousIndex];
    }

    uint baseIndex = index * 4u;
    uint4 accumulated = uint4(
        _accumRadiance[baseIndex + 0u],
        _accumRadiance[baseIndex + 1u],
        _accumRadiance[baseIndex + 2u],
        _accumRadiance[baseIndex + 3u]);
    uint sampleCount = accumulated.w;
    if (sampleCount > 0u)
    {
        const float radianceScale = 1024.0;
        const float normalScale = 1024.0;
        float inverseCount = rcp((float)sampleCount);
        float3 measured = float3(accumulated.xyz) * (inverseCount / radianceScale);
        float3 measuredNormal = float3(
            _accumNormal[baseIndex + 0u],
            _accumNormal[baseIndex + 1u],
            _accumNormal[baseIndex + 2u]) * (inverseCount / normalScale) * 2.0 - 1.0;
        measuredNormal = dot(measuredNormal, measuredNormal) > 1e-5
            ? normalize(measuredNormal)
            : float3(0.0, 0.0, 1.0);

        float hysteresis = previousRadiance.a > 0.001 ? responseParams.x : 0.0;
        float3 clampedPrevious = clamp(previousRadiance.rgb,
            measured * 0.25 - 0.05, measured * 4.0 + 0.25);
        float3 resolved = lerp(measured, clampedPrevious, hysteresis);
        _cacheRadianceOut[index] = float4(resolved, 1.0);
        _cacheGeometryOut[index] = float4(measuredNormal, 1.0);
    }
    else
    {
        float retention = saturate(traceParams.w);
        _cacheRadianceOut[index] = float4(previousRadiance.rgb * retention, previousRadiance.a * retention);
        _cacheGeometryOut[index] = float4(previousGeometry.xyz, previousGeometry.w * retention);
    }
}
