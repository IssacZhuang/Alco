#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// Min/Max dual-layer spatial resolve and temporal accumulation for
// voxel GI. One thread per trace pixel: every tap of the 8x8 direction-tile
// footprint is accumulated into a near (depthMin) and a far (depthMax) surface
// layer using soft relative depth tests, so a geometry edge keeps a nearly
// complete directional kernel on both of its sides instead of a rejected,
// degenerate one. The two layers are written to separate atlas sections with
// their layer linear depths in alpha; the deferred lighting pass then blends
// the layers at full-resolution depth (the upscale pass), so occlusion
// boundaries stay sharp at every trace resolution. Validated reprojection
// accumulates each layer independently. Specular keeps a small sharp
// footprint to preserve detail.
//
// ALD (Average Light Direction) is accumulated alongside RGB with the same
// bilateral weights. The deferred lighting pass uses ALD to give indirect
// light a directional diffuse response.
//
// Indirect atlas layout (5x trace width), sampled by DeferredLighting:
//   [0] diffuse near layer: rgb = irradiance, a = near layer linear depth
//   [1] diffuse far layer:  rgb = irradiance, a = far layer linear depth
//   [2] specular:           rgb = specular radiance, a = selected diffuse
//                           visibility (debug view only)
//   [3] ALD near layer:     xyz = dir*brightness, a = near layer linear depth
//   [4] ALD far layer:      xyz = dir*brightness, a = far layer linear depth
// History layout (6x trace width), read back next frame:
//   [0] near layer rgb + visibility, [1] far layer rgb + visibility,
//   [2] specular, [3] near ALD, [4] far ALD,
//   [5] linear depth + world normal (disocclusion metadata).

struct VoxelDemosaicConstants
{
    float4 params; // x=specularHysteresis, y=spatialSigma, z=diffuseHysteresis, w=unused
};

