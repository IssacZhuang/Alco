#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// Half-resolution bilateral spatial filter and temporal accumulation for
// voxel GI. A geometry-aware diffuse footprint suppresses voxel sampling noise,
// then validated reprojection accumulates stable history without disocclusion
// trails. Specular uses a smaller footprint to preserve reflection detail.
//
// Both the indirect atlas (sampled by DeferredLighting) and a history texture
// (read by this shader next frame) are written in the same dispatch. The
// history texture has a third atlas section containing linear depth and world
// normal so reprojected samples can be rejected after disocclusion.

struct VoxelDemosaicConstants
{
    float4 params; // x=specularHysteresis, y=spatialSigma, z=diffuseHysteresis, w=unused
};

DEFINE_TEX2D_READ(1, _traceInput);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_STORAGE(4, _indirectGI, float4, "rgba16f");
DEFINE_TEX2D_READ(5, _historyInput);
DEFINE_TEX2D_STORAGE(6, _historyOut, float4, "rgba16f");
DEFINE_TEX2D_READ(7, _emissive);

PUSH_CONSTANT VoxelDemosaicConstants constants;

static const int2 FIREFLY_GUIDE_OFFSETS[4] = {
    int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1),
};

float3 ClampRadianceLuminance(float3 radiance, float maximumLuminance)
{
    radiance = max(radiance, 0.0);
    float luminance = dot(radiance, float3(0.2126, 0.7152, 0.0722));
    return radiance * min(1.0, maximumLuminance / max(luminance, 0.0001));
}

