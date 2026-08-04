#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the configured
// screen-space trace resolution. Reconstructs world position and normal from the
// G-buffer, traces a deterministic rotation-balanced set of narrow diffuse
// cones plus one specular cone through the radiance volume. Diffuse cones use
// a 2x2 depth-weighted averaged geometry normal (CE5 GetAverNormAndSmooth) so
// cone directions stay stable across edges and tessellated relief. The kernel
// azimuth is mirrored on a four-frame cycle (CE5 SvoTracePS), letting the
// temporal resolve average out per-direction voxel quantization. The result is written into the output atlas (twice the trace
// width): total diffuse irradiance (visible sky plus bounced radiance) and
// diagnostic visibility in the left half, specular radiance in the right half.

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_READ(4, _emissive);
DEFINE_TEX2D_STORAGE(5, _indirectGI, float4, "rgba16f");
DEFINE_TEX3D_SAMPLE(6, _opacity);
DEFINE_TEX2D_READ(7, _albedo);

// CE5 distributes a large cosine-hemisphere kernel across a screen tile and
// resolves the complete tile. Trace one direction at each 8x8 screen phase;
// the resolve integrates all 64 directions. This is both more complete and
// cheaper than tracing four directions chosen from a two-polar-angle kernel.
static const float DIFFUSE_CONE_APERTURE = 1.0 / 24.0;

static const uint DIFFUSE_DIRECTION_TILE[64] = {
     0u, 32u,  8u, 40u,  2u, 34u, 10u, 42u,
    48u, 16u, 56u, 24u, 50u, 18u, 58u, 26u,
    12u, 44u,  4u, 36u, 14u, 46u,  6u, 38u,
    60u, 28u, 52u, 20u, 62u, 30u, 54u, 22u,
     3u, 35u, 11u, 43u,  1u, 33u,  9u, 41u,
    51u, 19u, 59u, 27u, 49u, 17u, 57u, 25u,
    15u, 47u,  7u, 39u, 13u, 45u,  5u, 37u,
    63u, 31u, 55u, 23u, 61u, 29u, 53u, 21u,
};

// Build a world-space tangent basis from a single surface normal.
float3x3 GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return float3x3(tangent, bitangent, normal);
}

