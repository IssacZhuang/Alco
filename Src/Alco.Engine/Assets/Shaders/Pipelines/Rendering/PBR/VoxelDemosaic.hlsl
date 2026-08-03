#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// Configurable-resolution bilateral spatial filter and temporal accumulation
// for voxel GI. A geometry-aware diffuse footprint suppresses voxel sampling
// noise, then validated reprojection accumulates stable history without
// disocclusion trails. Specular uses a smaller footprint to preserve detail.
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

// Recover view-linear depth from the homogeneous w produced by the inverse
// view-projection. Only the fourth matrix row is needed, avoiding a full world
// reconstruction for every bilateral tap.
float ReconstructLinearDepth(float2 uv, float depth, float4x4 invVP)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float reciprocalClipW = dot(invVP[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

float NormalSimilarity(float3 centerNormal, float3 sampleNormal)
{
    // Normal-map detail may vary strongly inside one low-frequency irradiance
    // footprint. Accept up to roughly 60 degrees, but still give orthogonal
    // architectural surfaces zero weight.
    return smoothstep(0.05, 0.5, dot(centerNormal, sampleNormal));
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
    float3 detailNormal = normalize(packedNormal.xyz * 2.0 - 1.0);
    float3 N = isSpecular
        ? detailNormal
        : geometryNormal;
    float currentLinearDepth = abs(mul(viewProjection, float4(worldPos, 1.0)).w);
    float4 centerVal = _traceInput.Load(int3(pixel, 0));

    // Reject isolated HDR cone hits before they enter either the spatial or
    // temporal filter. The guide comes from the four nearest samples on the
    // same geometric surface, so real extended bounce lighting is retained
    // while a lone ray hitting a bright emissive voxel is luminance-clamped.
    float diffuseMaximumLuminance = 65504.0;
    float2 localLinearDepthGradient = 0.0;
    if (!isSpecular)
    {
        float3 guideSum = 0.0;
        float guideWeightSum = 0.0;
        float guideLinearDepths[4] = {
            currentLinearDepth, currentLinearDepth,
            currentLinearDepth, currentLinearDepth,
        };
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
            float guideWeight = 0.0;
            if (guideDepth < 0.9999)
            {
                float2 guideGbufferUV =
                    (float2(guideGbufferPixel) + 0.5) / float2(gbufferRes);
                float guideLinearDepth = ReconstructLinearDepth(
                    guideGbufferUV, guideDepth, invViewProjection);
                float4 guidePackedNormal = GET_PIXEL_TEX2D(
                    _normal, guideGbufferPixel);
                float3 guideDetailNormal = normalize(
                    guidePackedNormal.xyz * 2.0 - 1.0);
                float normalWeight = NormalSimilarity(
                    detailNormal, guideDetailNormal);
                float depthTolerance = max(
                    0.025, currentLinearDepth * 0.002);
                float depthWeight = exp(
                    -abs(guideLinearDepth - currentLinearDepth)
                    / depthTolerance);
                guideWeight = normalWeight * depthWeight;
                if (guideWeight > 0.1)
                {
                    guideLinearDepths[guideIndex] = guideLinearDepth;
                }
            }
            guideSum += max(_traceInput.Load(int3(guideTrace, 0)).rgb, 0.0) * guideWeight;
            guideWeightSum += guideWeight;
        }

        float3 guideRadiance = guideWeightSum > 0.05
            ? guideSum / guideWeightSum
            : max(centerVal.rgb, 0.0);
        float guideLuminance = dot(guideRadiance, float3(0.2126, 0.7152, 0.0722));
        diffuseMaximumLuminance = clamp(guideLuminance * 4.0 + 0.02, 0.04, 8.0);
        centerVal.rgb = ClampRadianceLuminance(centerVal.rgb, diffuseMaximumLuminance);

        // The local depth gradient predicts the depth of a slanted plane at
        // the sparse outer taps. Comparing against that plane, rather than
        // against the center depth, preserves grazing surfaces while still
        // rejecting parallel trim and foreground layers.
        localLinearDepthGradient = float2(
            (guideLinearDepths[1] - guideLinearDepths[0]) * 0.5,
            (guideLinearDepths[3] - guideLinearDepths[2]) * 0.5);
    }

    // --- Bilateral spatial filter on the trace input ---
    // Stable mesh normals and a scale-aware diffuse footprint remove residual
    // cone hits while preserving the original CE5 cone width and mip choice.
    // The symmetric 5-tap weights [0.5 1 1 1 0.5] integrate each phase of the
    // 4x4 rotation tile with exactly equal total weight; an ordinary Gaussian
    // exposes that tile as fine vertical bands.
    // Specular keeps the sharper 3x3 footprint.
    bool phaseBalancedDiffuse = !isSpecular;
    int filterRadius = 1;
    float spatialSigma = max(constants.params.y, 0.001);
    float4 spatialSum = phaseBalancedDiffuse ? 0.0 : centerVal;
    float spatialW = phaseBalancedDiffuse ? 0.0 : 1.0;
    float4 neighborhoodMin = centerVal;
    float4 neighborhoodMax = centerVal;

    [unroll]
    for (int dy = -2; dy <= 2; dy++)
    {
        [unroll]
        for (int dx = -2; dx <= 2; dx++)
        {
            bool includePhaseBalanced = abs(dx) <= 2 && abs(dy) <= 2;
            bool includeRegular = (dx != 0 || dy != 0)
                && abs(dx) <= filterRadius && abs(dy) <= filterRadius;
            if (phaseBalancedDiffuse ? !includePhaseBalanced : !includeRegular)
            {
                continue;
            }

            // The projected voxel footprint appears as broad bands on flat
            // walls. Spread the same 25 phase-balanced taps over a 13x13
            // trace-pixel footprint: an odd stride still visits every phase of
            // the fixed 4x4 cone tile exactly once, but integrates the
            // world-space voxel lattice without extra samples.
            int diffuseStride = 3;
            int2 filterOffset = int2(dx, dy)
                * (isSpecular ? 1 : diffuseStride);
            int2 np = int2(pixel) + filterOffset;
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
            float2 nTraceUV =
                (float2(nTrace) + 0.5) / float2(giParams.z, giParams.w);
            int2 nGbufPixel = int2(nTraceUV * float2(gbufferRes));
            nGbufPixel = clamp(nGbufPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float nDepth = GET_PIXEL_TEX2D(_gbufferDepth, nGbufPixel);

            float surfaceW = 0.0;
            float3 nDetailNormal = N;
            if (nDepth < 0.9999)
            {
                float2 nGbufferUV =
                    (float2(nGbufPixel) + 0.5) / float2(gbufferRes);
                float nLinearDepth = ReconstructLinearDepth(
                    nGbufferUV, nDepth, invViewProjection);
                float4 nPackedNormal = GET_PIXEL_TEX2D(_normal, nGbufPixel);
                nDetailNormal = normalize(nPackedNormal.xyz * 2.0 - 1.0);
                float expectedLinearDepth = currentLinearDepth
                    + dot(float2(filterOffset), localLinearDepthGradient);
                float depthTolerance = max(
                    0.025, currentLinearDepth * 0.0015)
                    * (1.0 + length(float2(filterOffset)) * 0.1);
                float depthWeight = exp(
                    -abs(nLinearDepth - expectedLinearDepth)
                    / depthTolerance);
                surfaceW = NormalSimilarity(detailNormal, nDetailNormal)
                    * depthWeight;
            }
            float spatialW_neighbour;
            if (phaseBalancedDiffuse)
            {
                float phaseWeightX = abs(dx) == 2 ? 0.5 : 1.0;
                float phaseWeightY = abs(dy) == 2 ? 0.5 : 1.0;
                spatialW_neighbour = phaseWeightX * phaseWeightY;
            }
            else
            {
                spatialW_neighbour = exp(
                    -(dx * dx + dy * dy) / (2.0 * spatialSigma * spatialSigma));
            }

            // Diffuse uses a deliberately lenient normal-map compatibility
            // through surfaceW; specular applies a much sharper rejection.
            float normalW = 1.0;
            if (isSpecular)
            {
                normalW = pow(max(dot(N, nDetailNormal), 0.0), 16.0);
            }

            float w = surfaceW * spatialW_neighbour * normalW;
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
