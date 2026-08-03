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

static const int2 SURFACE_GUIDE_OFFSETS[4] = {
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

float GeometryNormalSimilarity(float3 centerNormal, float3 sampleNormal)
{
    // Geometry normals are stable enough to distinguish architectural layers.
    // Keep this stricter than NormalSimilarity(), which intentionally tolerates
    // normal-map detail inside a diffuse irradiance footprint.
    return smoothstep(0.65, 0.9, dot(centerNormal, sampleNormal));
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

    // Developer view: expose the cone-trace atlas before spatial/temporal
    // reconstruction. This makes it possible to distinguish a tracing issue
    // from a resolve issue at an exactly reproduced camera position.
    if (giParams2.x > 3.5 && giParams2.x < 4.5)
    {
        _indirectGI[pixel] = centerVal;
        _historyOut[pixel] = centerVal;
        if (!isSpecular)
        {
            _historyOut[uint2(tracePixel.x + halfWidth * 2, tracePixel.y)] =
                float4(currentLinearDepth, geometryNormal * 0.5 + 0.5);
        }
        return;
    }

    // Match CE5's bounded HDR resolve without using neighbouring screen phases
    // as a firefly oracle. A valid small light source may occur in only one
    // member of the tiled angular kernel; clamping it to the four immediate
    // neighbours deletes real bounce light before the phase resolve. The fixed
    // ceiling only catches non-physical emissive outliers.
    float diffuseMaximumLuminance = 8.0;
    float2 localLinearDepthGradient = 0.0;
    if (!isSpecular)
    {
        float guideLinearDepths[4] = {
            currentLinearDepth, currentLinearDepth,
            currentLinearDepth, currentLinearDepth,
        };
        float guideFitWeights[4] = { 0.0, 0.0, 0.0, 0.0 };

        // CE5 deliberately uses a broad 12--20 percent depth range while
        // gathering demosaic candidates. This range is only used to fit the
        // local receiving surface; actual radiance filtering below still uses
        // a tight residual around that fitted plane. Separating the two tests
        // is essential for facades viewed at a grazing angle.
        float3 viewDirection = normalize(cameraPosition.xyz - worldPos);
        float viewFacing = abs(dot(viewDirection, geometryNormal));
        float fitRelativeDepthRange = lerp(0.20, 0.12, viewFacing);
        [unroll]
        for (uint guideIndex = 0u; guideIndex < 4u; guideIndex++)
        {
            int2 guideTrace = clamp(
                tracePixel + SURFACE_GUIDE_OFFSETS[guideIndex],
                int2(0, 0),
                int2(halfWidth - 1, (int)giParams.w - 1));
            float2 guideUV = (float2(guideTrace) + 0.5) / float2(giParams.z, giParams.w);
            int2 guideGbufferPixel = int2(guideUV * float2(gbufferRes));
            guideGbufferPixel = clamp(
                guideGbufferPixel,
                int2(0, 0),
                int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float guideDepth = GET_PIXEL_TEX2D(_gbufferDepth, guideGbufferPixel);
            if (guideDepth < 0.9999)
            {
                float2 guideGbufferUV =
                    (float2(guideGbufferPixel) + 0.5) / float2(gbufferRes);
                float guideLinearDepth = ReconstructLinearDepth(
                    guideGbufferUV, guideDepth, invViewProjection);
                float4 guidePackedNormal = GET_PIXEL_TEX2D(
                    _normal, guideGbufferPixel);
                float guidePackedGeometryY = GET_PIXEL_TEX2D(
                    _emissive, guideGbufferPixel).a;
                float3 guideGeometryNormal = DecodeGeometryNormal(
                    float2(guidePackedNormal.a, guidePackedGeometryY));
                float normalWeight = GeometryNormalSimilarity(
                    geometryNormal, guideGeometryNormal);
                float relativeDepthDifference = abs(
                    1.0 - guideLinearDepth / max(currentLinearDepth, 0.0001));
                float depthFitWeight = 1.0 - smoothstep(
                    fitRelativeDepthRange * 0.75,
                    fitRelativeDepthRange,
                    relativeDepthDifference);
                guideLinearDepths[guideIndex] = guideLinearDepth;
                guideFitWeights[guideIndex] = normalWeight * depthFitWeight;
            }
        }

        // Fit each screen-space depth slope only when both sides belong to a
        // continuous local plane. Opposing depth jumps (for example a narrow
        // cornice in front of a wall) fail the slope-agreement test and retain
        // a zero slope, so the later tight residual cannot bridge the layer.
        float slopeAgreementTolerance = max(
            0.04, currentLinearDepth * 0.002);
        if (min(guideFitWeights[0], guideFitWeights[1]) > 0.1)
        {
            float negativeSlope = currentLinearDepth - guideLinearDepths[0];
            float positiveSlope = guideLinearDepths[1] - currentLinearDepth;
            if (abs(negativeSlope - positiveSlope) <= slopeAgreementTolerance)
            {
                localLinearDepthGradient.x =
                    (negativeSlope + positiveSlope) * 0.5;
            }
        }
        if (min(guideFitWeights[2], guideFitWeights[3]) > 0.1)
        {
            float negativeSlope = currentLinearDepth - guideLinearDepths[2];
            float positiveSlope = guideLinearDepths[3] - currentLinearDepth;
            if (abs(negativeSlope - positiveSlope) <= slopeAgreementTolerance)
            {
                localLinearDepthGradient.y =
                    (negativeSlope + positiveSlope) * 0.5;
            }
        }

        centerVal.rgb = ClampRadianceLuminance(
            centerVal.rgb, diffuseMaximumLuminance);
    }

    // --- Bilateral spatial filter on the trace input ---
    // Stable mesh normals and a scale-aware diffuse footprint remove residual
    // cone hits while preserving the original CE5 cone width and mip choice.
    // The symmetric 9-tap weights [0.5 1 1 1 1 1 1 1 0.5] integrate each
    // phase of the 8x8 direction tile with exactly equal total weight; an
    // ordinary Gaussian exposes the tile as fine bands.
    // Specular keeps the sharper 3x3 footprint.
    bool phaseBalancedDiffuse = !isSpecular;
    int filterRadius = 1;
    float spatialSigma = max(constants.params.y, 0.001);
    float4 spatialSum = phaseBalancedDiffuse ? 0.0 : centerVal;
    float spatialW = phaseBalancedDiffuse ? 0.0 : 1.0;
    float4 neighborhoodMin = centerVal;
    float4 neighborhoodMax = centerVal;

    [unroll]
    for (int dy = -4; dy <= 4; dy++)
    {
        [unroll]
        for (int dx = -4; dx <= 4; dx++)
        {
            bool includePhaseBalanced = abs(dx) <= 4 && abs(dy) <= 4;
            bool includeRegular = (dx != 0 || dy != 0)
                && abs(dx) <= filterRadius && abs(dy) <= filterRadius;
            if (phaseBalancedDiffuse ? !includePhaseBalanced : !includeRegular)
            {
                continue;
            }

            // Diffuse gathers one complete, contiguous CE-style direction tile.
            // Its 9x9 footprint is tighter than the previous sparse 13x13
            // footprint even though it reconstructs many more directions.
            int2 filterOffset = int2(dx, dy);
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
                    0.035, currentLinearDepth * 0.002)
                    * (1.0 + length(float2(filterOffset)) * 0.08);
                float depthWeight = exp(
                    -abs(nLinearDepth - expectedLinearDepth)
                    / depthTolerance);
                surfaceW = NormalSimilarity(detailNormal, nDetailNormal)
                    * depthWeight;
            }
            float spatialW_neighbour;
            if (phaseBalancedDiffuse)
            {
                float phaseWeightX = abs(dx) == 4 ? 0.5 : 1.0;
                float phaseWeightY = abs(dy) == 4 ? 0.5 : 1.0;
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
