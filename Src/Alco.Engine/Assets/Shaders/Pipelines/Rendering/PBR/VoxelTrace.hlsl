#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// When defined, a voxel specular cone is traced along the reflection vector
// and blended with SSR as a fallback for off-screen / occluded reflection
// paths. Undefine to make SSR the sole specular source.
#define VOXEL_SPECULAR_CONE

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the configured
// screen-space trace resolution. Reconstructs world position and normal from the
// G-buffer, traces a deterministic rotation-balanced set of narrow diffuse
// cones plus one specular cone through the radiance volume. Diffuse cones use
// a 2x2 depth-weighted averaged geometry normal so cone directions stay stable
// across edges and tessellated relief. Every pixel visits the complete
// 64-direction kernel over time and accumulates it before spatial demosaic.
// The result is written into the output atlas (twice the
// trace width): total diffuse irradiance (visible sky plus bounced radiance)
// and diagnostic visibility in the left half, specular radiance in the right
// half.

// Bind groups: set 0 packs the per-dispatch inputs together with the shared
// uniform (binding 0, from VoxelCommon.hlsli); set 1 is the output atlas, so the
// pass needs two of the eight available sets.
DEFINE_TEX3D_SAMPLE(0, _radiance);
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_READ(0, _emissive);
DEFINE_TEX3D_SAMPLE(0, _opacity);
DEFINE_TEX2D_READ(0, _albedo);
DEFINE_TEX2D_DEPTH_SAMPLE(0, _shadowMap);
// Previous frame's temporally accumulated SSR (rgb = radiance, a = confidence).
DEFINE_TEX2D_READ(0, _ssrHistory);
// Previous frame's raw trace atlas. Diffuse and ALD are accumulated here
// before spatial demosaic so every trace pixel integrates its own directions.
DEFINE_TEX2D_READ(0, _traceHistory);
// Previous demosaic metadata, segment 5: x = linear depth, yzw = world normal.
DEFINE_TEX2D_READ(0, _giHistoryMetadata);
// Hi-Z depth pyramid for SSR hierarchical raymarching (7 mips, max-reduced).
DEFINE_TEX2D_READ(0, _depthPyramid);
DEFINE_TEX2D_STORAGE(1, _indirectGI, float4, "rgba16f");
// This frame's accumulated SSR output (ping-pong with _ssrHistory).
DEFINE_TEX2D_STORAGE(1, _ssrHistoryOut, float4, "rgba16f");

// A large cosine-hemisphere kernel starts with a different direction at each
// 8x8 screen phase, then every pixel traverses all 64 members through raw
// temporal accumulation. This keeps the first frames spatially stratified
// while making the converged integral independent of neighbouring geometry.
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

// Sun shadow for SSR / near-field hit points: single-tap CSM lookup with
// slope-scaled bias. Matches the inject pass's shadow logic so reflected
// surfaces respect the same shadow cascades as the lit scene.
float SampleSunShadowScreen(float3 worldPosition, float3 N)
{
    if (lightingParams.x < 0.5)
    {
        return 1.0;
    }
    float viewDistance = length(worldPosition - cameraPosition.xyz);
    int cascade = -1;
    if (viewDistance < cascadeSplits.x) cascade = 0;
    else if (viewDistance < cascadeSplits.y) cascade = 1;
    else if (viewDistance < cascadeSplits.z) cascade = 2;
    else if (viewDistance < cascadeSplits.w) cascade = 3;
    if (cascade < 0)
    {
        return 1.0;
    }

    float3 biasedWorld = worldPosition + N * cascadeTexelSizes[cascade];
    float4 clip = mul(sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;
    float NdotL = saturate(dot(N, normalize(-sunDirection.xyz)));
    float bias = 0.0003 + 0.0015 * (1.0 - NdotL);
    return SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, shadowUV, ndc.z - bias);
}

