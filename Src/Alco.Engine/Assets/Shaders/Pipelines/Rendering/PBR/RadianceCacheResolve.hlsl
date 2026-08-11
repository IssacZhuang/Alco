#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// Full-resolution depth/normal-aware upsample followed by validated temporal
// reprojection. Diffuse alpha carries cache confidence to the lighting debug
// view; specular alpha stores camera distance for the next frame's history test.

DEFINE_TEX2D_READ(1, _diffuseRaw);
DEFINE_TEX2D_READ(1, _specularRaw);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_READ(2, _diffuseHistory);
DEFINE_TEX2D_READ(2, _specularHistory);
DEFINE_TEX2D_STORAGE(3, _diffuseOut, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(3, _specularOut, float4, "rgba16f");

void BilateralUpsample(uint2 pixel, float currentDistance, float3 currentNormal,
    out float3 diffuse, out float3 specular, out float confidence)
{
    uint2 viewport = (uint2)viewportParams.xy;
    uint2 traceSize = (uint2)viewportParams.zw;
    float2 tracePosition = (float2(pixel) + 0.5) / float2(viewport) * float2(traceSize) - 0.5;
    int2 baseTracePixel = (int2)floor(tracePosition);
    float3 diffuseSum = 0.0;
    float3 specularSum = 0.0;
    float confidenceSum = 0.0;
    float weightSum = 0.0;

    [unroll]
    for (int y = 0; y <= 1; y++)
    {
        [unroll]
        for (int x = 0; x <= 1; x++)
        {
            int2 tracePixel = clamp(baseTracePixel + int2(x, y), 0, (int2)traceSize - 1);
            float4 diffuseSample = GET_PIXEL_TEX2D(_diffuseRaw, tracePixel);
            float4 specularSample = GET_PIXEL_TEX2D(_specularRaw, tracePixel);
            float2 sampleUV = (float2(tracePixel) + 0.5) / float2(traceSize);
            int2 sourcePixel = clamp((int2)(sampleUV * float2(viewport)), 0, (int2)viewport - 1);
            float3 sourceNormal = normalize(GET_PIXEL_TEX2D(_normal, sourcePixel).xyz * 2.0 - 1.0);
            float depthWeight = exp2(-abs(diffuseSample.a - currentDistance)
                / max(currentDistance * 0.04, cacheOrigins[0].w * 0.5));
            float normalWeight = pow(saturate(dot(currentNormal, sourceNormal)), 8.0);
            float2 bilinearAxis = 1.0 - abs(tracePosition - float2(tracePixel));
            float bilinearWeight = saturate(bilinearAxis.x) * saturate(bilinearAxis.y);
            float weight = max(depthWeight * normalWeight * bilinearWeight, 0.0001);
            diffuseSum += diffuseSample.rgb * weight;
            specularSum += specularSample.rgb * weight;
            confidenceSum += specularSample.a * weight;
            weightSum += weight;
        }
    }
    diffuse = diffuseSum / weightSum;
    specular = specularSum / weightSum;
    confidence = confidenceSum / weightSum;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewport = (uint2)viewportParams.xy;
    if (any(pixel >= viewport))
    {
        return;
    }
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    if (depth >= 0.9999)
    {
        _diffuseOut[pixel] = float4(0.0, 0.0, 0.0, 1.0);
        _specularOut[pixel] = float4(0.0, 0.0, 0.0, 1e6);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewport);
    float3 worldPosition = ReconstructCacheWorldPosition(uv, depth);
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0);
    float currentDistance = length(worldPosition - cameraPosition.xyz);
    float3 currentDiffuse;
    float3 currentSpecular;
    float currentConfidence;
    BilateralUpsample(pixel, currentDistance, normal,
        currentDiffuse, currentSpecular, currentConfidence);

    float historyWeight = 0.0;
    float3 historyDiffuse = currentDiffuse;
    float3 historySpecular = currentSpecular;
    if (traceParams.y > 0.5)
    {
        float4 previousClip = mul(viewProjectionPrev, float4(worldPosition, 1.0));
        float2 previousNdc = previousClip.xy / previousClip.w;
        float2 previousUV = float2(previousNdc.x * 0.5 + 0.5, 0.5 - previousNdc.y * 0.5);
        if (previousClip.w > 0.0 && all(previousUV > 0.0) && all(previousUV < 1.0))
        {
            int2 historyPixel = clamp((int2)(previousUV * float2(viewport)), 0, (int2)viewport - 1);
            float4 previousDiffuse = GET_PIXEL_TEX2D(_diffuseHistory, historyPixel);
            float4 previousSpecular = GET_PIXEL_TEX2D(_specularHistory, historyPixel);
            float expectedPreviousDistance = length(worldPosition - previousCameraPosition.xyz);
            float distanceError = abs(previousSpecular.a - expectedPreviousDistance);
            float threshold = max(expectedPreviousDistance * 0.03, cacheOrigins[0].w * 0.5);
            if (distanceError < threshold)
            {
                historyDiffuse = clamp(previousDiffuse.rgb,
                    currentDiffuse * 0.25 - 0.05, currentDiffuse * 4.0 + 0.2);
                historySpecular = clamp(previousSpecular.rgb,
                    currentSpecular * 0.2 - 0.05, currentSpecular * 5.0 + 0.25);
                historyWeight = saturate(responseParams.y);
            }
        }
    }

    float3 resolvedDiffuse = lerp(currentDiffuse, historyDiffuse, historyWeight);
    float3 resolvedSpecular = lerp(currentSpecular, historySpecular, historyWeight);
    _diffuseOut[pixel] = float4(max(resolvedDiffuse, 0.0), saturate(currentConfidence));
    _specularOut[pixel] = float4(max(resolvedSpecular, 0.0), currentDistance);
}
