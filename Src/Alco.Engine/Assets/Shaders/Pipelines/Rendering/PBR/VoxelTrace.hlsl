#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the half-res
// trace resolution. Reconstructs the world position and normal from the
// G-buffer, traces 9 diffuse cones covering the hemisphere and one specular
// cone along the reflection vector through the radiance volume. The result is
// written into the output atlas (twice the trace width): diffuse bounce
// radiance plus environment visibility in the left half, specular radiance in
// the right. Only specular cones fall back to the sky gradient; diffuse sky is
// evaluated independently by the lighting pass and modulated by visibility.

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_READ(4, _mrAO);
DEFINE_TEX2D_STORAGE(5, _indirectGI, float4, "rgba16f");
DEFINE_TEX3D_SAMPLE(6, _opacity);
DEFINE_TEX2D_READ(7, _albedo);

// --- Diffuse cone set -------------------------------------------------------
// 9 directions distributed across the hemisphere (z = surface normal).
//   1 cone at θ=0° (straight up), 4 at θ=45°, 4 at θ=75°.
//   With ~30° half-angle (tan ≈ 0.577) the 9 cones tile the hemisphere
//   with overlap, matching the classic SVOGI diffuse approximation.
// Based on Crassin et al., "Interactive Indirect Illumination Using Voxel
// Cone Tracing" (GPU Pro / GPU Gems).
static const uint DIFFUSE_CONE_COUNT = 9u;
static const float DIFFUSE_CONE_APERTURE = 0.57735; // tan(30°)

static const float3 DIFFUSE_CONE_DIRECTIONS[9] = {
    float3( 0.00000,  0.00000,  1.00000), // θ=0°
    float3( 0.70711,  0.00000,  0.70711), // θ=45°, φ=0°
    float3( 0.00000,  0.70711,  0.70711), // θ=45°, φ=90°
    float3(-0.70711,  0.00000,  0.70711), // θ=45°, φ=180°
    float3( 0.00000, -0.70711,  0.70711), // θ=45°, φ=270°
    float3( 0.68301,  0.68301,  0.25882), // θ=75°, φ=45°
    float3(-0.68301,  0.68301,  0.25882), // θ=75°, φ=135°
    float3(-0.68301, -0.68301,  0.25882), // θ=75°, φ=225°
    float3( 0.68301, -0.68301,  0.25882), // θ=75°, φ=315°
};

// Cosine-weight for each cone (N·dir in tangent space = dir.z).
// Concentrates energy near the normal where it matters most.
static const float DIFFUSE_CONE_WEIGHTS[9] = {
    1.00000,
    0.70711, 0.70711, 0.70711, 0.70711,
    0.25882, 0.25882, 0.25882, 0.25882,
};

// Build a world-space tangent basis from a single surface normal.
float3x3 GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return float3x3(tangent, bitangent, normal);
}

// --- Utility helpers (ported from the DDGI-era shader) ----------------------

