#include "Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionPostCommon.hlsli"

DEFINE_TEX2D_SAMPLE(1, _reflectionRaw);
DEFINE_TEX2D_SAMPLE(1, _reflectionHistory);
DEFINE_TEX2D_READ(1, _albedo);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float2 fullSize = ssrRenderSize.xy;
    float2 traceSize = ssrRenderSize.zw;
    int2 centerPixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float centerDepth = GET_PIXEL_TEX2D(_gbufferDepth, centerPixel);
    if (centerDepth >= 0.9999)
    {
        return 0.0;
    }

    float3 centerNormal = normalize(GET_PIXEL_TEX2D(_normal, centerPixel).xyz * 2.0 - 1.0);
    float3 centerWorld = SsrPostReconstructWorldPosition(input.uv, centerDepth);
    float centerDistance = length(centerWorld - ssrCameraPosition.xyz);

    // Complementary-style normal-aware 5x5 spatial reflection filter.
    float3 colorSum = 0.0;
    float confidenceSum = 0.0;
    float weightSum = 0.0;
    float2 traceTexel = rcp(traceSize);
    [loop]
    for (int y = -2; y <= 2; y++)
    {
        [loop]
        for (int x = -2; x <= 2; x++)
        {
            float2 sampleUV = clamp(input.uv + float2(x, y) * traceTexel, 0.0, 1.0);
            float4 sampleReflection = SAMPLE_TEX2D(_reflectionRaw, sampleUV);
            int2 samplePixel = clamp(int2(sampleUV * fullSize), int2(0, 0), int2(fullSize) - 1);
            float sampleDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
            if (sampleDepth >= 0.9999)
            {
                continue;
            }

            float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
            float3 sampleWorld = SsrPostReconstructWorldPosition(sampleUV, sampleDepth);
            float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), 24.0);
            float depthScale = 0.08 + centerDistance * 0.015;
            float depthWeight = exp(-length(sampleWorld - centerWorld) / depthScale);
            float spatialWeight = exp(-float(x * x + y * y) / 8.0);
            float weight = normalWeight * depthWeight * spatialWeight;

            colorSum += sampleReflection.rgb * sampleReflection.a * weight;
            confidenceSum += sampleReflection.a * weight;
            weightSum += weight;
        }
    }

    float confidence = confidenceSum / max(weightSum, 1e-5);
    float3 color = confidenceSum > 1e-5
        ? colorSum / confidenceSum
        : 0.0;

    if (ssrParams.y > 0.5)
    {
        float4 previousClip = mul(ssrPreviousViewProjection, float4(centerWorld, 1.0));
        if (previousClip.w > 0.0)
        {
            float2 previousNdc = previousClip.xy / previousClip.w;
            float2 previousUV = float2(previousNdc.x * 0.5 + 0.5, 0.5 - previousNdc.y * 0.5);
            if (all(previousUV >= 0.0) && all(previousUV <= 1.0))
            {
                float4 history = SAMPLE_TEX2D(_reflectionHistory, previousUV);
                float roughness = GET_PIXEL_TEX2D(_albedo, centerPixel).a;
                float smoothness = 1.0 - roughness;
                float minimumBlend = 0.035 + 0.09 * pow(smoothness, 8.0);
                bool currentHit = confidence > 0.01;
                float colorBlend = currentHit
                    ? max(minimumBlend, 1.0 - history.a)
                    : 0.0;
                float confidenceBlend = currentHit ? 0.20 : 0.35;
                color = lerp(history.rgb, color, colorBlend);
                confidence = lerp(history.a, confidence, confidenceBlend);
            }
        }
    }

    return float4(max(color, 0.0), saturate(confidence));
}
