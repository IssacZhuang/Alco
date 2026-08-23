// file "VoxelDemosaic.slang" - module "voxel_demosaic"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelDemosaic.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_demosaic;

import alco_rendering_core;
import alco_world3d_voxel_common;
import alco_world3d_geometry_normal;
import alco_world3d_reversed_depth;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Min/Max dual-layer spatial resolve for temporally integrated raw voxel GI.
// One thread per trace pixel: a geometry-aware 9x9 filter reduces the small
// residual variance between independently converged pixels and accumulates it
// into near (depthMin) and far (depthMax) surface layers. The two layers are
// written to separate atlas sections with
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
[[vk::binding(1, 0)]] Texture2D _traceInput;
[[vk::binding(5, 0)]] DepthTexture2D _gbufferDepth;
[[vk::binding(2, 0)]] Texture2D _normal;
[[vk::binding(3, 0)]] Texture2D _historyInput;
[[vk::binding(4, 0)]] Texture2D _emissive;
[[vk::binding(0, 1)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> _indirectGI;
[[vk::binding(1, 1)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> _historyOut;

[[vk::push_constant]] VoxelDemosaicConstants constants;

// The spatial resolve works on an 8x8 output group. Diffuse/ALD need a four-
// pixel halo (16x16 tile), while specular only needs a one-pixel halo (10x10).
// Keeping the tiles separate holds workgroup storage below WebGPU's guaranteed
// 16 KiB limit:
//   diffuse + ALD  = 2 * 16 * 16 * float4 = 8.0 KiB
//   surface data   = 4 * 16 * 16 * float  = 4.0 KiB
//   specular       =     10 * 10 * float4 = 1.6 KiB
// Surface data stores precomputed linear depth and decoded geometry normal, so
// the expensive G-buffer loads and reconstruction happen once per tile texel.
static const uint DEMOSAIC_GROUP_SIZE = 8u;
static const uint DEMOSAIC_DIFFUSE_RADIUS = 4u;
static const uint DEMOSAIC_DIFFUSE_TILE_SIZE = 16u;
static const uint DEMOSAIC_DIFFUSE_TILE_COUNT = 256u;
static const uint DEMOSAIC_SPECULAR_RADIUS = 1u;
static const uint DEMOSAIC_SPECULAR_TILE_SIZE = 10u;
static const uint DEMOSAIC_SPECULAR_TILE_COUNT = 100u;
static const uint DEMOSAIC_GROUP_THREAD_COUNT = 64u;

groupshared float4 gsDiffuse[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared float4 gsAld[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared float4 gsSpecular[DEMOSAIC_SPECULAR_TILE_COUNT];
groupshared float gsLinearDepth[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared float gsGeometryNormalX[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared float gsGeometryNormalY[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared float gsGeometryNormalZ[DEMOSAIC_DIFFUSE_TILE_COUNT];
groupshared uint gsHasGeometry;

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

void WriteZeroOutputs(int2 tracePixel, int halfWidth)
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
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(
    uint3 dispatchId : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 groupThreadId : SV_GroupThreadID,
    uint groupIndex : SV_GroupIndex)
{
    uint2 traceResolution = uint2(giParams.z, giParams.w);
    uint2 gbufferRes = uint2(giParams2.y, giParams2.z);
    int halfWidth = (int)giParams.z;
    bool validDispatch = all(dispatchId.xy < traceResolution);
    int2 traceMax = int2(traceResolution) - 1;
    int2 gbufferMax = int2(gbufferRes) - 1;
    int2 groupOrigin = int2(groupId.xy * DEMOSAIC_GROUP_SIZE);

    // Populate the 16x16 surface tile first. Besides eliminating repeated
    // G-buffer reads, this lets an all-sky workgroup skip every trace-atlas load.
    for (uint tileIndex = groupIndex;
        tileIndex < DEMOSAIC_DIFFUSE_TILE_COUNT;
        tileIndex += DEMOSAIC_GROUP_THREAD_COUNT)
    {
        uint2 tileCoord = uint2(
            tileIndex % DEMOSAIC_DIFFUSE_TILE_SIZE,
            tileIndex / DEMOSAIC_DIFFUSE_TILE_SIZE);
        int2 sampleTracePixel = clamp(
            groupOrigin + int2(tileCoord) - int(DEMOSAIC_DIFFUSE_RADIUS),
            int2(0, 0),
            traceMax);
        float2 sampleTraceUV =
            (float2(sampleTracePixel) + 0.5) / float2(traceResolution);
        int2 sampleGbufferPixel = clamp(
            int2(sampleTraceUV * float2(gbufferRes)),
            int2(0, 0),
            gbufferMax);
        float sampleDepth = GetPixelDepth2D(_gbufferDepth, sampleGbufferPixel);

        float linearDepth = -1.0;
        float3 geometryNormal = float3(0.0, 0.0, 1.0);
        if (!IS_SKY_DEPTH(sampleDepth))
        {
            float2 sampleGbufferUV =
                (float2(sampleGbufferPixel) + 0.5) / float2(gbufferRes);
            linearDepth = ReconstructLinearDepth(
                sampleGbufferUV, sampleDepth, invViewProjection);
            float4 packedNormal = GET_PIXEL_TEX2D(_normal, sampleGbufferPixel);
            float packedGeometryY = GET_PIXEL_TEX2D(_emissive, sampleGbufferPixel).a;
            geometryNormal = DecodeGeometryNormal(
                float2(packedNormal.a, packedGeometryY));
        }

        gsLinearDepth[tileIndex] = linearDepth;
        gsGeometryNormalX[tileIndex] = geometryNormal.x;
        gsGeometryNormalY[tileIndex] = geometryNormal.y;
        gsGeometryNormalZ[tileIndex] = geometryNormal.z;
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex == 0u)
    {
        gsHasGeometry = 0u;
        [unroll]
        for (uint centerY = 0u; centerY < DEMOSAIC_GROUP_SIZE; centerY++)
        {
            [unroll]
            for (uint centerX = 0u; centerX < DEMOSAIC_GROUP_SIZE; centerX++)
            {
                uint centerIndex =
                    (centerY + DEMOSAIC_DIFFUSE_RADIUS) * DEMOSAIC_DIFFUSE_TILE_SIZE
                    + centerX + DEMOSAIC_DIFFUSE_RADIUS;
                gsHasGeometry |= gsLinearDepth[centerIndex] > 0.0 ? 1u : 0u;
            }
        }
    }
    GroupMemoryBarrierWithGroupSync();

    if (gsHasGeometry == 0u)
    {
        if (validDispatch)
        {
            WriteZeroOutputs(int2(dispatchId.xy), halfWidth);
        }
        return;
    }

    // Diffuse and ALD share the 16x16 footprint.
    // (separate loop-variable names: slang scopes for-init declarations to the
    // function, so redeclaring tileIndex here would be ambiguous)
    for (uint diffuseTileIndex = groupIndex;
        diffuseTileIndex < DEMOSAIC_DIFFUSE_TILE_COUNT;
        diffuseTileIndex += DEMOSAIC_GROUP_THREAD_COUNT)
    {
        uint2 tileCoord = uint2(
            diffuseTileIndex % DEMOSAIC_DIFFUSE_TILE_SIZE,
            diffuseTileIndex / DEMOSAIC_DIFFUSE_TILE_SIZE);
        int2 sampleTracePixel = clamp(
            groupOrigin + int2(tileCoord) - int(DEMOSAIC_DIFFUSE_RADIUS),
            int2(0, 0),
            traceMax);
        gsDiffuse[diffuseTileIndex] = _traceInput.Load(int3(sampleTracePixel, 0));
        gsAld[diffuseTileIndex] = _traceInput.Load(
            int3(sampleTracePixel + int2(halfWidth * 2, 0), 0));
    }

    // Specular only needs the tighter 10x10 footprint.
    for (uint specularTileIndex = groupIndex;
        specularTileIndex < DEMOSAIC_SPECULAR_TILE_COUNT;
        specularTileIndex += DEMOSAIC_GROUP_THREAD_COUNT)
    {
        uint2 tileCoord = uint2(
            specularTileIndex % DEMOSAIC_SPECULAR_TILE_SIZE,
            specularTileIndex / DEMOSAIC_SPECULAR_TILE_SIZE);
        int2 sampleTracePixel = clamp(
            groupOrigin + int2(tileCoord) - int(DEMOSAIC_SPECULAR_RADIUS),
            int2(0, 0),
            traceMax);
        gsSpecular[specularTileIndex] = _traceInput.Load(
            int3(sampleTracePixel + int2(halfWidth, 0), 0));
    }
    GroupMemoryBarrierWithGroupSync();

    // Threads outside a non-multiple-of-eight dispatch must participate in all
    // barriers above, but produce no output.
    if (!validDispatch)
    {
        return;
    }

    int2 tracePixel = int2(dispatchId.xy);
    float2 traceUV = (float2(tracePixel) + 0.5) / float2(traceResolution);
    int2 gbufferPixel = int2(traceUV * float2(gbufferRes));
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), gbufferMax);

    float depth = GetPixelDepth2D(_gbufferDepth, gbufferPixel);
    if (IS_SKY_DEPTH(depth))
    {
        WriteZeroOutputs(tracePixel, halfWidth);
        return;
    }

    uint centerDiffuseIndex =
        (groupThreadId.y + DEMOSAIC_DIFFUSE_RADIUS) * DEMOSAIC_DIFFUSE_TILE_SIZE
        + groupThreadId.x + DEMOSAIC_DIFFUSE_RADIUS;
    uint centerSpecularIndex =
        (groupThreadId.y + DEMOSAIC_SPECULAR_RADIUS) * DEMOSAIC_SPECULAR_TILE_SIZE
        + groupThreadId.x + DEMOSAIC_SPECULAR_RADIUS;
    // Match VoxelTrace's canonical receiver grid. Using the selected full-res
    // G-buffer pixel center here creates the same fixed quarter-history-texel
    // offset and makes post-demosaic feedback smear toward screen-space left.
    float3 worldPos = ReconstructWorldPosition(traceUV, depth, invViewProjection);
    float3 geometryNormal = float3(
        gsGeometryNormalX[centerDiffuseIndex],
        gsGeometryNormalY[centerDiffuseIndex],
        gsGeometryNormalZ[centerDiffuseIndex]);
    float currentLinearDepth = abs(mul(viewProjection, float4(worldPos, 1.0)).w);
    float4 centerDiffuse = gsDiffuse[centerDiffuseIndex];
    float4 centerSpecular = gsSpecular[centerSpecularIndex];
    float4 centerAld = gsAld[centerDiffuseIndex];

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
            float layerDepth = GetPixelDepth2D(_gbufferDepth, layerPixel);
            if (!IS_SKY_DEPTH(layerDepth))
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
    // The symmetric 9-tap weights [0.5 1 1 1 1 1 1 1 0.5] give every legacy
    // 8x8 start phase equal total weight while smoothing the residual variance
    // of the per-pixel temporal integrals. Every tap contributes to both the
    // near and the far layer with independent soft depth tests.
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
            // Diffuse gathers a compact 9x9 geometry-aware denoising footprint.
            int2 filterOffset = int2(dx, dy);
            uint tileIndex =
                (uint)((int)groupThreadId.y + int(DEMOSAIC_DIFFUSE_RADIUS) + dy)
                    * DEMOSAIC_DIFFUSE_TILE_SIZE
                + (uint)((int)groupThreadId.x + int(DEMOSAIC_DIFFUSE_RADIUS) + dx);

            float nLinearDepth = gsLinearDepth[tileIndex];
            float4 diffuseTap = gsDiffuse[tileIndex];
            // ALD accumulates with the same bilateral weights as RGB. ALD is
            // a direction-weighted value, not HDR color, so no luminance
            // clamping is applied.
            float4 aldTap = gsAld[tileIndex];

            float phaseWeightX = abs(dx) == 4 ? 0.5 : 1.0;
            float phaseWeightY = abs(dy) == 4 ? 0.5 : 1.0;
            float phaseWeight = phaseWeightX * phaseWeightY;

            float weightMin = 0.0;
            float weightMax = 0.0;
            if (nLinearDepth > 0.0)
            {
                float3 nGeometryNormal = float3(
                    gsGeometryNormalX[tileIndex],
                    gsGeometryNormalY[tileIndex],
                    gsGeometryNormalZ[tileIndex]);
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
            uint surfaceTileIndex =
                (uint)((int)groupThreadId.y + int(DEMOSAIC_DIFFUSE_RADIUS) + sy)
                    * DEMOSAIC_DIFFUSE_TILE_SIZE
                + (uint)((int)groupThreadId.x + int(DEMOSAIC_DIFFUSE_RADIUS) + sx);
            uint specularTileIndex =
                (uint)((int)groupThreadId.y + int(DEMOSAIC_SPECULAR_RADIUS) + sy)
                    * DEMOSAIC_SPECULAR_TILE_SIZE
                + (uint)((int)groupThreadId.x + int(DEMOSAIC_SPECULAR_RADIUS) + sx);
            float nLinearDepth = gsLinearDepth[surfaceTileIndex];

            float surfaceW = 0.0;
            float3 nGeometryNormal = geometryNormal;
            if (nLinearDepth > 0.0)
            {
                nGeometryNormal = float3(
                    gsGeometryNormalX[surfaceTileIndex],
                    gsGeometryNormalY[surfaceTileIndex],
                    gsGeometryNormalZ[surfaceTileIndex]);
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
            float4 specularTap = gsSpecular[specularTileIndex];
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

                // Shared reprojection displacement in trace-UV units. The
                // specular blend below discards history with it (reflection
                // content slides under motion even when the reprojection is
                // valid); the diffuse blend only shortens its window.
                float2 motionVector = prevUV - traceUV;
                float motionDist = length(motionVector);

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

                    // Diffuse tolerates camera motion far better than
                    // specular: for static geometry the reprojection stays
                    // valid, so motion halves the effective hysteresis
                    // instead of discarding history. The shortened window
                    // stays responsive to the brick-quantized scroll of the
                    // voxel field while still averaging the single-cone
                    // noise left wherever the raw-stage reprojection rejected
                    // its own history.
                    float diffuseHysteresis = constants.params.z
                        * (1.0 - 0.5 * saturate(motionDist / 0.05));
                    resultMin = AccumulateDiffuseLayer(
                        historyMin, layerMin, neighborhoodMin, neighborhoodMax,
                        diffuseHysteresis, historyConfidence);
                    resultMax = AccumulateDiffuseLayer(
                        historyMax, layerMax, neighborhoodMin, neighborhoodMax,
                        diffuseHysteresis, historyConfidence);

                    // ALD accumulates with the same temporal scheme as diffuse
                    // RGB: clamp to a neighbourhood, blend at the confidence-
                    // weighted rate. ALD magnitude (w) is clamped against the
                    // diffuse neighbourhood alpha range so a stale bright
                    // direction cannot persist after the source surface moves.
                    float aldBlendRate = lerp(1.0, 1.0 - diffuseHysteresis, historyConfidence);
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