float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow((color + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

float3 ApproximateScreenSurfaceRadiance(int2 pixel, float3 normal)
{
    float3 albedo = DecodeSRGB(GET_PIXEL_TEX2D(_albedo, pixel).rgb);
    float3 sky = VoxelSkyColor(normal);
    float3 L = normalize(-sunDirection.xyz);
    float sunAmount = max(dot(normal, L), 0.0) / PI;
    float3 sun = sunColorAndIntensity.rgb * sunColorAndIntensity.w * sunAmount;
    return albedo * (sky + sun);
}

// A compact screen-space near-field gather. Coplanar samples reject naturally;
// nearby facing surfaces contribute colored contact bounce that complements
// the voxel cone tracing at sub-voxel scale.
float4 GatherScreenSpaceNearField(
    int2 centerPixel,
    float2 centerUV,
    float3 worldPosition,
    float3 normal)
{
    uint2 resolution = uint2(giParams2.y, giParams2.z);
    float rotation = frac(sin(dot(float2(centerPixel), float2(12.9898, 78.233))) * 43758.5453) * TAU;
    float3 gathered = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int ray = 0; ray < 4; ray++)
    {
        float angle = rotation + ray * (TAU / 4.0);
        float2 direction = float2(cos(angle), sin(angle));
        [unroll]
        for (int step = 1; step <= 6; step++)
        {
            float radiusPixels = 2.0 + step * step * 1.25;
            float2 sampleUV = centerUV + direction * radiusPixels / float2(resolution);
            if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            {
                break;
            }

            int2 samplePixel = clamp((int2)(sampleUV * float2(resolution)), 0, (int2)resolution - 1);
            float sampleDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
            if (sampleDepth >= 0.9999)
            {
                continue;
            }
            float3 samplePosition = ReconstructWorldPosition(sampleUV, sampleDepth);
            float3 toSample = samplePosition - worldPosition;
            float distance_ = length(toSample);
            if (distance_ < levelOrigins[0].w * 2.0 || distance_ > levelOrigins[0].w * 16.0)
            {
                continue;
            }

            float3 directionToSample = toSample / distance_;
            float receiverFacing = max(dot(normal, directionToSample), 0.0);
            float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
            float sourceFacing = max(dot(sampleNormal, -directionToSample), 0.0);
            float weight = receiverFacing * sourceFacing / (1.0 + distance_ * distance_);
            if (weight < 0.01)
            {
                continue;
            }

            gathered += ApproximateScreenSurfaceRadiance(samplePixel, sampleNormal) * weight;
            totalWeight += weight;
            break;
        }
    }

    if (totalWeight <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(gathered / totalWeight, saturate(totalWeight * 0.75));
}

// Low-roughness screen-space reflection. The voxel cone remains the fallback
// for rough, hidden and off-screen reflection paths.
float4 TraceScreenSpaceReflection(
    float3 startPosition,
    float3 direction,
    float roughness)
{
    uint2 resolution = uint2(giParams2.y, giParams2.z);
    float maximumDistance = min(giParams.y, levelOrigins[0].w * 128.0);
    float previousDifference = -1.0;
    [loop]
    for (int step = 1; step <= 24; step++)
    {
        float progress = step / 24.0;
        float distance_ = maximumDistance * progress * progress;
        float3 rayPosition = startPosition + direction * distance_;
        float4 clip = mul(viewProjection, float4(rayPosition, 1.0));
        if (clip.w <= 0.0)
        {
            break;
        }
        float3 ndc = clip.xyz / clip.w;
        float2 sampleUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0) || ndc.z <= 0.0 || ndc.z >= 1.0)
        {
            break;
        }

        int2 samplePixel = clamp((int2)(sampleUV * float2(resolution)), 0, (int2)resolution - 1);
        float sceneDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
        float difference = ndc.z - sceneDepth;
        if (sceneDepth < 0.9999 && difference >= 0.0 && previousDifference < 0.0)
        {
            float3 scenePosition = ReconstructWorldPosition(sampleUV, sceneDepth);
            float thickness = levelOrigins[0].w * 4.0 + distance_ * 0.025;
            if (length(scenePosition - rayPosition) <= thickness)
            {
                float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
                float edge = saturate(min(min(sampleUV.x, sampleUV.y), min(1.0 - sampleUV.x, 1.0 - sampleUV.y)) * 12.0);
                float confidence = edge * (1.0 - roughness);
                return float4(ApproximateScreenSurfaceRadiance(samplePixel, sampleNormal), confidence);
            }
        }
        previousDifference = difference;
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}

// Supplement coarse clipmap occlusion with visible depth-buffer intersections.
// This follows CE5's far screen-space opacity path: it is faded in only near
// the finest clipmap boundary, where projected voxel coverage becomes sparse.
float TraceScreenSpaceConeOpacity(
    float3 startPosition,
    float3 direction,
    float apertureTan,
    float maxDistance)
{
    uint2 resolution = uint2(giParams2.y, giParams2.z);
    float minimumDistance = levelOrigins[0].w * 4.0;
    float maximumDistance = min(maxDistance, levelOrigins[0].w * 96.0);
    if (maximumDistance <= minimumDistance)
    {
        return 0.0;
    }

    float previousDifference = -1.0;
    [unroll]
    for (int step = 1; step <= 8; step++)
    {
        float progress = step / 8.0;
        float distance_ = lerp(minimumDistance, maximumDistance, progress * progress);
        float3 rayPosition = startPosition + direction * distance_;
        float4 clip = mul(viewProjection, float4(rayPosition, 1.0));
        if (clip.w <= 0.0)
        {
            break;
        }

        float3 ndc = clip.xyz / clip.w;
        float2 sampleUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0) || ndc.z <= 0.0 || ndc.z >= 1.0)
        {
            break;
        }

        int2 samplePixel = clamp((int2)(sampleUV * float2(resolution)), 0, (int2)resolution - 1);
        float sceneDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
        float difference = ndc.z - sceneDepth;
        if (sceneDepth < 0.9999 && difference >= 0.0 && previousDifference < 0.0)
        {
            float3 scenePosition = ReconstructWorldPosition(sampleUV, sceneDepth);
            float coneRadius = max(levelOrigins[0].w * 2.0, distance_ * apertureTan);
            float separation = length(scenePosition - rayPosition);
            if (separation <= coneRadius * 2.0)
            {
                float edgeConfidence = saturate(
                    min(min(sampleUV.x, sampleUV.y), min(1.0 - sampleUV.x, 1.0 - sampleUV.y)) * 12.0);
                return saturate(1.0 - separation / (coneRadius * 2.0)) * edgeConfidence;
            }
        }
        previousDifference = difference;
    }
    return 0.0;
}