// Generate one member of the deterministic 64-direction cosine-weighted
// hemisphere kernel. The Bayer screen tile distributes neighbouring polar
// strata, while the golden-angle azimuth avoids aligned rings.
float3 GetDiffuseKernelDirection(uint sequenceIndex)
{
    float radialSample = (float(sequenceIndex) + 0.5) / 64.0;
    float radius = sqrt(radialSample);
    float azimuth = frac((float(sequenceIndex) + 0.5) * 0.61803398875) * TAU;
    float sine;
    float cosine;
    sincos(azimuth, sine, cosine);
    return float3(
        cosine * radius,
        sine * radius,
        sqrt(max(1.0 - radialSample, 0.0)));
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

// Recover view-linear depth from the homogeneous w produced by the inverse
// view-projection (fourth matrix row only).
float ReconstructLinearDepth(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float reciprocalClipW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
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
float4 SampleRadianceBlended(float3 position, int level, float mip, float3 absDir, bool enableBlend)
{
    float4 radSample = SampleRadiance(position, level, mip);
    float4 opaSample = SampleOpacity(position, level, mip);

    if (enableBlend)
    {
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
    }

    // CE5 projects directional opacity onto the absolute ray direction. Do not
    // add an isotropic occupancy floor: it turns thin surfaces into volumetric
    // occluders and produces broad darkening around otherwise open receivers.
    float voxelAlpha = saturate(dot(opaSample.xyz, absDir));
    return float4(radSample.rgb, voxelAlpha);
}

// March one cone through the clipmap, accumulating radiance front-to-back.
// Uses anisotropic directional opacity: alpha at each step is projected from
// the opacity volume's xyz onto |cone direction|, matching CryEngine SVOGI.
// Returns rgb = gathered surface radiance plus visible sky, a = accumulated
// occlusion. The caller selects whether the unoccluded cone reaches the sky.
float4 TraceCone(
    float3 startPosition,
    float3 direction,
    float apertureTan,
    float maxDistance,
    float skyFallback,
    float marchStepScale)
{
    float mipCount = clipmapParams.z;
    float fineVoxelSize = levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    int startLevel = VoxelFindLevel(startPosition);
    float t = startLevel >= 0 ? VoxelEffectiveVoxelSize(startPosition, startLevel) * 0.5 : fineVoxelSize * 0.5;
    float3 absDir = abs(direction);
    int prevLevel = -2;
    float effectiveVoxelSize = fineVoxelSize;

    for (int step = 0; step < 48 && t <= maxDistance && alpha < 0.98; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        bool levelChanged = level != prevLevel;
        if (levelChanged)
        {
            effectiveVoxelSize = VoxelEffectiveVoxelSize(position, level);
            prevLevel = level;
        }

        float voxelSize = levelOrigins[level].w;
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        // A clipmap origin scrolls by one 8^3 brick. Mips above 3 are not
        // world-aligned under that translation (8 texels become half a texel
        // at mip 4), so sampling them makes the radiance field change phase at
        // every brick boundary.
        float mip = clamp(log2(diameter / voxelSize), 0.0,
            min(mipCount - 1.0, VOXEL_BRICK_ALIGNED_MAX_MIP));
        float4 sample = SampleRadianceBlended(position, level, mip, absDir, levelChanged);
        // CE5 ConeTraceBrick fades radiance over the first voxel of travel so
        // a voxel right at the cone origin cannot fully contribute. This
        // suppresses residual self-intersection acne on top of the receiver
        // bias; occupancy still accumulates unfaded.
        float nearFade = saturate(t / voxelSize);
        // A one-footprint step is sufficient for diffuse cones because the
        // sampled mip already represents that footprint. Specular passes 0.5
        // to retain the previous oversampling needed by sharp reflections.
        float marchDistance = max(effectiveVoxelSize, diameter) * marchStepScale;

        color += (1.0 - alpha) * sample.a * sample.rgb * nearFade;
        alpha += (1.0 - alpha) * sample.a;
        t += marchDistance;
    }

    // Add directional sky radiance through the unoccluded part of the cone.
    // A hard direction.z threshold made one of the reduced diffuse cones gain
    // or lose an entire sky contribution when a mesh normal crossed the
    // horizon, producing bright polygon-sized blocks on nearly vertical
    // facades. The low-frequency sky is already filtered, so blend it through
    // a narrow horizon band instead.
    float skyVisibility = smoothstep(-0.12, 0.12, direction.z) * skyFallback;
    color += (1.0 - alpha) * VoxelSkyColor(direction) * skyVisibility;
    return float4(color, alpha);
}

// Trace one member of the tiled diffuse kernel per pixel. The demosaic pass
// gathers the complete 8x8 tile, as in CE5, so temporal history remains a
// denoising aid rather than being required for angular convergence. As in CE5
// SvoTracePS the assignment rotates per frame: the kernel azimuth is mirrored
// on a four-frame cycle, so the temporal accumulation integrates several
// direction sets per pixel instead of converging onto the voxel-quantization
// pattern of one static direction (visible as teeth along occlusion
// boundaries).
//
// CE5 ConeTracePS accumulates ALD (Average Light Direction) alongside RGB:
//   vALD.xyz += r.direction * brightness
//   vALD.w   += brightness
// The deferred lighting pass uses ALD to give indirect light a directional
// diffuse response instead of treating it as flat ambient. Each trace pixel
// traces one cone, so it outputs one ALD contribution; the demosaic pass
// gathers the full tile just as it does for RGB.
float4 TraceDiffuseCones(
    float3 startPosition,
    float3 normal,
    float maxDistance,
    uint2 tracePixel,
    out float3 outWorldDir)
{
    float3x3 tbn = GetTangentBasis(normal);
    uint tileIndex = (tracePixel.x & 7u) + ((tracePixel.y & 7u) << 3u);
    // CE5 SvoTracePS rotates the kernel assignment per frame so the temporal
    // accumulation integrates several direction sets per pixel instead of
    // converging onto one static direction's voxel-quantization pattern
    // (visible as teeth along occlusion boundaries). Only the azimuth is
    // mirrored, on a four-frame cycle: swapping in the complementary half of
    // the kernel (CE5's odd-frame behavior) trades the elevation stratum of
    // every pixel each frame, which oscillates the accumulated value at
    // occlusion terminators faster than the history window can settle.
    uint frameIndex = uint(giFrameParams.x);
    float3 kernelDirection = GetDiffuseKernelDirection(DIFFUSE_DIRECTION_TILE[tileIndex]);
    if ((frameIndex & 1u) != 0u) kernelDirection.x = -kernelDirection.x;
    if ((frameIndex & 2u) != 0u) kernelDirection.y = -kernelDirection.y;
    float3 worldDir = normalize(mul(kernelDirection, tbn));
    outWorldDir = worldDir;
    float4 coneResult = TraceCone(
        startPosition, worldDir, DIFFUSE_CONE_APERTURE, maxDistance, 1.0, 1.0);
    return float4(coneResult.rgb, saturate(1.0 - coneResult.a));
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
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), int2(gbufferResolution) - 1);
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferResolution);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[uint2(tracePixel.x + traceResolution.x * 2, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 worldPosition = ReconstructWorldPosition(gbufferUV, depth);
    float4 packedNormal = GET_PIXEL_TEX2D(_normal, gbufferPixel);
    float4 packedAlbedo = GET_PIXEL_TEX2D(_albedo, gbufferPixel);
    float packedGeometryY = GET_PIXEL_TEX2D(_emissive, gbufferPixel).a;
    float3 detailNormal = normalize(packedNormal.xyz * 2.0 - 1.0);
    float3 geometryNormal = DecodeGeometryNormal(float2(packedNormal.a, packedGeometryY));
    float roughness = packedAlbedo.a;
    float3 V = normalize(cameraPosition.xyz - worldPosition);
    float maxDistance = giParams.y;

    // CE5 GetAverNormAndSmooth: trace with a 2x2 depth-weighted averaged
    // geometry normal instead of the raw per-pixel normal. The relative depth
    // test keeps normals from the far side of a depth discontinuity out of
    // the average, so cone directions stay stable across edges and over
    // tessellated relief instead of picking per-pixel directions.
    float centerLinearDepth = ReconstructLinearDepth(gbufferUV, depth);
    float3 N = geometryNormal;
    {
        float3 averagedNormal = 0.0;
        float averagedWeight = 0.0;
        [unroll]
        for (int normalY = 0; normalY <= 1; normalY++)
        {
            [unroll]
            for (int normalX = 0; normalX <= 1; normalX++)
            {
                int2 normalPixel = clamp(
                    gbufferPixel + int2(normalX, normalY),
                    int2(0, 0),
                    int2(gbufferResolution) - 1);
                float sampleDepth = GET_PIXEL_TEX2D(_gbufferDepth, normalPixel);
                if (sampleDepth >= 0.9999)
                {
                    continue;
                }
                float2 sampleUV =
                    (float2(normalPixel) + 0.5) / float2(gbufferResolution);
                float sampleLinearDepth = ReconstructLinearDepth(
                    sampleUV, sampleDepth);
                float sampleWeight = saturate(
                    0.12 - abs(1.0 - sampleLinearDepth / max(centerLinearDepth, 0.0001)))
                    + 0.001;
                float4 samplePackedNormal = GET_PIXEL_TEX2D(_normal, normalPixel);
                float samplePackedGeometryY = GET_PIXEL_TEX2D(_emissive, normalPixel).a;
                averagedNormal += DecodeGeometryNormal(float2(
                    samplePackedNormal.a, samplePackedGeometryY)) * sampleWeight;
                averagedWeight += sampleWeight;
            }
        }
        if (averagedWeight > 0.001)
        {
            N = normalize(averagedNormal / averagedWeight);
        }
    }

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

    // Diffuse RGB contains visible directional sky and bounced surface
    // radiance. Alpha is retained only as a diagnostic visibility output.
    float3 diffuseWorldDir;
    float4 diffuseResult = TraceDiffuseCones(startPosition, N, maxDistance, tracePixel, diffuseWorldDir);
    float3 diffuse = diffuseResult.rgb;

    // Specular: one deterministic cone along the reflection direction. A
    // screen-tiled, frame-flipped direction made the low-frequency reflection
    // pattern crawl and forced temporal history to hide it, which in turn
    // produced visible camera-motion trails.
    float3 reflectDirection = reflect(-V, detailNormal);
    float specularApertureTan = max(roughness * roughness, 0.06);
    float3 specular = TraceCone(
        startPosition, reflectDirection, specularApertureTan, maxDistance, 1.0, 0.5).rgb;
    if (roughness < 0.65)
    {
        // Fade the blend out ahead of the roughness gate: specular antialiasing
        // widens roughness with distance, so a hard cutoff here pops at range.
        float4 screenReflection = TraceScreenSpaceReflection(startPosition, reflectDirection, roughness);
        float gateFade = saturate((0.65 - roughness) * 10.0);
        specular = lerp(specular, screenReflection.rgb, screenReflection.a * (1.0 - roughness) * gateFade);
    }

    // CE5 ALD (Average Light Direction): direction-weighted accumulation of
    // cone brightness. xyz = worldDir * brightness, w = brightness. The
    // deferred lighting pass normalises this to derive the dominant indirect
    // light direction and gives diffuse a directional response (corners
    // darken, surfaces facing the bounce-light source brighten) instead of
    // treating indirect light as flat ambient.
    float diffuseBrightness = length(diffuse);
    float4 ald = float4(diffuseWorldDir * diffuseBrightness, diffuseBrightness);

    _indirectGI[tracePixel] = float4(diffuse, diffuseResult.a);
    _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(specular, 1.0);
    _indirectGI[uint2(tracePixel.x + traceResolution.x * 2, tracePixel.y)] = ald;
}
