#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// Seeds every cache cascade from visible G-buffer surfaces. Fixed-point atomic
// accumulation makes writes deterministic when many pixels land in one cell.
// The previous cache is sampled as incoming light, so repeated updates converge
// toward multi-bounce diffuse transport instead of storing direct light only.

DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
DEFINE_TEX2D_READ(1, _albedo);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_READ(1, _emissive);
DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);

struct PointLightData
{
    float4 positionRange;
    float4 colorIntensity;
};

DEFINE_STORAGE(2, PointLightData, _pointLights);
DEFINE_STORAGE(2, float4, _cacheRadiance);
DEFINE_STORAGE(3, uint, _accumRadiance);
DEFINE_STORAGE(3, uint, _accumNormal);

float3 LoadCacheTrilinear(float3 worldPosition, uint cascade, out float confidence)
{
    // Injection runs before cache reprojection, so the read buffer still uses
    // the previous frame's snapped origin.
    float4 origin = previousCacheOrigins[cascade];
    float3 grid = (worldPosition - origin.xyz) / origin.w - 0.5;
    int3 baseCell = (int3)floor(grid);
    float3 fraction = frac(grid);
    int resolution = (int)CacheResolution();
    float3 result = 0.0;
    confidence = 0.0;

    [unroll]
    for (int z = 0; z <= 1; z++)
    {
        [unroll]
        for (int y = 0; y <= 1; y++)
        {
            [unroll]
            for (int x = 0; x <= 1; x++)
            {
                int3 cell = baseCell + int3(x, y, z);
                if (any(cell < 0) || any(cell >= resolution))
                {
                    continue;
                }
                float3 selector = float3(x, y, z);
                float3 axisWeight = lerp(1.0 - fraction, fraction, selector);
                float weight = axisWeight.x * axisWeight.y * axisWeight.z;
                float4 sampleValue = _cacheRadiance[CacheLinearIndex((uint3)cell, cascade)];
                result += sampleValue.rgb * weight;
                confidence += sampleValue.a * weight;
            }
        }
    }
    return result;
}

float3 SamplePreviousBounce(float3 worldPosition, float3 normal)
{
    [unroll]
    for (uint cascade = 0u; cascade < RC_CASCADE_COUNT; cascade++)
    {
        float cellSize = previousCacheOrigins[cascade].w;
        int3 ignored;
        float3 samplePosition = worldPosition + normal * cellSize * 1.5;
        if (PreviousCacheWorldToCell(samplePosition, cascade, ignored))
        {
            float confidence;
            float3 radiance = LoadCacheTrilinear(samplePosition, cascade, confidence);
            if (confidence > 0.01)
            {
                return radiance * PI * saturate(confidence);
            }
        }
    }
    return 0.0;
}

float SampleCacheSunShadow(float3 worldPosition, float3 normal)
{
    if (lightingParams.x < 0.5)
    {
        return 1.0;
    }
    float viewDistance = length(worldPosition - cameraPosition.xyz);
    int cascade = -1;
    if (viewDistance < cascadeSplits.x) cascade = 0;
    else if (viewDistance < cascadeSplits.y) cascade = 1;
    else if (viewDistance < cascadeSplits.z) cascade = 2;
    else if (viewDistance < cascadeSplits.w) cascade = 3;
    if (cascade < 0)
    {
        return 1.0;
    }

    float3 biasedPosition = worldPosition + normal * cascadeTexelSizes[cascade];
    float4 clip = mul(sunViewProjection[cascade], float4(biasedPosition, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrant = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 uv = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrant;
    float bias = 0.0003 + 0.0015 * (1.0 - saturate(dot(normal, normalize(-sunDirection.xyz))));
    return SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, uv, ndc.z - bias);
}

float3 EvaluateCacheDirect(float3 worldPosition, float3 normal, uint2 pixel)
{
    float3 result = 0.0;
    float3 lightDirection = normalize(-sunDirection.xyz);
    float sunAmount = saturate(dot(normal, lightDirection));
    if (sunAmount > 0.0)
    {
        result += sunColorAndIntensity.rgb * sunColorAndIntensity.w * sunAmount
            * SampleCacheSunShadow(worldPosition, normal);
    }

    uint lightCount = min((uint)lightingParams.y, 64u);
    [loop]
    for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
    {
        PointLightData light = _pointLights[lightIndex];
        float3 toLight = light.positionRange.xyz - worldPosition;
        float distanceSquared = dot(toLight, toLight);
        float range = light.positionRange.w;
        if (distanceSquared >= range * range || distanceSquared < 1e-6)
        {
            continue;
        }
        float distanceToLight = sqrt(distanceSquared);
        float3 L = toLight / distanceToLight;
        float attenuation = saturate(1.0 - distanceToLight / range);
        attenuation = attenuation * attenuation / max(distanceSquared, 0.25);
        result += light.colorIntensity.rgb * light.colorIntensity.w
            * saturate(dot(normal, L)) * attenuation;
    }
    return result;
}

void AtomicAccumulate(uint index, float3 radiance, float3 normal)
{
    const float radianceScale = 1024.0;
    const float normalScale = 1024.0;
    uint3 packedRadiance = (uint3)round(min(max(radiance, 0.0), 64.0) * radianceScale);
    uint3 packedNormal = (uint3)round(saturate(normal * 0.5 + 0.5) * normalScale);
    uint baseIndex = index * 4u;
    InterlockedAdd(_accumRadiance[baseIndex + 0u], packedRadiance.x);
    InterlockedAdd(_accumRadiance[baseIndex + 1u], packedRadiance.y);
    InterlockedAdd(_accumRadiance[baseIndex + 2u], packedRadiance.z);
    InterlockedAdd(_accumRadiance[baseIndex + 3u], 1u);
    InterlockedAdd(_accumNormal[baseIndex + 0u], packedNormal.x);
    InterlockedAdd(_accumNormal[baseIndex + 1u], packedNormal.y);
    InterlockedAdd(_accumNormal[baseIndex + 2u], packedNormal.z);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    // One cache seed per 2x2 G-buffer block keeps atomic pressure bounded.
    uint2 pixel = dispatchId.xy * 2u + uint2((uint)cacheParams.w & 1u, ((uint)cacheParams.w >> 1u) & 1u);
    uint2 viewport = (uint2)viewportParams.xy;
    if (any(pixel >= viewport))
    {
        return;
    }
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    if (depth >= 0.9999)
    {
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewport);
    float3 worldPosition = ReconstructCacheWorldPosition(uv, depth);
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0);
    float3 albedo = DecodeCacheSRGB(GET_PIXEL_TEX2D(_albedo, int2(pixel)).rgb);
    float3 emissive = GET_PIXEL_TEX2D(_emissive, int2(pixel)).rgb * lightingParams.w;
    float3 incident = EvaluateCacheDirect(worldPosition, normal, pixel)
        + CacheSkyIrradiance(normal)
        + SamplePreviousBounce(worldPosition, normal) * responseParams.z;
    float3 outgoingRadiance = albedo * incident / PI + emissive;

    [unroll]
    for (uint cascade = 0u; cascade < RC_CASCADE_COUNT; cascade++)
    {
        int3 cell;
        if (CacheWorldToCell(worldPosition, cascade, cell))
        {
            AtomicAccumulate(CacheLinearIndex((uint3)cell, cascade), outgoingRadiance, normal);
        }
    }
}