// Hardware trilinear sample of the radiance volume at a (fractional) mip;
// rgb = radiance, a = occupancy. All levels share the one Texture3D, stacked
// along the w axis.
float4 SampleRadiance(float3 position, int level, float mip)
{
    return SAMPLE_TEX3D_LEVEL(_radiance, VoxelWorldToUVW(position, level, mip), mip);
}

// Sample the directional opacity volume. xyz = directional opacity components
// (project onto |rayDir| for anisotropic occlusion), a = coverage fraction.
float4 SampleOpacity(float3 position, int level, float mip)
{
    return SAMPLE_TEX3D_LEVEL(_opacity, VoxelWorldToUVW(position, level, mip), mip);
}

// Sample radiance + opacity at a position, blending between the current level
// and the next coarser level near boundaries. This eliminates the hard popping
// that occurs when a cone ray crosses from one clipmap level to the next,
// because each level is independently voxelized with different data.
float4 SampleRadianceBlended(float3 position, int level, float mip, float3 absDir)
{
    float4 radSample = SampleRadiance(position, level, mip);
    float4 opaSample = SampleOpacity(position, level, mip);

    int levelCount = (int)clipmapParams.y;
    if (level + 1 < levelCount)
    {
        float boundaryWeight = VoxelLevelTransitionWeight(position, level);
        if (boundaryWeight > 0.001)
        {
            float nextVoxelSize = levelOrigins[level + 1].w;
            float curVoxelSize = levelOrigins[level].w;
            // Convert the mip to the coarser level's mip space.
            float nextMip = clamp(mip + log2(curVoxelSize / nextVoxelSize), 0.0, clipmapParams.z - 1.0);

            float4 nextRad = SAMPLE_TEX3D_LEVEL(_radiance,
                VoxelWorldToUVW(position, level + 1, nextMip), nextMip);
            float4 nextOpa = SAMPLE_TEX3D_LEVEL(_opacity,
                VoxelWorldToUVW(position, level + 1, nextMip), nextMip);

            radSample = lerp(radSample, nextRad, boundaryWeight);
            opaSample = lerp(opaSample, nextOpa, boundaryWeight);
        }
    }

    float voxelAlpha = dot(opaSample.xyz, absDir);
    voxelAlpha = max(voxelAlpha, radSample.a * 0.3);
    return float4(radSample.rgb, voxelAlpha);
}