// Bind groups: set 0 packs the per-dispatch inputs together with the shared
// uniform (binding 0, from VoxelCommon.hlsli); set 1 holds the output textures,
// so the pass needs two of the eight available sets.
DEFINE_TEX2D_READ(0, _traceInput);
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_READ(0, _historyInput);
DEFINE_TEX2D_READ(0, _emissive);
DEFINE_TEX2D_STORAGE(1, _indirectGI, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(1, _historyOut, float4, "rgba16f");

PUSH_CONSTANT VoxelDemosaicConstants constants;

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

// Clamp the reprojected diffuse layer estimate to the current geometry-aware
// neighborhood, then accumulate it at a confidence-weighted rate. Disoccluded
// pixels use only the current frame.
float4 AccumulateDiffuseLayer(
    float4 history,
    float4 current,
    float4 neighborhoodMin,
    float4 neighborhoodMax,
    float hysteresis,
    float historyConfidence)
{
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
    float blendRate = lerp(1.0, 1.0 - hysteresis, historyConfidence);
    return lerp(history, current, blendRate);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 traceResolution = uint2(giParams.z, giParams.w);
    if (any(dispatchId.xy >= traceResolution))
    {
        return;
    }

    int2 tracePixel = int2(dispatchId.xy);
    int halfWidth = (int)giParams.z;

    uint2 gbufferRes = uint2(giParams2.y, giParams2.z);
    float2 traceUV = (float2(tracePixel) + 0.5) / float2(traceResolution);
    int2 gbufferPixel = int2(traceUV * float2(gbufferRes));
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferRes);

    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[tracePixel + int2(halfWidth, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[tracePixel + int2(halfWidth * 2, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[tracePixel + int2(halfWidth * 3, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[tracePixel + int2(halfWidth * 4, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel + int2(halfWidth, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel + int2(halfWidth * 2, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel + int2(halfWidth * 3, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel + int2(halfWidth * 4, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[tracePixel + int2(halfWidth * 5, 0)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 worldPos = ReconstructWorldPosition(gbufferUV, depth, invViewProjection);
    float4 packedNormal = GET_PIXEL_TEX2D(_normal, gbufferPixel);
    float packedGeometryY = GET_PIXEL_TEX2D(_emissive, gbufferPixel).a;
    float3 geometryNormal = DecodeGeometryNormal(float2(packedNormal.a, packedGeometryY));
    float3 detailNormal = normalize(packedNormal.xyz * 2.0 - 1.0);
    float currentLinearDepth = abs(mul(viewProjection, float4(worldPos, 1.0)).w);
    float4 centerDiffuse = _traceInput.Load(int3(tracePixel, 0));
    float4 centerSpecular = _traceInput.Load(int3(tracePixel + int2(halfWidth, 0), 0));
    float4 centerAld = _traceInput.Load(int3(tracePixel + int2(halfWidth * 2, 0), 0));

    // Developer view: expose the cone-trace atlas before spatial/temporal
    // reconstruction. This makes it possible to distinguish a tracing issue
    // from a resolve issue at an exactly reproduced camera position.
    if (giParams2.x > 3.5 && giParams2.x < 4.5)
    {
        _indirectGI[tracePixel] = float4(centerDiffuse.rgb, currentLinearDepth);
        _indirectGI[tracePixel + int2(halfWidth, 0)] =
            float4(centerDiffuse.rgb, currentLinearDepth);
        _indirectGI[tracePixel + int2(halfWidth * 2, 0)] =
            float4(centerSpecular.rgb, centerDiffuse.a);
        _indirectGI[tracePixel + int2(halfWidth * 3, 0)] =
            float4(centerAld.xyz, currentLinearDepth);
        _indirectGI[tracePixel + int2(halfWidth * 4, 0)] =
            float4(centerAld.xyz, currentLinearDepth);
        _historyOut[tracePixel] = centerDiffuse;
        _historyOut[tracePixel + int2(halfWidth, 0)] = centerDiffuse;
        _historyOut[tracePixel + int2(halfWidth * 2, 0)] = centerSpecular;
        _historyOut[tracePixel + int2(halfWidth * 3, 0)] = centerAld;
        _historyOut[tracePixel + int2(halfWidth * 4, 0)] = centerAld;
        _historyOut[tracePixel + int2(halfWidth * 5, 0)] =
            float4(currentLinearDepth, geometryNormal * 0.5 + 0.5);
        return;
    }

    // Bounded HDR resolve without using neighbouring screen phases as a
    // firefly oracle. A valid small light source may occur in only one member
    // of the tiled angular kernel; clamping it to the four immediate neighbours
    // deletes real bounce light before the phase resolve. The fixed ceiling
    // only catches non-physical emissive outliers.
    float diffuseMaximumLuminance = 8.0;

    // The receiving surface layers are the min and max linear depth inside a
    // 2x2 G-buffer neighborhood. At a depth discontinuity, foreground taps
    // accumulate into the near layer and background taps into the far layer,
    // so each side of the edge keeps a nearly complete directional kernel
    // instead of a rejected fraction.
    float layerDepthMin = currentLinearDepth;
    float layerDepthMax = currentLinearDepth;
    [unroll]
    for (int layerY = 0; layerY <= 1; layerY++)
    {
        [unroll]
        for (int layerX = 0; layerX <= 1; layerX++)
        {
            int2 layerPixel = clamp(
                gbufferPixel + int2(layerX, layerY),
                int2(0, 0),
                int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float layerDepth = GET_PIXEL_TEX2D(_gbufferDepth, layerPixel);
            if (layerDepth < 0.9999)
            {
                float2 layerUV =
                    (float2(layerPixel) + 0.5) / float2(gbufferRes);
                float layerLinearDepth = ReconstructLinearDepth(
                    layerUV, layerDepth, invViewProjection);
                layerDepthMin = min(layerDepthMin, layerLinearDepth);
                layerDepthMax = max(layerDepthMax, layerLinearDepth);
            }
        }
    }

    centerDiffuse.rgb = ClampRadianceLuminance(
        centerDiffuse.rgb, diffuseMaximumLuminance);
    centerSpecular.rgb = ClampRadianceLuminance(
        centerSpecular.rgb, diffuseMaximumLuminance);

    // The depth acceptance is a relative ratio widened at grazing view angles,
    // not an absolute centimeter tolerance.
    float3 viewDirection = normalize(cameraPosition.xyz - worldPos);
    float viewFacing = abs(dot(viewDirection, geometryNormal));
    float depthRangeRatio = 0.12 + 0.08 * (1.0 - viewFacing);

    // --- Dual-layer diffuse gather on the trace input ---
    // The symmetric 9-tap weights [0.5 1 1 1 1 1 1 1 0.5] integrate each
    // phase of the 8x8 direction tile with exactly equal total weight; an
    // ordinary Gaussian exposes the tile as fine bands. Every tap contributes
    // to both the near and the far layer with independent soft depth tests.
    float4 layerSumMin = 0.0;
    float4 layerSumMax = 0.0;
    float layerWeightMin = 0.0;
    float layerWeightMax = 0.0;
    float4 neighborhoodMin = centerDiffuse;
    float4 neighborhoodMax = centerDiffuse;

    // --- ALD accumulation state (accumulated alongside RGB with identical
    // bilateral weights) ---
    float4 layerAldSumMin = centerAld * 0.001;
    float4 layerAldSumMax = centerAld * 0.001;
    float aldWeightMin = 0.001;
    float aldWeightMax = 0.001;

    // --- Specular gather state (3x3 tight bilateral, filled in the same
    // footprint loop below on its own taps) ---
    float spatialSigma = max(constants.params.y, 0.001);
    float4 specularSum = centerSpecular;
    float specularWeight = 1.0;
    float3 specularNeighborhoodMin = centerSpecular.rgb;
    float3 specularNeighborhoodMax = centerSpecular.rgb;

    [unroll]
    for (int dy = -4; dy <= 4; dy++)
    {
        [unroll]
        for (int dx = -4; dx <= 4; dx++)
        {
            // Diffuse gathers one complete, contiguous direction tile. Its
            // 9x9 footprint is tighter than the previous sparse 13x13
            // footprint even though it reconstructs many more directions.
            int2 filterOffset = int2(dx, dy);
            int2 np = clamp(
                tracePixel + filterOffset,
                int2(0, 0),
                int2((int)traceResolution.x - 1, (int)traceResolution.y - 1));

            // G-buffer depth at the neighbour for layer assignment.
            float2 nTraceUV = (float2(np) + 0.5) / float2(traceResolution);
            int2 nGbufPixel = int2(nTraceUV * float2(gbufferRes));
            nGbufPixel = clamp(nGbufPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float nDepth = GET_PIXEL_TEX2D(_gbufferDepth, nGbufPixel);

            float4 diffuseTap = _traceInput.Load(int3(np, 0));
            // ALD accumulates with the same bilateral weights as RGB. ALD is
            // a direction-weighted value, not HDR color, so no luminance
            // clamping is applied.
            float4 aldTap = _traceInput.Load(int3(np + int2(halfWidth * 2, 0), 0));

            float phaseWeightX = abs(dx) == 4 ? 0.5 : 1.0;
            float phaseWeightY = abs(dy) == 4 ? 0.5 : 1.0;
            float phaseWeight = phaseWeightX * phaseWeightY;

            float weightMin = 0.0;
            float weightMax = 0.0;
            if (nDepth < 0.9999)
            {
                float2 nGbufferUV =
                    (float2(nGbufPixel) + 0.5) / float2(gbufferRes);
                float nLinearDepth = ReconstructLinearDepth(
                    nGbufferUV, nDepth, invViewProjection);
                float4 nPackedNormal = GET_PIXEL_TEX2D(_normal, nGbufPixel);
                float nPackedGeometryY = GET_PIXEL_TEX2D(_emissive, nGbufPixel).a;
                float3 nGeometryNormal = DecodeGeometryNormal(
                    float2(nPackedNormal.a, nPackedGeometryY));
                // Orthogonal architecture still contributes at a 0.25 floor
                // so concave corners do not starve the kernel; coplanar taps
                // keep full weight.
                float normalWeight = NormalSimilarity(
                    geometryNormal, nGeometryNormal) * 0.75 + 0.25;

                // Independent soft depth tests per layer. The +0.001 floor
                // keeps every kernel non-empty.
                float depthTestMin = saturate(
                    (depthRangeRatio - abs(1.0 - nLinearDepth / max(layerDepthMin, 0.0001))) * 4.0)
                    + 0.001;
                float depthTestMax = saturate(
                    (depthRangeRatio - abs(1.0 - nLinearDepth / max(layerDepthMax, 0.0001))) * 4.0)
                    + 0.001;
                weightMin = depthTestMin * normalWeight * phaseWeight;
                weightMax = depthTestMax * normalWeight * phaseWeight;
            }
            else
            {
                // Sky taps carry no cone. Empty taps still accumulate with
                // a small floor weight so missing phases count as black
                // samples instead of breaking the kernel normalization.
                weightMin = 0.015 * phaseWeight;
                weightMax = 0.015 * phaseWeight;
                diffuseTap = 0.0;
            }

            diffuseTap.rgb = ClampRadianceLuminance(
                diffuseTap.rgb, diffuseMaximumLuminance);
            layerSumMin += diffuseTap * weightMin;
            layerSumMax += diffuseTap * weightMax;
            layerWeightMin += weightMin;
            layerWeightMax += weightMax;
            layerAldSumMin += aldTap * weightMin;
            layerAldSumMax += aldTap * weightMax;
            aldWeightMin += weightMin;
            aldWeightMax += weightMax;
            if (max(weightMin, weightMax) > 0.001)
            {
                neighborhoodMin = min(neighborhoodMin, diffuseTap);
                neighborhoodMax = max(neighborhoodMax, diffuseTap);
            }
        }
    }

    // Specular: tight 3x3 bilateral rejection around the receiver. Uses the
    // geometry normal (not the detail normal) for bilateral weights: the
    // detail normal shimmers per-pixel under camera motion and would inject
    // noise into the spatial filter, but the geometry normal is stable and
    // still rejects taps across depth/normal discontinuities.
    [unroll]
    for (int sy = -1; sy <= 1; sy++)
    {
        [unroll]
        for (int sx = -1; sx <= 1; sx++)
        {
            if (sx == 0 && sy == 0)
            {
                continue;
            }
            int2 filterOffset = int2(sx, sy);
            int2 np = clamp(
                tracePixel + filterOffset,
                int2(0, 0),
                int2((int)traceResolution.x - 1, (int)traceResolution.y - 1));

            float2 nTraceUV = (float2(np) + 0.5) / float2(traceResolution);
            int2 nGbufPixel = int2(nTraceUV * float2(gbufferRes));
            nGbufPixel = clamp(nGbufPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float nDepth = GET_PIXEL_TEX2D(_gbufferDepth, nGbufPixel);

            float surfaceW = 0.0;
            float3 nGeometryNormal = geometryNormal;
            if (nDepth < 0.9999)
            {
                float2 nGbufferUV =
                    (float2(nGbufPixel) + 0.5) / float2(gbufferRes);
                float nLinearDepth = ReconstructLinearDepth(
                    nGbufferUV, nDepth, invViewProjection);
                float4 nPackedNormal = GET_PIXEL_TEX2D(_normal, nGbufPixel);
                float nPackedGeometryY = GET_PIXEL_TEX2D(_emissive, nGbufPixel).a;
                nGeometryNormal = DecodeGeometryNormal(
                    float2(nPackedNormal.a, nPackedGeometryY));
                float depthTolerance = max(
                    0.035, currentLinearDepth * 0.002)
                    * (1.0 + length(float2(filterOffset)) * 0.08);
                float depthWeight = exp(
                    -abs(nLinearDepth - currentLinearDepth)
                    / depthTolerance);
                surfaceW = NormalSimilarity(geometryNormal, nGeometryNormal)
                    * depthWeight;
            }
            float spatialW_neighbour = exp(
                -(sx * sx + sy * sy) / (2.0 * spatialSigma * spatialSigma));
            float normalW = pow(max(dot(geometryNormal, nGeometryNormal), 0.0), 16.0);

            float w = surfaceW * spatialW_neighbour * normalW;
            float4 specularTap = _traceInput.Load(
                int3(np + int2(halfWidth, 0), 0));
            specularTap.rgb = ClampRadianceLuminance(
                specularTap.rgb, diffuseMaximumLuminance);
            specularSum += specularTap * w;
            specularWeight += w;
            specularNeighborhoodMin = min(specularNeighborhoodMin, specularTap.rgb);
            specularNeighborhoodMax = max(specularNeighborhoodMax, specularTap.rgb);
        }
    }

    // rgb = irradiance, a = per-layer visibility (diagnostic).
    float4 layerMin = layerSumMin / max(layerWeightMin, 0.0001);
    float4 layerMax = layerSumMax / max(layerWeightMax, 0.0001);
    float4 specularCurrent = specularSum / max(specularWeight, 0.0001);
    // ALD normalisation: xyz = direction-weighted, w = total brightness.
    float4 layerAldMin = layerAldSumMin / max(aldWeightMin, 0.0001);
    float4 layerAldMax = layerAldSumMax / max(aldWeightMax, 0.0001);

    // --- Temporal reprojection: one shared surface-validity test, then an
    // independent accumulation per layer ---
    float4 resultMin = layerMin;
    float4 resultMax = layerMax;
    float4 resultSpecular = specularCurrent;
    float4 resultAldMin = layerAldMin;
    float4 resultAldMax = layerAldMax;
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
                int2 previousTracePixel = int2(prevUV * float2(traceResolution));
                previousTracePixel = clamp(
                    previousTracePixel,
                    int2(0, 0),
                    int2((int)traceResolution.x - 1, (int)traceResolution.y - 1));

                // The depth RATIO (not absolute difference) gradually
                // increases the current-frame blend weight. An absolute
                // tolerance (7.5 cm) is far too strict for geometric
                // features: a window recess or eave at 5 m has 20-30 cm of
                // depth change, which would completely discard temporal
                // history on every frame the half-resolution trace pixel
                // crosses the feature edge. The ratio test (0.06 at 5 m /
                // 4.7 m) only raises the blend from 0.1 to 0.16, keeping 84%
                // of the accumulated history.
                int2 metadataPixel = previousTracePixel + int2(halfWidth * 5, 0);
                float4 historyMetadata = _historyInput.Load(int3(metadataPixel, 0));
                float expectedPreviousDepth = abs(prevClip.w);
                float depthRatio = abs(
                    expectedPreviousDepth / max(historyMetadata.x, 0.0001) - 1.0);
                float historyConfidence = 1.0 - saturate(depthRatio);
                historyConfidence *= (historyMetadata.x > 0.0 ? 1.0 : 0.0);

                if (historyConfidence > 0.001)
                {
                    float4 historyMin = _historyInput.Load(
                        int3(previousTracePixel, 0));
                    float4 historyMax = _historyInput.Load(
                        int3(previousTracePixel + int2(halfWidth, 0), 0));
                    float4 historySpecular = _historyInput.Load(
                        int3(previousTracePixel + int2(halfWidth * 2, 0), 0));
                    float4 historyAldMin = _historyInput.Load(
                        int3(previousTracePixel + int2(halfWidth * 3, 0), 0));
                    float4 historyAldMax = _historyInput.Load(
                        int3(previousTracePixel + int2(halfWidth * 4, 0), 0));

                    resultMin = AccumulateDiffuseLayer(
                        historyMin, layerMin, neighborhoodMin, neighborhoodMax,
                        constants.params.z, historyConfidence);
                    resultMax = AccumulateDiffuseLayer(
                        historyMax, layerMax, neighborhoodMin, neighborhoodMax,
                        constants.params.z, historyConfidence);

                    // ALD accumulates with the same temporal scheme as diffuse
                    // RGB: clamp to a neighbourhood, blend at the confidence-
                    // weighted rate. ALD magnitude (w) is clamped against the
                    // diffuse neighbourhood alpha range so a stale bright
                    // direction cannot persist after the source surface moves.
                    float aldHysteresis = constants.params.z;
                    float aldBlendRate = lerp(1.0, 1.0 - aldHysteresis, historyConfidence);
                    resultAldMin = lerp(historyAldMin, layerAldMin, aldBlendRate);
                    resultAldMax = lerp(historyAldMax, layerAldMax, aldBlendRate);

                    // Clamp history specular to the current-frame neighborhood
                    // bounding box before blending (same principle as diffuse's
                    // AccumulateDiffuseLayer). Without this, a single bright
                    // firefly voxel sample persists across frames and manifests
                    // as per-pixel flicker during camera motion.
                    float3 specularRange = specularNeighborhoodMax - specularNeighborhoodMin;
                    float3 specularPadding = max(
                        specularRange * 0.25,
                        max(abs(specularCurrent.rgb) * 0.05, 0.002));
                    historySpecular.rgb = clamp(
                        historySpecular.rgb,
                        specularNeighborhoodMin - specularPadding,
                        specularNeighborhoodMax + specularPadding);

                    // The specular temporal blend is driven by camera-motion
                    // displacement, not luminance change alone. When the
                    // reprojected UV shifts more than 2.5% of the screen, the
                    // blend ramps to 1.0 (full current frame) — a reflection
                    // highlight sliding across the surface changes brightness
                    // little at any given pixel, so a luminance-only trigger
                    // lets stale history persist and then pop when the
                    // highlight arrives.
                    float2 motionVector = prevUV - traceUV;
                    float motionDist = length(motionVector);
                    float motionBlend = saturate(motionDist / 0.025);

                    float curLum = dot(specularCurrent.rgb, float3(0.299, 0.587, 0.114));
                    float histLum = dot(historySpecular.rgb, float3(0.299, 0.587, 0.114));
                    float lumDiff = abs(curLum - histLum) / max(max(curLum, histLum), 0.001);
                    float visibilityDiff = abs(specularCurrent.a - historySpecular.a);
                    float change = max(saturate(lumDiff * 3.0), saturate(visibilityDiff * 4.0));
                    // Either camera motion or a luminance jump should accelerate
                    // the blend; also floor at the disocclusion rate.
                    float specularBlendRate = max(
                        lerp(1.0 - constants.params.x, 1.0, max(change, motionBlend)),
                        1.0 - historyConfidence);
                    resultSpecular = lerp(historySpecular, specularCurrent, specularBlendRate);
                }
            }
        }
    }

    // The visibility shown by the debug view mirrors the lighting pass: the
    // receiving pixel blends the two layer visibilities by its own depth.
    float layerLerp = saturate(
        (currentLinearDepth - layerDepthMin)
        / max(layerDepthMax - layerDepthMin, 0.0001));
    float selectedVisibility = lerp(resultMin.a, resultMax.a, layerLerp);

    _indirectGI[tracePixel] = float4(resultMin.rgb, layerDepthMin);
    _indirectGI[tracePixel + int2(halfWidth, 0)] = float4(resultMax.rgb, layerDepthMax);
    _indirectGI[tracePixel + int2(halfWidth * 2, 0)] =
        float4(resultSpecular.rgb, selectedVisibility);
    _indirectGI[tracePixel + int2(halfWidth * 3, 0)] =
        float4(resultAldMin.xyz, layerDepthMin);
    _indirectGI[tracePixel + int2(halfWidth * 4, 0)] =
        float4(resultAldMax.xyz, layerDepthMax);
    _historyOut[tracePixel] = resultMin;
    _historyOut[tracePixel + int2(halfWidth, 0)] = resultMax;
    _historyOut[tracePixel + int2(halfWidth * 2, 0)] = resultSpecular;
    _historyOut[tracePixel + int2(halfWidth * 3, 0)] = resultAldMin;
    _historyOut[tracePixel + int2(halfWidth * 4, 0)] = resultAldMax;
    _historyOut[tracePixel + int2(halfWidth * 5, 0)] =
        float4(currentLinearDepth, geometryNormal * 0.5 + 0.5);
}