float3 ApproximateScreenSurfaceRadiance(int2 pixel, float3 normal, float3 worldPosition)
{
    float3 albedo = DecodeSRGB(GET_PIXEL_TEX2D(_albedo, pixel).rgb);
    float3 sky = VoxelSkyColor(normal);
    float3 L = normalize(-sunDirection.xyz);
    float sunAmount = max(dot(normal, L), 0.0) / PI;
    float shadow = SampleSunShadowScreen(worldPosition, normal);
    float3 sun = sunColorAndIntensity.rgb * sunColorAndIntensity.w * sunAmount * shadow;
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

            gathered += ApproximateScreenSurfaceRadiance(samplePixel, sampleNormal, samplePosition) * weight;
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

// --- Screen-space reflection (Hi-Z raymarch + GGX VNDF + temporal AABB) -----
// All SSR functions and bindings live in SsrCommon.hlsli. This include must
// come after ReconstructWorldPosition and ApproximateScreenSurfaceRadiance are
// defined, since TraceScreenSpaceReflection depends on them.
#include "Shaders/Pipelines/Rendering/PBR/SsrCommon.hlsli"

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
// and the next coarser level near boundaries. Each clipmap level is independently
// voxelized and scrolls by brick-sized quanta, so the radiance field changes
// phase at every brick boundary. Blending is applied on every sample (not only
// on the step where the ray crosses a level boundary) because the clipmap
// origin itself jumps, shifting the boundary relative to a fixed world position
// even when the ray stays inside the same level.
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

    // Directional opacity is projected onto the absolute ray direction. Do not
    // add an isotropic occupancy floor: it turns thin surfaces into volumetric
    // occluders and produces broad darkening around otherwise open receivers.
    float voxelAlpha = saturate(dot(opaSample.xyz, absDir));
    return float4(radSample.rgb, voxelAlpha);
}

// March one cone through the clipmap, accumulating radiance front-to-back.
// Uses anisotropic directional opacity: alpha at each step is projected from
// the opacity volume's xyz onto |cone direction|.
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
        float4 sample = SampleRadianceBlended(position, level, mip, absDir);
        // Radiance is faded over the first voxel of travel so a voxel right at
        // the cone origin cannot fully contribute. This suppresses residual
        // self-intersection acne on top of the receiver bias; occupancy still
        // accumulates unfaded.
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

// Trace one member of the diffuse kernel per pixel. The assignment cycles over
// the complete 64-direction set and is accumulated in raw trace history before
// demosaic, so angular convergence no longer depends on neighbouring geometry.
//
// ALD (Average Light Direction) is accumulated alongside RGB:
//   ald.xyz += direction * brightness
//   ald.w   += brightness
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
    uint frameIndex = uint(giFrameParams.x);
    uint sequenceIndex = DIFFUSE_DIRECTION_TILE[tileIndex];
    // Visit the complete 64-direction kernel at every pixel. Multiplication by
    // an odd number permutes all six-bit frame phases, and XOR applies that
    // permutation without changing the complete direction set present on any
    // individual frame. After 64 valid history samples the pixel therefore
    // owns the same hemisphere integral that previously had to be borrowed
    // from an 8x8 screen neighbourhood.
    uint temporalPhase = (frameIndex * 37u) & 63u;
    sequenceIndex ^= temporalPhase;
    float3 kernelDirection = GetDiffuseKernelDirection(sequenceIndex);

    // Dual-kernel approach: an "opacity" kernel lowers the cone elevation
    // toward the surface tangent, gathering more near-field occlusion for
    // stronger contact AO. The radiance and opacity directions are blended via
    // lerp(kernOpa, kern, transmittance*4): opaque surfaces (transmittance 0)
    // use the lowered direction; transparent surfaces fall back to the radiance
    // direction. Alco has no transmittance channel, so the blend always
    // resolves to the opacity direction. With DiffuseSpreading=0 the direction
    // is unchanged (identity).
    float3 opacityDirection = kernelDirection;
    opacityDirection.z -= giFrameParams.w;
    kernelDirection = normalize(opacityDirection);

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
        _ssrHistoryOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
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

    // Trace with a 2x2 depth-weighted averaged geometry normal instead of the
    // raw per-pixel normal. The relative depth test keeps normals from the
    // far side of a depth discontinuity out of the average, so cone directions
    // stay stable across edges and over tessellated relief instead of picking
    // per-pixel directions.
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

    // Specular reflection: SSR is always active. The voxel specular cone is
    // opt-in via VOXEL_SPECULAR_CONE — when enabled it provides a low-frequency
    // fallback that blends with SSR; when disabled SSR is the sole source.
    float3 reflectDirection = reflect(-V, detailNormal);
    float3 specular = 0.0;

#ifdef VOXEL_SPECULAR_CONE
    float specularApertureTan = max(roughness * roughness, 0.06);
    specular = TraceCone(
        startPosition, reflectDirection, specularApertureTan, maxDistance, 1.0, 0.5).rgb;