// March one cone through the clipmap, accumulating radiance front-to-back.
// Uses anisotropic directional opacity: alpha at each step is projected from
// the opacity volume's xyz onto |cone direction|, matching CryEngine SVOGI.
// Returns rgb = gathered radiance, a = accumulated occlusion. Sky fallback is
// optional so diffuse and specular integration can keep separate semantics.
float4 TraceCone(float3 startPosition, float3 direction, float apertureTan, float maxDistance, float skyFallback)
{
    float mipCount = clipmapParams.z;
    float fineVoxelSize = levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    int startLevel = VoxelFindLevel(startPosition);
    float t = startLevel >= 0 ? VoxelEffectiveVoxelSize(startPosition, startLevel) * 0.5 : fineVoxelSize * 0.5;
    float3 absDir = abs(direction);

    for (int step = 0; step < 64 && t <= maxDistance && alpha < 0.98; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        float voxelSize = levelOrigins[level].w;
        float effectiveVoxelSize = VoxelEffectiveVoxelSize(position, level);
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        // Fractional mip: the sampler blends the neighboring mip levels.
        float mip = clamp(log2(diameter / voxelSize), 0.0, mipCount - 1.0);
        float4 sample = SampleRadianceBlended(position, level, mip, absDir);
        float marchDistance = max(effectiveVoxelSize * 0.5, diameter * 0.5);
        float integrationScale = saturate(marchDistance / max(diameter, effectiveVoxelSize));
        float effectiveLod = max(log2(effectiveVoxelSize / fineVoxelSize), 0.0);
        float coarseCoverageScale = 1.0 + 0.035 * effectiveLod * effectiveLod;
        float sampleAlpha = 1.0 - pow(
            saturate(1.0 - sample.a),
            integrationScale * coarseCoverageScale);

        color += (1.0 - alpha) * sampleAlpha * sample.rgb;
        alpha += (1.0 - alpha) * sampleAlpha;
        t += marchDistance;
    }

    // Specular fallback for the unoccluded part of the cone. Diffuse callers
    // pass zero because their sky baseline is evaluated by DeferredLighting.
    float skyVisibility = direction.z >= 0.0 ? skyFallback : 0.0;
    color += (1.0 - alpha) * VoxelSkyColor(direction) * skyVisibility;
    return float4(color, alpha);
}

