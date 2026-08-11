#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// Half-resolution final gather. The world-space cache supplies stable distant
// and off-screen radiance. A four-direction screen-space near-field gather
// restores colored contact bounce that is smaller than a cache cell.

DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
DEFINE_TEX2D_READ(1, _albedo);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_READ(1, _mrAO);
DEFINE_TEX2D_READ(1, _emissive);
DEFINE_STORAGE(2, float4, _cacheRadiance);
DEFINE_TEX2D_STORAGE(3, _diffuseRaw, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(3, _specularRaw, float4, "rgba16f");

float4 LoadCacheValue(int3 cell, uint cascade)
{
    int resolution = (int)CacheResolution();
    if (any(cell < 0) || any(cell >= resolution))
    {
        return 0.0;
    }
    return _cacheRadiance[CacheLinearIndex((uint3)cell, cascade)];
}

float4 SampleCacheField(float3 worldPosition)
{
    [unroll]
    for (uint cascade = 0u; cascade < RC_CASCADE_COUNT; cascade++)
    {
        float4 origin = cacheOrigins[cascade];
        float3 grid = (worldPosition - origin.xyz) / origin.w - 0.5;
        int3 baseCell = (int3)floor(grid);
        float3 fraction = frac(grid);
        int resolution = (int)CacheResolution();
        if (any(baseCell < 0) || any(baseCell + 1 >= resolution))
        {
            continue;
        }
        float4 result = 0.0;
        [unroll]
        for (int z = 0; z <= 1; z++)
        {
            [unroll]
            for (int y = 0; y <= 1; y++)
            {
                [unroll]
                for (int x = 0; x <= 1; x++)
                {
                    float3 selector = float3(x, y, z);
                    float3 axisWeight = lerp(1.0 - fraction, fraction, selector);
                    result += LoadCacheValue(baseCell + int3(x, y, z), cascade)
                        * (axisWeight.x * axisWeight.y * axisWeight.z);
                }
            }
        }
        if (result.a > 0.002 || cascade == RC_CASCADE_COUNT - 1)
        {
            return result;
        }
    }
    return 0.0;
}

float3 GatherCacheDiffuse(float3 worldPosition, float3 normal, out float confidence)
{
    static const float3 directions[6] =
    {
        float3(1, 0, 0), float3(-1, 0, 0),
        float3(0, 1, 0), float3(0, -1, 0),
        float3(0, 0, 1), float3(0, 0, -1)
    };
    float cellSize = cacheOrigins[0].w;
    float3 origin = worldPosition + normal * cellSize * 0.8;
    float3 sum = 0.0;
    float weightSum = 0.0;
    confidence = 0.0;
    [unroll]
    for (int directionIndex = 0; directionIndex < 6; directionIndex++)
    {
        float weight = saturate(dot(normal, directions[directionIndex]));
        if (weight <= 0.0)
        {
            continue;
        }
        float4 value = SampleCacheField(origin + directions[directionIndex] * cellSize * 2.25);
        float weightedConfidence = value.a * weight;
        sum += value.rgb * weightedConfidence;
        weightSum += weightedConfidence;
        confidence += weightedConfidence;
    }
    confidence = saturate(confidence / 3.0);
    return weightSum > 1e-4 ? sum / weightSum * PI : 0.0;
}

float3 ApproximateScreenRadiance(int2 pixel, float3 normal)
{
    float3 albedo = DecodeCacheSRGB(GET_PIXEL_TEX2D(_albedo, pixel).rgb);
    float3 emissive = GET_PIXEL_TEX2D(_emissive, pixel).rgb * lightingParams.w;
    return albedo * CacheSkyIrradiance(normal) / PI + emissive;
}

float4 GatherScreenNearField(uint2 centerPixel, float2 centerUV, float3 worldPosition, float3 normal)
{
    uint2 viewport = (uint2)viewportParams.xy;
    float rotation = CacheHash(float2(centerPixel) + cacheParams.w) * TAU;
    float3 gathered = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int ray = 0; ray < 4; ray++)
    {
        float angle = rotation + ray * (TAU * 0.25);
        float2 direction = float2(cos(angle), sin(angle));
        [unroll]
        for (int stepIndex = 1; stepIndex <= 6; stepIndex++)
        {
            float radiusPixels = 2.0 + stepIndex * stepIndex * 1.5;
            float2 sampleUV = centerUV + direction * radiusPixels / float2(viewport);
            if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            {
                break;
            }
            int2 samplePixel = clamp((int2)(sampleUV * float2(viewport)), 0, (int2)viewport - 1);
            float depth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
            if (depth >= 0.9999)
            {
                continue;
            }
            float3 samplePosition = ReconstructCacheWorldPosition(sampleUV, depth);
            float3 toSample = samplePosition - worldPosition;
            float distanceToSample = length(toSample);
            if (distanceToSample < 0.05 || distanceToSample > traceParams.x)
            {
                continue;
            }
            float3 sampleDirection = toSample / distanceToSample;
            float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
            float weight = saturate(dot(normal, sampleDirection))
                * saturate(dot(sampleNormal, -sampleDirection))
                / (1.0 + distanceToSample * distanceToSample);
            if (weight < 0.005)
            {
                continue;
            }
            float4 cachedSource = SampleCacheField(samplePosition + sampleNormal * cacheOrigins[0].w);
            float3 sourceRadiance = max(ApproximateScreenRadiance(samplePixel, sampleNormal), cachedSource.rgb);
            gathered += sourceRadiance * weight;
            totalWeight += weight;
            break;
        }
    }
    return totalWeight > 0.0
        ? float4(gathered / totalWeight * PI, saturate(totalWeight))
        : 0.0;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 tracePixel = dispatchId.xy;
    uint2 traceSize = (uint2)viewportParams.zw;
    if (any(tracePixel >= traceSize))
    {
        return;
    }
    uint2 viewport = (uint2)viewportParams.xy;
    float2 uv = (float2(tracePixel) + 0.5) / float2(traceSize);
    uint2 pixel = min((uint2)(uv * float2(viewport)), viewport - 1u);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    if (depth >= 0.9999)
    {
        _diffuseRaw[tracePixel] = float4(0.0, 0.0, 0.0, 1e6);
        _specularRaw[tracePixel] = 0.0;
        return;
    }

    float3 worldPosition = ReconstructCacheWorldPosition(uv, depth);
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0);
    float3 viewDirection = normalize(cameraPosition.xyz - worldPosition);
    float confidence;
    float3 cacheDiffuse = GatherCacheDiffuse(worldPosition, normal, confidence);
    float4 nearField = GatherScreenNearField(pixel, uv, worldPosition, normal);
    float3 diffuse = CacheSkyIrradiance(normal)
        + cacheDiffuse * responseParams.z
        + nearField.rgb * 0.35;

    float roughness = GET_PIXEL_TEX2D(_mrAO, int2(pixel)).y;
    float3 reflection = reflect(-viewDirection, normal);
    float cellSize = cacheOrigins[0].w;
    float4 reflectedNear = SampleCacheField(worldPosition + normal * cellSize + reflection * cellSize * 2.0);
    float4 reflectedFar = SampleCacheField(worldPosition + normal * cellSize + reflection * cellSize * 6.0);
    float3 specular = lerp(reflectedNear.rgb, reflectedFar.rgb, saturate(roughness * roughness));
    float distanceToCamera = length(worldPosition - cameraPosition.xyz);
    _diffuseRaw[tracePixel] = float4(max(diffuse, 0.0), distanceToCamera);
    _specularRaw[tracePixel] = float4(max(specular, 0.0), saturate(max(confidence, nearField.a)));
}
