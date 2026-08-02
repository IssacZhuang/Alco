#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Camera-relative cascaded DDGI update. Each probe traces a small rotating ray
// set through the freshly injected voxel radiance and stores first-order RGB
// spherical harmonics. Four SH coefficient slabs for every cascade share one
// compact 3D texture. Established probes are phase-updated and temporally
// filtered; newly exposed probes update immediately after clipmap scrolling.

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_TEX3D_READ(2, _ddgiHistory);
DEFINE_TEX3D_STORAGE(3, _ddgiOutput, float4, "rgba16f");

static const uint DDGI_COEFFICIENT_COUNT = 4u;
static const uint DDGI_RAY_COUNT = 12u;

float4 DdgiOrigin(int cascade)
{
    return ddgiOrigins[cascade];
}

float4 DdgiPreviousOrigin(int cascade)
{
    return ddgiPreviousOrigins[cascade];
}

uint3 DdgiStorageCoord(uint3 probe, uint cascade, uint coefficient)
{
    uint probeDepth = (uint)ddgiParams.z;
    return uint3(probe.xy, probe.z + probeDepth * (coefficient + DDGI_COEFFICIENT_COUNT * cascade));
}

bool TryGetHistoryProbe(float3 worldPosition, int cascade, out uint3 previousProbe)
{
    float4 previousOrigin = DdgiPreviousOrigin(cascade);
    int3 coordinate = (int3)round((worldPosition - previousOrigin.xyz) / previousOrigin.w);
    int3 resolution = (int3)ddgiParams.xyz;
    if (ddgiParams2.x < 0.5 || any(coordinate < 0) || any(coordinate >= resolution))
    {
        previousProbe = uint3(0u, 0u, 0u);
        return false;
    }

    previousProbe = (uint3)coordinate;
    return true;
}

float3 DdgiRayDirection(uint rayIndex, uint probeIndex, uint frameIndex)
{
    float sequence = (rayIndex + 0.5) / (float)DDGI_RAY_COUNT;
    float z = 1.0 - 2.0 * sequence;
    float radius = sqrt(saturate(1.0 - z * z));
    float rotation = frac(
        rayIndex * 0.61803398875
        + probeIndex * 0.1031
        + frameIndex * 0.754877666);
    float angle = TAU * rotation;
    return float3(radius * cos(angle), radius * sin(angle), z);
}

float4 DdgiTraceRay(float3 startPosition, float3 direction, float maximumDistance)
{
    float t = levelOrigins[0].w * 1.5;
    [loop]
    for (int step = 0; step < 32 && t < maximumDistance; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        float voxelSize = levelOrigins[level].w;
        float diameter = max(voxelSize, t * 0.08);
        float mip = clamp(log2(diameter / voxelSize), 0.0, clipmapParams.z - 1.0);
        float4 sample_ = SAMPLE_TEX3D_LEVEL(_radiance, VoxelWorldToUVW(position, level, mip), mip);
        if (sample_.a > 0.12)
        {
            return float4(sample_.rgb, t);
        }
        t += max(voxelSize, diameter * 0.75);
    }

    return float4(VoxelSkyColor(direction), maximumDistance);
}

[shader("compute")]
[numthreads(4, 4, 2)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint3 probeResolution = (uint3)ddgiParams.xyz;
    uint cascade = dispatchId.z / probeResolution.z;
    uint3 probe = uint3(dispatchId.xy, dispatchId.z % probeResolution.z);
    if (cascade >= (uint)ddgiParams2.w || any(probe >= probeResolution))
    {
        return;
    }

    float4 origin = DdgiOrigin((int)cascade);
    float3 worldPosition = origin.xyz + float3(probe) * origin.w;
    uint3 historyProbe;
    bool hasHistory = TryGetHistoryProbe(worldPosition, (int)cascade, historyProbe);
    float dirtyExpansion = origin.w * 2.0;
    bool locallyInvalidated = ddgiDirtyMin.w > 0.5
        && all(worldPosition >= ddgiDirtyMin.xyz - dirtyExpansion)
        && all(worldPosition <= ddgiDirtyMax.xyz + dirtyExpansion);
    hasHistory = hasHistory && !locallyInvalidated;

    uint probeIndex = probe.x
        + probe.y * probeResolution.x
        + probe.z * probeResolution.x * probeResolution.y
        + cascade * probeResolution.x * probeResolution.y * probeResolution.z;
    uint updatePeriod = max((uint)ddgiParams2.y, 1u);
    bool updateProbe = !hasHistory || ((probeIndex + (uint)ddgiParams.w) % updatePeriod) == 0u;
    if (!updateProbe)
    {
        [unroll]
        for (uint coefficient = 0u; coefficient < DDGI_COEFFICIENT_COUNT; coefficient++)
        {
            _ddgiOutput[DdgiStorageCoord(probe, cascade, coefficient)] =
                LOAD_TEX3D(_ddgiHistory, DdgiStorageCoord(historyProbe, cascade, coefficient), 0);
        }
        return;
    }

    float3 coefficients[DDGI_COEFFICIENT_COUNT] = {
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0) };
    float meanDistance = 0.0;
    float meanDistanceSquared = 0.0;
    // One world-space ray budget for every cascade (based on the finest
    // spacing): scaling it with the cascade's own spacing would let coarser
    // cascades escape to the sky more often, baking a systematic near-dark /
    // far-bright gradient into the cascade boundaries.
    float maximumDistance = DdgiOrigin(0).w * 10.0;
    uint frameIndex = (uint)ddgiParams.w;
    [unroll]
    for (uint ray = 0u; ray < DDGI_RAY_COUNT; ray++)
    {
        float3 direction = DdgiRayDirection(ray, probeIndex, frameIndex);
        float4 traced = DdgiTraceRay(worldPosition, direction, maximumDistance);
        float4 basis = float4(0.282095, 0.488603 * direction.y, 0.488603 * direction.z, 0.488603 * direction.x);
        float weight = 4.0 * PI / (float)DDGI_RAY_COUNT;
        [unroll]
        for (uint coefficient = 0u; coefficient < DDGI_COEFFICIENT_COUNT; coefficient++)
        {
            coefficients[coefficient] += traced.rgb * basis[coefficient] * weight;
        }
        meanDistance += traced.a / (float)DDGI_RAY_COUNT;
        meanDistanceSquared += traced.a * traced.a / (float)DDGI_RAY_COUNT;
    }

    float4 outputCoefficients[DDGI_COEFFICIENT_COUNT] = {
        float4(coefficients[0], meanDistance),
        float4(coefficients[1], meanDistanceSquared),
        float4(coefficients[2], 1.0),
        float4(coefficients[3], 0.0) };

    if (hasHistory)
    {
        float hysteresis = ddgiParams2.z;
        [unroll]
        for (uint coefficient = 0u; coefficient < DDGI_COEFFICIENT_COUNT; coefficient++)
        {
            float4 history = LOAD_TEX3D(
                _ddgiHistory,
                DdgiStorageCoord(historyProbe, cascade, coefficient),
                0);
            outputCoefficients[coefficient] = lerp(outputCoefficients[coefficient], history, hysteresis);
        }
    }

    [unroll]
    for (uint coefficient = 0u; coefficient < DDGI_COEFFICIENT_COUNT; coefficient++)
    {
        _ddgiOutput[DdgiStorageCoord(probe, cascade, coefficient)] = outputCoefficients[coefficient];
    }
}