// Trace the 9-cone diffuse hemisphere, cosine-weighted, through the radiance
// volume. Produces directional indirect diffuse unlike the former DDGI SH
// probes that could only represent low-frequency lighting.
float4 TraceDiffuseCones(
    float3 startPosition,
    float3 normal,
    float maxDistance,
    float screenSpaceOcclusionWeight)
{
    float3x3 tbn = GetTangentBasis(normal);
    float3 diffuse = 0.0;
    float occlusion = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (uint i = 0u; i < DIFFUSE_CONE_COUNT; i++)
    {
        float3 worldDir = mul(DIFFUSE_CONE_DIRECTIONS[i], tbn);
        float4 coneResult = TraceCone(startPosition, worldDir, DIFFUSE_CONE_APERTURE, maxDistance, 0.0);
        if (screenSpaceOcclusionWeight > 0.001)
        {
            float screenOpacity = TraceScreenSpaceConeOpacity(
                startPosition,
                worldDir,
                DIFFUSE_CONE_APERTURE,
                maxDistance) * screenSpaceOcclusionWeight;
            coneResult.rgb *= 1.0 - screenOpacity;
            coneResult.a = 1.0 - (1.0 - coneResult.a) * (1.0 - screenOpacity);
        }
        float weight = DIFFUSE_CONE_WEIGHTS[i];
        diffuse += coneResult.rgb * weight;
        occlusion += coneResult.a * weight;
        totalWeight += weight;
    }
    float inverseWeight = rcp(max(totalWeight, 0.0001));
    return float4(diffuse * inverseWeight, saturate(1.0 - occlusion * inverseWeight));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 tracePixel = dispatchId.xy;
    uint2 traceResolution = uint2(giParams.z, giParams.w);
    if (any(tracePixel >= traceResolution))
    {
        return;
    }

    float2 uv = (float2(tracePixel) + 0.5) / float2(traceResolution);
    uint2 gbufferResolution = uint2(giParams2.y, giParams2.z);
    int2 gbufferPixel = int2(uv * float2(gbufferResolution));
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 worldPosition = ReconstructWorldPosition(uv, depth);
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float roughness = GET_PIXEL_TEX2D(_mrAO, gbufferPixel).y;
    float3 V = normalize(cameraPosition.xyz - worldPosition);
    float maxDistance = giParams.y;

    // Bias by the voxel size of the finest clipmap level that contains this
    // surface. Using the global finest size self-intersects surfaces represented
    // only by coarse levels and makes their GI incorrectly black.
    float fineVoxelSize = levelOrigins[0].w;
    int surfaceLevel = VoxelFindLevel(worldPosition);
    float surfaceVoxelSize = surfaceLevel >= 0
        ? VoxelEffectiveVoxelSize(worldPosition, surfaceLevel)
        : fineVoxelSize;
    float receiverBias = max(fineVoxelSize * 2.0, surfaceVoxelSize * 0.5);
    float3 startPosition = worldPosition + N * receiverBias;

    // Diffuse: 9-cone hemisphere trace. RGB contains only bounced surface
    // radiance and alpha contains unoccluded environment visibility.
    float screenSpaceOcclusionWeight = VoxelLevelContains(worldPosition, 0)
        ? VoxelLevelTransitionWeight(worldPosition, 0)
        : 1.0;
    float4 diffuseResult = TraceDiffuseCones(
        startPosition,
        N,
        maxDistance,
        screenSpaceOcclusionWeight);

    // CE5 blends far screen-space opacity into the final diffuse alpha after
    // tree tracing. Conservatively account for unresolved projected coverage
    // as the receiver moves to coarser clipmap levels. This affects only sky
    // visibility; gathered bounce radiance keeps its physical energy.
    float receiverLod = saturate(log2(surfaceVoxelSize / fineVoxelSize));
    float unresolvedCoverageScale = 1.0 + 0.27 * receiverLod * receiverLod;
    diffuseResult.a = pow(saturate(diffuseResult.a), unresolvedCoverageScale);
    float3 diffuse = diffuseResult.rgb;

    // Specular: one cone along the reflection direction, aperture from roughness.
    float3 reflectDirection = reflect(-V, N);

    // CE5-style per-pixel spatial dithering with temporal flip.
    // Each pixel in a 4x4 tile samples a different sub-direction within the
    // cone footprint. The demosaic spatial filter then accumulates these 16
    // spatially-distributed samples into a smooth result. Frame-parity flips
    // (4 phases over 8 frames) add temporal variation — matching CryEngine's
    // kernel tiling + per-frame flip approach. This avoids the flickering
    // that golden-angle temporal jitter causes on narrow specular cones.
    float specularApertureTan = max(roughness * roughness, 0.06);
    uint2 tile = tracePixel & 3u;
    float2 spatialOffset = (float2(tile) + 0.5) / 4.0 - 0.5;
    uint frameHalf = uint(giFrameParams.x) / 2u;
    if (frameHalf & 1u) spatialOffset.x = -spatialOffset.x;
    if (frameHalf & 2u) spatialOffset.y = -spatialOffset.y;
    float3 jitterAxis = abs(reflectDirection.z) < 0.99 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 jitterRight = normalize(cross(reflectDirection, jitterAxis));
    float3 jitterUp = cross(reflectDirection, jitterRight);
    float3 jitteredDir = normalize(reflectDirection
        + (jitterRight * spatialOffset.x + jitterUp * spatialOffset.y) * specularApertureTan * 0.5);

    float3 specular = TraceCone(startPosition, jitteredDir, specularApertureTan, maxDistance, 1.0).rgb;
    if (roughness < 0.65)
    {
        // Fade the blend out ahead of the roughness gate: specular antialiasing
        // widens roughness with distance, so a hard cutoff here pops at range.
        float4 screenReflection = TraceScreenSpaceReflection(startPosition, reflectDirection, roughness);
        float gateFade = saturate((0.65 - roughness) * 10.0);
        specular = lerp(specular, screenReflection.rgb, screenReflection.a * (1.0 - roughness) * gateFade);
    }

    _indirectGI[tracePixel] = float4(diffuse, diffuseResult.a);
    _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(specular, 1.0);
}