#endif

    // Screen-space reflection with dedicated temporal accumulation.
    float4 ssrAccumulated = float4(0.0, 0.0, 0.0, 0.0);
    if (roughness < 0.65)
    {
        // GGX VNDF importance sampling: perturb the mirror reflection for SSR
        // so temporal accumulation converges toward a correct glossy lobe.
        float3 ssrDirection = reflectDirection;
        if (roughness > 0.001)
        {
            float3 N = detailNormal;
            float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 T = normalize(cross(up, N));
            float3 B = cross(N, T);
            float3 Ve = float3(dot(V, T), dot(V, B), dot(V, N));

            float alpha = roughness * roughness;
            uint frameIdx = uint(giFrameParams.x);
            float2 rand = float2(
                SsrIGN(gbufferPixel, frameIdx),
                frac(SsrIGN(gbufferPixel, frameIdx) * 7.1));

            float3 Hh = SampleGGXVNDF(Ve, alpha, alpha, rand.x, rand.y);
            float3 H = normalize(Hh.x * T + Hh.y * B + Hh.z * N);
            ssrDirection = reflect(-V, H);
        }

        float4 screenReflection = TraceScreenSpaceReflection(
            worldPosition, ssrDirection, roughness, gbufferUV);

        // Temporal reprojection: find where this surface was last frame.
        float4 ssrHistory = float4(0.0, 0.0, 0.0, 0.0);
        bool historyValid = false;
        int2 prevPixel = int2(0, 0);
        if (giFrameParams.z > 0.5)
        {
            float4 prevClip = mul(viewProjectionPrev, float4(worldPosition, 1.0));
            if (prevClip.w > 0.0)
            {
                float2 prevNDC = float2(prevClip.x / prevClip.w, prevClip.y / prevClip.w);
                float2 prevUV = float2(prevNDC.x * 0.5 + 0.5, 0.5 - prevNDC.y * 0.5);
                if (all(prevUV >= 0.0) && all(prevUV <= 1.0))
                {
                    // Depth-consistency disocclusion test: compare the depth at
                    // the reprojected position to detect surfaces that were not
                    // visible last frame.
                    int2 prevGbufferPixel = clamp(
                        int2(prevUV * float2(gbufferResolution)),
                        int2(0, 0), int2(gbufferResolution) - 1);
                    float prevDepth = GET_PIXEL_TEX2D(_gbufferDepth, prevGbufferPixel);
                    float3 prevWorldPos = ReconstructWorldPosition(prevUV, prevDepth);
                    bool disocclusion = length(prevWorldPos - worldPosition) > levelOrigins[0].w * 4.0;

                    if (!disocclusion)
                    {
                        prevPixel = clamp(
                            int2(prevUV * float2(traceResolution)),
                            int2(0, 0),
                            int2((int)traceResolution.x - 1, (int)traceResolution.y - 1));
                        ssrHistory = _ssrHistory.Load(int3(prevPixel, 0));
                        historyValid = true;
                    }
                }
            }
        }

        if (historyValid)
        {
            // AABB neighbourhood clip: sample a 3×3 region around the history
            // pixel to build a colour bounding box, then clip the history
            // sample toward it. This prevents ghosting from stale radiance
            // while preserving legitimate detail.
            int2 histCenter = prevPixel;

            float3 histMin = 1e18;
            float3 histMax = -1e18;
            [unroll] for (int dy = -1; dy <= 1; dy++)
            {
                [unroll] for (int dx = -1; dx <= 1; dx++)
                {
                    float3 s = _ssrHistory.Load(int3(clamp(histCenter + int2(dx, dy),
                        int2(0, 0), int2((int)traceResolution.x - 1, (int)traceResolution.y - 1)), 0)).rgb;
                    histMin = min(histMin, s);
                    histMax = max(histMax, s);
                }
            }

            float3 clippedHistory = SsrClipAABB(histMin, histMax, ssrHistory.rgb);
            ssrHistory.a = min(ssrHistory.a, screenReflection.a);

            float blendRate = screenReflection.a > 0.01 ? 0.12 : 0.35;
            ssrAccumulated = float4(
                lerp(clippedHistory, screenReflection.rgb, blendRate),
                lerp(ssrHistory.a, screenReflection.a, blendRate));
        }
        else
        {
            ssrAccumulated = screenReflection;
        }

        _ssrHistoryOut[tracePixel] = ssrAccumulated;

#ifdef VOXEL_SPECULAR_CONE
        float gateFade = saturate((0.65 - roughness) * 10.0);
        specular = lerp(specular, ssrAccumulated.rgb,
            ssrAccumulated.a * (1.0 - roughness) * gateFade);
#else
        specular = ssrAccumulated.rgb * ssrAccumulated.a;
#endif
    }
    else
    {
        _ssrHistoryOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
    }

    // ALD (Average Light Direction): direction-weighted accumulation of cone
    // brightness. xyz = worldDir * brightness, w = brightness. The deferred
    // lighting pass normalises this to derive the dominant indirect light
    // direction and gives diffuse a directional response (corners darken,
    // surfaces facing the bounce-light source brighten) instead of treating
    // indirect light as flat ambient.
    float diffuseBrightness = length(diffuse);
    float4 ald = float4(diffuseWorldDir * diffuseBrightness, diffuseBrightness);

    // Per-pixel raw temporal integration. Unlike the old post-demosaic
    // history, this runs before any neighbouring screen phase is gathered, so
    // a narrow face accumulates its own angular samples and never needs the
    // orthogonal face beside it to complete the direction kernel.
    float4 temporalDiffuse = float4(diffuse, diffuseResult.a);
    float4 temporalAld = ald;
    float previousSampleCount = 0.0;
    bool rawHistoryValid = false;
    int2 previousTracePixel = int2(0, 0);

    if (giFrameParams.z > 0.5)
    {
        float4 previousClip = mul(
            viewProjectionPrev, float4(worldPosition, 1.0));
        if (previousClip.w > 0.0)
        {
            float2 previousNdc = previousClip.xy / previousClip.w;
            float2 previousUv = float2(
                previousNdc.x * 0.5 + 0.5,
                0.5 - previousNdc.y * 0.5);
            if (all(previousUv >= 0.0) && all(previousUv <= 1.0))
            {
                previousTracePixel = clamp(
                    int2(previousUv * float2(traceResolution)),
                    int2(0, 0),
                    int2(traceResolution) - 1);
                float4 previousMetadata = _giHistoryMetadata.Load(int3(
                    previousTracePixel
                        + int2((int)traceResolution.x * 5, 0),
                    0));
                float expectedPreviousDepth = abs(previousClip.w);
                float depthRatio = abs(
                    expectedPreviousDepth
                        / max(previousMetadata.x, 0.0001)
                    - 1.0);
                float3 previousNormal = normalize(
                    previousMetadata.yzw * 2.0 - 1.0);
                float normalAgreement = dot(
                    geometryNormal, previousNormal);

                float4 previousRawSpecular = _traceHistory.Load(int3(
                    previousTracePixel
                        + int2((int)traceResolution.x, 0),
                    0));
                previousSampleCount =
                    saturate(previousRawSpecular.a) * 64.0;
                rawHistoryValid = previousMetadata.x > 0.0
                    && previousSampleCount > 0.5
                    && depthRatio < 0.08
                    && normalAgreement > 0.8;
            }
        }
    }

    if (rawHistoryValid)
    {
        float4 previousDiffuse = _traceHistory.Load(int3(
            previousTracePixel, 0));
        float4 previousAld = _traceHistory.Load(int3(
            previousTracePixel
                + int2((int)traceResolution.x * 2, 0),
            0));
        // Bootstrap with an exact running average for the first complete
        // 64-direction cycle. Afterwards a small EMA keeps dynamic lighting
        // responsive while strongly attenuating the residual sampling period.
        float temporalBlend = previousSampleCount < 63.5
            ? rcp(previousSampleCount + 1.0)
            : 0.015625;
        temporalDiffuse = lerp(
            previousDiffuse, temporalDiffuse, temporalBlend);
        temporalAld = lerp(previousAld, temporalAld, temporalBlend);
    }

    float nextSampleCount = rawHistoryValid
        ? min(previousSampleCount + 1.0, 64.0)
        : 1.0;
    float rawHistoryAge = nextSampleCount / 64.0;

    _indirectGI[tracePixel] = temporalDiffuse;
    _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] =
        float4(specular, rawHistoryAge);
    _indirectGI[uint2(tracePixel.x + traceResolution.x * 2, tracePixel.y)] =
        temporalAld;
}