float3 ReconstructWorldPosition(float2 uv, float depth, float4x4 invVP)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invVP, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 atlasRes = uint2(giParams.z * 2u, giParams.w);
    if (any(dispatchId.xy >= atlasRes))
    {
        return;
    }

    uint2 pixel = dispatchId.xy;
    bool isSpecular = pixel.x >= giParams.z;
    int halfWidth = (int)giParams.z;

    // Map atlas pixel to the half-res trace pixel and then to full-res G-buffer.
    int2 tracePixel = isSpecular ? int2(pixel.x - halfWidth, pixel.y) : int2(pixel.xy);
    tracePixel = clamp(tracePixel, int2(0, 0), int2(halfWidth - 1, (int)giParams.w - 1));

    uint2 gbufferRes = uint2(giParams2.y, giParams2.z);
    float2 traceUV = (float2(tracePixel) + 0.5) / float2(giParams.z, giParams.w);
    int2 gbufferPixel = int2(traceUV * float2(gbufferRes));
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferRes);

    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        if (!isSpecular)
        {
            _historyOut[uint2(tracePixel.x + halfWidth * 2, tracePixel.y)] =
                float4(0.0, 0.0, 0.0, 0.0);
        }
        return;
    }

    float3 worldPos = ReconstructWorldPosition(gbufferUV, depth, invViewProjection);
    float4 packedNormal = GET_PIXEL_TEX2D(_normal, gbufferPixel);
    float packedGeometryY = GET_PIXEL_TEX2D(_emissive, gbufferPixel).a;
    float3 geometryNormal = DecodeGeometryNormal(float2(packedNormal.a, packedGeometryY));
    float3 N = isSpecular
        ? normalize(packedNormal.xyz * 2.0 - 1.0)
        : geometryNormal;
    float currentLinearDepth = abs(mul(viewProjection, float4(worldPos, 1.0)).w);
    float4 centerVal = _traceInput.Load(int3(pixel, 0));

    // Reject isolated HDR cone hits before they enter either the spatial or
    // temporal filter. The guide comes from the four nearest samples on the
    // same geometric surface, so real extended bounce lighting is retained
    // while a lone ray hitting a bright emissive voxel is luminance-clamped.
    float diffuseMaximumLuminance = 65504.0;
    if (!isSpecular)
    {
        float3 guideSum = 0.0;
        float guideWeightSum = 0.0;
        float guideDepthScale = 50.0 / max(constants.params.y * 2.0, 0.001);
        [unroll]
        for (uint guideIndex = 0u; guideIndex < 4u; guideIndex++)
        {
            int2 guideTrace = clamp(
                tracePixel + FIREFLY_GUIDE_OFFSETS[guideIndex],
                int2(0, 0),
                int2(halfWidth - 1, (int)giParams.w - 1));
            float2 guideUV = (float2(guideTrace) + 0.5) / float2(giParams.z, giParams.w);
            int2 guideGbufferPixel = int2(guideUV * float2(gbufferRes));
            guideGbufferPixel = clamp(
                guideGbufferPixel,
                int2(0, 0),
                int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float guideDepth = GET_PIXEL_TEX2D(_gbufferDepth, guideGbufferPixel);
            float guideWeight = exp(-abs(guideDepth - depth) * guideDepthScale)
                * (guideDepth < 0.9999 ? 1.0 : 0.0);
            guideSum += max(_traceInput.Load(int3(guideTrace, 0)).rgb, 0.0) * guideWeight;
            guideWeightSum += guideWeight;
        }

        float3 guideRadiance = guideWeightSum > 0.05
            ? guideSum / guideWeightSum
            : max(centerVal.rgb, 0.0);
        float guideLuminance = dot(guideRadiance, float3(0.2126, 0.7152, 0.0722));
        diffuseMaximumLuminance = clamp(guideLuminance * 4.0 + 0.02, 0.04, 8.0);
        centerVal.rgb = ClampRadianceLuminance(centerVal.rgb, diffuseMaximumLuminance);
    }

    // --- Bilateral spatial filter on the trace input ---
    // Stable mesh normals and a 7x7 diffuse footprint remove residual isolated
    // cone hits while preserving the original CE5 cone width and mip choice.
    // Specular keeps the sharper 3x3 footprint.
    int filterRadius = isSpecular ? 1 : 3;
    float spatialSigma = max(constants.params.y * (isSpecular ? 1.0 : 2.0), 0.001);
    float depthScale = 50.0 / spatialSigma;

    float4 spatialSum = centerVal;
    float spatialW = 1.0;
    float4 neighborhoodMin = centerVal;
    float4 neighborhoodMax = centerVal;

    [unroll]
    for (int dy = -3; dy <= 3; dy++)
    {
        [unroll]
        for (int dx = -3; dx <= 3; dx++)
        {
            if ((dx == 0 && dy == 0) || abs(dx) > filterRadius || abs(dy) > filterRadius)
            {
                continue;
            }

            int2 np = int2(pixel) + int2(dx, dy);
            // Keep within same atlas half (diffuse or specular).
            if (isSpecular)
            {
                np.x = clamp(np.x, halfWidth, (int)atlasRes.x - 1);
            }
            else
            {
                np.x = clamp(np.x, 0, halfWidth - 1);
            }
            np.y = clamp(np.y, 0, (int)atlasRes.y - 1);

            // G-buffer depth at the neighbour for bilateral weighting.
            int2 nTrace = isSpecular ? np - int2(halfWidth, 0) : np;
            float2 nUV = (float2(nTrace) + 0.5) / float2(giParams.z, giParams.w);
            int2 nGbufPixel = int2(nUV * float2(gbufferRes));
            nGbufPixel = clamp(nGbufPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float nDepth = GET_PIXEL_TEX2D(_gbufferDepth, nGbufPixel);

            float depthW = exp(-abs(nDepth - depth) * depthScale);
            float spatialW_neighbour = exp(-(dx * dx + dy * dy) / (2.0 * spatialSigma * spatialSigma));

            // Normal maps are high-frequency material detail, not a boundary
            // for low-frequency diffuse irradiance. Let depth preserve diffuse
            // geometry edges; retain normal rejection only for specular.
            float normalW = 1.0;
            if (isSpecular)
            {
                float3 nNormal = normalize(
                    GET_PIXEL_TEX2D(_normal, nGbufPixel).xyz * 2.0 - 1.0);
                normalW = pow(max(dot(N, nNormal), 0.0), 16.0);
            }

            float w = depthW * spatialW_neighbour * normalW;
            float4 neighbourValue = _traceInput.Load(int3(np, 0));
            if (!isSpecular)
            {
                neighbourValue.rgb = ClampRadianceLuminance(
                    neighbourValue.rgb, diffuseMaximumLuminance);
            }
            spatialSum += neighbourValue * w;
            spatialW += w;
            if (w > 0.001)
            {
                neighborhoodMin = min(neighborhoodMin, neighbourValue);
                neighborhoodMax = max(neighborhoodMax, neighbourValue);
            }
        }
    }
    float4 current = spatialSum / max(spatialW, 0.0001);

    // --- Temporal reprojection ---
    float4 result = current;
    bool historyAvailable = giFrameParams.z > 0.5;

    if (historyAvailable)
    {
        float4 prevClip = mul(viewProjectionPrev, float4(worldPos, 1.0));
        if (prevClip.w > 0.0)
        {
            float2 prevNDC = float2(prevClip.x / prevClip.w, prevClip.y / prevClip.w);
            float2 prevUV = float2(prevNDC.x * 0.5 + 0.5, 0.5 - prevNDC.y * 0.5);

            if (all(prevUV >= 0.0) && all(prevUV <= 1.0))
            {
                int2 previousTracePixel = int2(prevUV * float2(giParams.z, giParams.w));
                previousTracePixel = clamp(
                    previousTracePixel,
                    int2(0, 0),
                    int2(halfWidth - 1, (int)giParams.w - 1));

                // The third history section stores the surface that produced
                // the sample. Reprojection is accepted only when both linear
                // depth and world normal still match. This rejects history
                // revealed from behind an occluder during camera movement.
                int2 metadataPixel = previousTracePixel + int2(halfWidth * 2, 0);
                float4 historyMetadata = _historyInput.Load(int3(metadataPixel, 0));
                float expectedPreviousDepth = abs(prevClip.w);
                float depthDifference = abs(historyMetadata.x - expectedPreviousDepth);
                float depthTolerance = max(0.075, expectedPreviousDepth * 0.0075);
                float depthConfidence = 1.0 - saturate(depthDifference / depthTolerance);
                float3 historyNormal = normalize(historyMetadata.yzw * 2.0 - 1.0);
                float normalConfidence = saturate(
                    (dot(geometryNormal, historyNormal) - 0.8) * 5.0);
                float historyConfidence = depthConfidence * normalConfidence
                    * (historyMetadata.x > 0.0 ? 1.0 : 0.0);

                if (historyConfidence > 0.001)
                {
                    int2 histPixel = previousTracePixel
                        + (isSpecular ? int2(halfWidth, 0) : int2(0, 0));
                    float4 history = _historyInput.Load(int3(histPixel, 0));

                    if (isSpecular)
                    {
                        // Specular samples are deterministic enough that a large
                        // radiance change usually represents real motion.
                        float curLum = dot(current.rgb, float3(0.299, 0.587, 0.114));
                        float histLum = dot(history.rgb, float3(0.299, 0.587, 0.114));
                        float lumDiff = abs(curLum - histLum) / max(max(curLum, histLum), 0.001);
                        float visibilityDiff = abs(current.a - history.a);
                        float change = max(saturate(lumDiff * 3.0), saturate(visibilityDiff * 4.0));
                        float blendRate = max(
                            lerp(1.0 - constants.params.x, 1.0, change),
                            1.0 - historyConfidence);
                        result = lerp(history, current, blendRate);
                    }
                    else
                    {
                        // Clamp the reprojected diffuse estimate to the current
                        // geometry-aware neighborhood, then accumulate it at a
                        // confidence-weighted rate. Disoccluded pixels use only
                        // the current frame.
                        float3 radianceRange = neighborhoodMax.rgb - neighborhoodMin.rgb;
                        float3 radiancePadding = max(
                            radianceRange * 0.25,
                            max(abs(current.rgb) * 0.05, 0.002));
                        history.rgb = clamp(
                            history.rgb,
                            neighborhoodMin.rgb - radiancePadding,
                            neighborhoodMax.rgb + radiancePadding);
                        float visibilityRange = neighborhoodMax.a - neighborhoodMin.a;
                        float visibilityPadding = max(visibilityRange * 0.25, 0.01);
                        history.a = clamp(
                            history.a,
                            neighborhoodMin.a - visibilityPadding,
                            neighborhoodMax.a + visibilityPadding);
                        float blendRate = lerp(
                            1.0,
                            1.0 - constants.params.z,
                            historyConfidence);
                        result = lerp(history, current, blendRate);
                    }
                }
            }
        }
    }

    _indirectGI[pixel] = result;
    _historyOut[pixel] = result;
    if (!isSpecular)
    {
        _historyOut[uint2(tracePixel.x + halfWidth * 2, tracePixel.y)] =
            float4(currentLinearDepth, geometryNormal * 0.5 + 0.5);
    }
}
