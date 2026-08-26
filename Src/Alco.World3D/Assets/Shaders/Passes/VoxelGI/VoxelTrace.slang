// file "VoxelTrace.slang" - module "VoxelTrace"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module VoxelTrace;

import AlcoRendering_Core;
import AlcoWorld3D_VoxelCommon;
import AlcoWorld3D_GeometryNormal;
import AlcoWorld3D_ReversedDepth;

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the configured
// screen-space trace resolution. Reconstructs world position and normal from the
// G-buffer, traces a deterministic rotation-balanced set of narrow diffuse
// cones plus one specular cone through the radiance volume. Diffuse cones use
// a 2x2 depth-weighted averaged geometry normal so cone directions stay stable
// across edges and tessellated relief. Every pixel visits the complete
// 64-direction kernel over time and accumulates it before spatial demosaic.
// March positions are jittered by a per-pixel blue-noise factor (rotated per
// frame) so voxel-grid quantization crawl averages out under that accumulation.
// The result is written into a three-segment output atlas: total diffuse
// irradiance plus diagnostic visibility, voxel specular fallback, and average
// light direction data.

public struct PointLightData
{
    float4 positionRange;
    float4 colorIntensity;
};
// AlcoWorld3D_VoxelCommon declares data; this pass's ParameterBlock follows it
// in declaration order.
public struct PassParams
{
    public Texture3D radiance;
    public DepthTexture2D gbufferDepth;
    public Texture2D normal;
    public Texture2D emissive;
    public Texture3D opacity;
    public Texture2D albedo;
    public DepthTexture2D shadowMap;
// Previous frame's raw trace atlas. Diffuse and ALD are accumulated here
// before spatial demosaic so every trace pixel integrates its own directions.
    public Texture2D traceHistory;
// Previous demosaic metadata, segment 5: x = linear depth, yzw = world normal.
    public Texture2D giHistoryMetadata;
// 128x128 blue-noise tile baked once from
// SSRBlueNoise.slang (the same tile the SSR trace samples).
// Its B channel jitters the cone march below.
    public Texture2D blueNoise;
// Point lights are included in the compact screen-space near-field diffuse
// gather below.
    public RWStructuredBuffer<PointLightData> pointLights;
// Reflective shadow map (sun view of the selected CSM cascade): depth + sRGB
// albedo (alpha=1 marks rendered texels) + world normal. Bound by the voxel GI
// node; the fallback (valid 1x1 depth view, black albedo with alpha=0) disables
// sun-bounce injection before depth is read. The albedo slot is sampled (bilinear) so
// the bounce colour a receiver gathers is filtered instead of snapping to
// single RSM texels.
    public DepthTexture2D rsmDepth;
    public Texture2D rsmAlbedo;
    public Texture2D rsmNormal;
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> indirectGI;
};

public ParameterBlock<PassParams> pass;


// A large cosine-hemisphere kernel starts with a different direction at each
// 8x8 screen phase, then every pixel traverses all 64 members through raw
// temporal accumulation. This keeps the first frames spatially stratified
// while making the converged integral independent of neighbouring geometry.
static const float DIFFUSE_CONE_APERTURE = 1.0 / 24.0;
static const uint DIFFUSE_BOOTSTRAP_SAMPLE_COUNT = 4u;

// Must match BlueNoiseTextureSize on the C# side and SSR_BLUE_NOISE_SIZE in
// the bake shader.
static const uint BLUE_NOISE_TILE = 128u;

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
float3x3 getTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return float3x3(tangent, bitangent, normal);
}

// Generate one member of the deterministic 64-direction cosine-weighted
// hemisphere kernel. The Bayer screen tile distributes neighbouring polar
// strata, while the golden-angle azimuth avoids aligned rings.
float3 getDiffuseKernelDirection(uint sequenceIndex)
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

// --- Utility helpers --------------------------------------------------------

float3 decodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow((color + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

float3 reconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(data.invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// Recover view-linear depth from the homogeneous w produced by the inverse
// view-projection (fourth matrix row only).
float reconstructLinearDepth(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float reciprocalClipW = dot(data.invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

// Sun shadow for near-field hit points: single-tap CSM lookup with
// slope-scaled bias. Matches the inject pass's shadow logic so reflected
// surfaces respect the same shadow cascades as the lit scene.
float sampleSunShadowScreen(float3 worldPosition, float3 n)
{
    if (!data.shadowEnabled)
    {
        return 1.0;
    }
    float viewDistance = length(worldPosition - data.cameraPosition.xyz);
    int cascade = -1;
    if (viewDistance < data.cascadeSplits.x) cascade = 0;
    else if (viewDistance < data.cascadeSplits.y) cascade = 1;
    else if (viewDistance < data.cascadeSplits.z) cascade = 2;
    else if (viewDistance < data.cascadeSplits.w) cascade = 3;
    if (cascade < 0)
    {
        return 1.0;
    }

    float3 biasedWorld = worldPosition + n * data.cascadeTexelSizes[cascade];
    float4 clip = mul(data.sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;
    float ndotL = saturate(dot(n, normalize(-data.sunDirection.xyz)));
    float bias = 0.0003 + 0.0015 * (1.0 - ndotL);
    return pass.shadowMap.SampleCmpLevelZero(samplers.depthComparison, shadowUV, ndc.z - bias);
}

float3 approximateScreenSurfaceRadiance(int2 pixel, float3 normal, float3 worldPosition)
{
    float3 albedo = decodeSRGB(pass.albedo.Load(int3(pixel, 0)).rgb);
    float3 emissive = pass.emissive.Load(int3(pixel, 0)).rgb;
    float3 sky = voxelSkyColor(normal);
    float3 l = normalize(-data.sunDirection.xyz);
    float sunAmount = max(dot(normal, l), 0.0) / PI;
    float shadow = sampleSunShadowScreen(worldPosition, normal);
    float3 direct = data.sunColorAndIntensity.rgb * data.sunColorAndIntensity.w * sunAmount * shadow;

    uint lightCount = (uint)data.numPointLights;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = pass.pointLights[i].positionRange;
        float4 colInt = pass.pointLights[i].colorIntensity;
        if (colInt.w <= 0.0)
        {
            continue;
        }

        float3 toLight = posRange.xyz - worldPosition;
        float distanceToLight = length(toLight);
        if (posRange.w > 0.0 && distanceToLight > posRange.w)
        {
            continue;
        }

        float attenuation = 1.0 / (distanceToLight * distanceToLight + 1.0);
        if (posRange.w > 0.0)
        {
            float falloff = saturate(1.0 - distanceToLight / posRange.w);
            attenuation *= falloff * falloff;
        }

        float pointNdotL = max(dot(normal, toLight / max(distanceToLight, 1e-6)), 0.0);
        direct += colInt.rgb * colInt.w * attenuation * (pointNdotL / PI);
    }

    return emissive + albedo * (sky + direct);
}

// A compact screen-space near-field gather. Coplanar samples reject naturally;
// nearby facing surfaces contribute colored contact bounce that complements
// the voxel cone tracing at sub-voxel scale.
float4 gatherScreenSpaceNearField(
    int2 centerPixel,
    float2 centerUV,
    float3 worldPosition,
    float3 normal)
{
    uint2 resolution = uint2(data.gbufferWidth, data.gbufferHeight);
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
            float sampleDepth = getPixelDepth2D(pass.gbufferDepth, samplePixel);
            if (isSkyDepth(sampleDepth))
            {
                continue;
            }
            float3 samplePosition = reconstructWorldPosition(sampleUV, sampleDepth);
            float3 toSample = samplePosition - worldPosition;
            float distance = length(toSample);
            if (distance < data.levelOrigins[0].w * 2.0 || distance > data.levelOrigins[0].w * 16.0)
            {
                continue;
            }

            float3 directionToSample = toSample / distance;
            float receiverFacing = max(dot(normal, directionToSample), 0.0);
            float3 sampleNormal = normalize(pass.normal.Load(int3(samplePixel, 0)).xyz * 2.0 - 1.0);
            float sourceFacing = max(dot(sampleNormal, -directionToSample), 0.0);
            float weight = receiverFacing * sourceFacing / (1.0 + distance * distance);
            if (weight < 0.01)
            {
                continue;
            }

            gathered += approximateScreenSurfaceRadiance(samplePixel, sampleNormal, samplePosition) * weight;
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

// Hardware trilinear sample of the radiance volume at a (fractional) mip;
// rgb = radiance, a = occupancy. All levels share the one Texture3D, stacked
// along the w axis.
float4 sampleRadiance(float3 position, int level, float mip)
{
    return pass.radiance.SampleLevel(samplers.linearClamp, voxelWorldToUVW(position, level, mip), mip);
}

// Sample the directional opacity volume. xyz = directional opacity components
// (project onto |rayDir| for anisotropic occlusion), a = coverage fraction.
float4 sampleOpacity(float3 position, int level, float mip)
{
    return pass.opacity.SampleLevel(samplers.linearClamp, voxelWorldToUVW(position, level, mip), mip);
}

// Sample radiance + opacity at a position, blending between the current level
// and the next coarser level near boundaries. Each clipmap level is independently
// voxelized and scrolls by brick-sized quanta, so the radiance field changes
// phase at every brick boundary. Blending is applied on every sample (not only
// on the step where the ray crosses a level boundary) because the clipmap
// origin itself jumps, shifting the boundary relative to a fixed world position
// even when the ray stays inside the same level.
float4 sampleRadianceBlended(float3 position, int level, float mip, float3 absDir)
{
    float4 radSample = sampleRadiance(position, level, mip);
    float4 opaSample = sampleOpacity(position, level, mip);

    int levelCount = (int)data.levelCount;
    if (level + 1 < levelCount)
    {
        float boundaryWeight = voxelLevelTransitionWeight(position, level);
        if (boundaryWeight > 0.001)
        {
            float nextVoxelSize = data.levelOrigins[level + 1].w;
            float curVoxelSize = data.levelOrigins[level].w;
            // Convert the mip to the coarser level's mip space.
            float nextMip = clamp(mip + log2(curVoxelSize / nextVoxelSize), 0.0, data.mipCount - 1.0);

            float4 nextRad = pass.radiance.SampleLevel(
                samplers.linearClamp,
                voxelWorldToUVW(position, level + 1, nextMip), nextMip);
            float4 nextOpa = pass.opacity.SampleLevel(
                samplers.linearClamp,
                voxelWorldToUVW(position, level + 1, nextMip), nextMip);

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
// marchJitter is a per-pixel, per-frame blue-noise factor in [0,1) that scales
// the initial offset and every step (see mainCS).
float4 traceCone(
    float3 startPosition,
    float3 direction,
    float apertureTan,
    float maxDistance,
    float skyFallback,
    float marchStepScale,
    float marchJitter,
    bool injectRsmSun)
{
    float mipCount = data.mipCount;
    float fineVoxelSize = data.levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    int startLevel = voxelFindLevel(startPosition);
    float t = startLevel >= 0 ? voxelEffectiveVoxelSize(startPosition, startLevel) * 0.5 : fineVoxelSize * 0.5;
    // The radiance volume quantizes the field in world space, so unjittered
    // marches lock to the same grid phase and a moving receiver crawls across
    // texel boundaries coherently — worst in the coarse far levels, where the
    // brick-aligned mip clamp leaves the cone under-filtered. The jitter
    // spreads the sample phase across neighbouring pixels and frames,
    // redistributing that crawl into high-frequency dither that the demosaic
    // bilateral and the temporal accumulation average out.
    t *= 0.85 + 0.3 * marchJitter;
    float3 absDir = abs(direction);
    int prevLevel = -2;
    float effectiveVoxelSize = fineVoxelSize;

    for (int step = 0; step < 48 && t <= maxDistance && alpha < 0.98; step++)
    {
        float3 position = startPosition + direction * t;
        int level = voxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        bool levelChanged = level != prevLevel;
        if (levelChanged)
        {
            effectiveVoxelSize = voxelEffectiveVoxelSize(position, level);
            prevLevel = level;
        }

        float voxelSize = data.levelOrigins[level].w;
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        // A clipmap origin scrolls by one 8^3 brick. Mips above 3 are not
        // world-aligned under that translation (8 texels become half a texel
        // at mip 4), so sampling them makes the radiance field change phase at
        // every brick boundary.
        float mip = clamp(log2(diameter / voxelSize), 0.0,
            min(mipCount - 1.0, VOXEL_BRICK_ALIGNED_MAX_MIP));
        float4 sample = sampleRadianceBlended(position, level, mip, absDir);
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

        // RSM sun bounce (CE5 SVOTI final gather, CommonSVO.cfi ConeTraceBrick):
        // at EVERY march step, project into the sun-space RSM of the selected
        // cascade. While the cone travels through empty space in front of a
        // sunlit first surface (RSM depth behind the march depth), the step
        // gathers that surface's albedo radiating one bounce of sunlight — a
        // thin glowing shell in front of every sun-visible surface that cones
        // through open space accumulate. No occupancy gate: the shell lives in
        // empty voxels, and that is where receivers gather it from. The depth
        // comparison IS the occlusion test (an RSM texel is by definition the
        // first surface along its sun ray, so shadowed receivers project onto
        // a different depth and get nothing). On-surface steps are excluded by
        // the epsilon: their direct sun already lives in the voxel radiance.
        if (injectRsmSun && data.rsmIntensity > 0.0 && t < data.rsmMaxDistance)
        {
            uint rsmCascade = (uint)(data.rsmCascadeIndex + 0.5);
            float4 rsmClip = mul(data.sunViewProjection[rsmCascade], float4(position, 1.0));
            float3 rsmNdc = rsmClip.xyz / rsmClip.w;
            if (all(abs(rsmNdc.xy) <= 1.0) && rsmNdc.z >= 0.0 && rsmNdc.z <= 1.0)
            {
                // The RSM renders the whole cascade view (no atlas quadrant),
                // so unlike sampleSunShadowScreen the UV is not folded.
                float2 rsmUv = float2(rsmNdc.x * 0.5 + 0.5, 0.5 - rsmNdc.y * 0.5);
                int2 rsmTexel = int2(rsmUv * float2(data.rsmResolution, data.rsmResolution));
                // Bilinear albedo: neighbouring march points and neighbouring
                // receivers must see a continuous bounce colour, not per-texel
                // snaps. Cleared texels are (0,0,0,0), so filtering drives
                // alpha toward 0 at the sun-patch border; alpha becomes a
                // coverage weight and the rgb is un-premultiplied below.
                float4 rsmAlbedoSample = pass.rsmAlbedo.SampleLevel(samplers.linearClamp, rsmUv, 0.0);
                float rsmCoverage = saturate(rsmAlbedoSample.a);
                if (rsmCoverage > 0.05)
                {
                    float rsmDepth = getPixelDepth2D(pass.rsmDepth, rsmTexel);
                    // The glowing shell must be at least one and a half march
                    // steps thick (and never thinner than the fine voxel / RSM
                    // texel). A fixed fine-scale shell is thinner than the
                    // growing march step of distant receivers, so their cones
                    // stochastically skip it and converge into light spots
                    // instead of a uniform bounce — the shell scaling with the
                    // current step is what keeps every crossing sampled. CE5
                    // scales the tolerance with the current node size
                    // ((8/nodeSize)*450) for the same reason. data.rsmDepthRange is
                    // the cascade depth range (world->NDC-z scale),
                    // data.rsmTexelWorldSize the RSM texel world size.
                    float rsmShellWorld = max(
                        marchDistance * 1.5,
                        max(fineVoxelSize, data.rsmTexelWorldSize) * 1.5);
                    float fRsm = saturate(
                        1.0 - abs(rsmDepth - rsmNdc.z) * data.rsmDepthRange / rsmShellWorld);
                    // Strictly in front of the sunlit surface only.
                    if (fRsm > 0.0 && rsmDepth > rsmNdc.z + 0.0007)
                    {
                        float3 rsmNormal = normalize(pass.rsmNormal.Load(int3(rsmTexel, 0)).xyz * 2.0 - 1.0);
                        float3 bounceAlbedo = decodeSRGB(
                            min(rsmAlbedoSample.rgb / max(rsmCoverage, 0.5), 1.0));
                        // Minimum reflectance so deeply dark texels still
                        // bounce a visible amount of sun.
                        bounceAlbedo += saturate(data.rsmMinAlbedo - dot(bounceAlbedo, 0.333));
                        // CE5 rejects only surfaces significantly facing away
                        // from the receiver (dot(N, dir) < 0.7) and applies no
                        // ndotL: the texel's sun visibility is already implied
                        // by the RSM. A cosine-falloff here zeroes the grazing
                        // wall contacts that carry most street-level bounce.
                        float facingReceiver = saturate((0.7 - dot(rsmNormal, direction)) * 2.0);
                        float3 rsmLight = data.sunColorAndIntensity.rgb * data.sunColorAndIntensity.w *
                            facingReceiver * fRsm * rsmCoverage;
                        // CE5 applies no 1/pi: the term is radiance modulated by
                        // the user intensity, not a Lambertian irradiance.
                        color += (1.0 - alpha) * rsmLight * bounceAlbedo * data.rsmIntensity * nearFade;
                    }
                }
            }
        }

        alpha += (1.0 - alpha) * sample.a;
        t += marchDistance * (0.92 + 0.16 * marchJitter);
    }

    // Add directional sky radiance through the unoccluded part of the cone.
    // A hard direction.z threshold made one of the reduced diffuse cones gain
    // or lose an entire sky contribution when a mesh normal crossed the
    // horizon, producing bright polygon-sized blocks on nearly vertical
    // facades. The low-frequency sky is already filtered, so blend it through
    // a narrow horizon band instead.
    float skyVisibility = smoothstep(-0.12, 0.12, direction.z) * skyFallback;
    color += (1.0 - alpha) * voxelSkyColor(direction) * skyVisibility;
    return float4(color, alpha);
}

// Trace one member of the diffuse kernel per pixel. The assignment cycles over
// the complete 64-direction set and is accumulated in raw trace history before
// demosaic, so angular convergence no longer depends on neighbouring geometry.
//
// ALD (Average Light Direction) is accumulated alongside rgb:
//   ald.xyz += direction * brightness
//   ald.w   += brightness
// The deferred lighting pass uses ALD to give indirect light a directional
// diffuse response instead of treating it as flat ambient. A trace pixel
// normally traces one cone; a disoccluded pixel bootstraps several independent
// directions before temporal accumulation. The demosaic pass gathers the
// resulting ALD just as it does for rgb.
float4 traceDiffuseCones(
    float3 startPosition,
    float3 normal,
    float maxDistance,
    uint2 tracePixel,
    float marchJitter,
    float kernelRotation,
    uint temporalPhaseOffset,
    out float3 outWorldDir)
{
    float3x3 tbn = getTangentBasis(normal);
    uint tileIndex = (tracePixel.x & 7u) + ((tracePixel.y & 7u) << 3u);
    uint frameIndex = uint(data.frameIndex);
    uint sequenceIndex = DIFFUSE_DIRECTION_TILE[tileIndex];
    // Visit the complete 64-direction kernel at every pixel. Multiplication by
    // an odd number permutes all six-bit frame phases, and XOR applies that
    // permutation without changing the complete direction set present on any
    // individual frame. After 64 valid history samples the pixel therefore
    // owns the same hemisphere integral that previously had to be borrowed
    // from an 8x8 screen neighbourhood.
    uint temporalPhase =
        ((frameIndex * 37u) + temporalPhaseOffset) & 63u;
    sequenceIndex ^= temporalPhase;
    float3 kernelDirection = getDiffuseKernelDirection(sequenceIndex);

    // A planar receiver otherwise gives every point exactly the same 64-ray
    // quadrature after temporal convergence. Sharp RSM emitters then expose
    // the integer hit-count contours of that shared direction set as coherent
    // light bands. Rotate the complete set around the receiver normal with a
    // screen-space blue-noise angle. Keep the angle fixed for one complete
    // 64-direction cycle, then advance it by a golden-ratio phase so long-term
    // accumulation is not locked to a single quadrature orientation.
    float rotationSin;
    float rotationCos;
    sincos(kernelRotation, rotationSin, rotationCos);
    kernelDirection.xy = float2(
        kernelDirection.x * rotationCos - kernelDirection.y * rotationSin,
        kernelDirection.x * rotationSin + kernelDirection.y * rotationCos);

    // Dual-kernel approach: an "opacity" kernel lowers the cone elevation
    // toward the surface tangent, gathering more near-field occlusion for
    // stronger contact AO. The radiance and opacity directions are blended via
    // lerp(kernOpa, kern, transmittance*4): opaque surfaces (transmittance 0)
    // use the lowered direction; transparent surfaces fall back to the radiance
    // direction. Alco has no transmittance channel, so the blend always
    // resolves to the opacity direction. With DiffuseSpreading=0 the direction
    // is unchanged (identity).
    float3 opacityDirection = kernelDirection;
    opacityDirection.z -= data.diffuseSpreading;
    kernelDirection = normalize(opacityDirection);

    float3 worldDir = normalize(mul(kernelDirection, tbn));
    outWorldDir = worldDir;
    float4 coneResult = traceCone(
        startPosition, worldDir, DIFFUSE_CONE_APERTURE, maxDistance, 1.0, 1.0, marchJitter, true);
    return float4(coneResult.rgb, saturate(1.0 - coneResult.a));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 tracePixel = dispatchId.xy;
    uint2 traceResolution = uint2(data.traceWidth, data.traceHeight);
    if (any(tracePixel >= traceResolution))
    {
        return;
    }

    float2 uv = (float2(tracePixel) + 0.5) / float2(traceResolution);
    uint2 gbufferResolution = uint2(data.gbufferWidth, data.gbufferHeight);
    int2 gbufferPixel = int2(uv * float2(gbufferResolution));
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), int2(gbufferResolution) - 1);
    float depth = getPixelDepth2D(pass.gbufferDepth, gbufferPixel);
    if (isSkyDepth(depth))
    {
        pass.indirectGI[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        pass.indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        pass.indirectGI[uint2(tracePixel.x + traceResolution.x * 2, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    // uv is the canonical receiver location of this trace texel. Reconstructing
    // from the quantized G-buffer pixel center introduces a fixed +0.25 history-
    // texel bias at half resolution: every frame reads from the right and feeds
    // that color back into the pixel on the left. Keep receiver reconstruction
    // on the trace grid so a stationary reprojection lands on the exact same
    // history texel (the SSR trace follows the same rule).
    float3 worldPosition = reconstructWorldPosition(uv, depth);
    float4 packedNormal = pass.normal.Load(int3(gbufferPixel, 0));
    float4 packedAlbedo = pass.albedo.Load(int3(gbufferPixel, 0));
    float packedGeometryY = pass.emissive.Load(int3(gbufferPixel, 0)).a;
    float3 detailNormal = normalize(packedNormal.xyz * 2.0 - 1.0);
    float3 geometryNormal = decodeGeometryNormal(float2(packedNormal.a, packedGeometryY));
    float roughness = packedAlbedo.a;
    float3 v = normalize(data.cameraPosition.xyz - worldPosition);
    float maxDistance = data.traceMaxDistance;

    // Trace with a 2x2 depth-weighted averaged geometry normal instead of the
    // raw per-pixel normal. The relative depth test keeps normals from the
    // far side of a depth discontinuity out of the average, so cone directions
    // stay stable across edges and over tessellated relief instead of picking
    // per-pixel directions.
    float centerLinearDepth = reconstructLinearDepth(uv, depth);
    float3 n = geometryNormal;
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
                float sampleDepth = getPixelDepth2D(pass.gbufferDepth, normalPixel);
                if (isSkyDepth(sampleDepth))
                {
                    continue;
                }
                float2 sampleUV =
                    (float2(normalPixel) + 0.5) / float2(gbufferResolution);
                float sampleLinearDepth = reconstructLinearDepth(
                    sampleUV, sampleDepth);
                float sampleWeight = saturate(
                    0.12 - abs(1.0 - sampleLinearDepth / max(centerLinearDepth, 0.0001)))
                    + 0.001;
                float4 samplePackedNormal = pass.normal.Load(int3(normalPixel, 0));
                float samplePackedGeometryY = pass.emissive.Load(int3(normalPixel, 0)).a;
                averagedNormal += decodeGeometryNormal(float2(
                    samplePackedNormal.a, samplePackedGeometryY)) * sampleWeight;
                averagedWeight += sampleWeight;
            }
        }
        if (averagedWeight > 0.001)
        {
            n = normalize(averagedNormal / averagedWeight);
        }
    }

    // bias by the voxel size of the finest clipmap level that contains this
    // surface. Using the global finest size self-intersects surfaces represented
    // only by coarse levels and makes their GI incorrectly black.
    float fineVoxelSize = data.levelOrigins[0].w;
    int surfaceLevel = voxelFindLevel(worldPosition);
    float surfaceVoxelSize = surfaceLevel >= 0
        ? voxelEffectiveVoxelSize(worldPosition, surfaceLevel)
        : fineVoxelSize;
    float receiverBias = max(fineVoxelSize * 2.0, surfaceVoxelSize * 0.5);
    float3 startPosition = worldPosition + n * receiverBias;

    // One blue-noise sample per trace pixel drives the cone rotation and march
    // jitter. Rotation changes only after a complete 64-direction cycle, while
    // the B channel advances every frame so voxel-grid phases converge.
    uint2 noisePixel = tracePixel % BLUE_NOISE_TILE;
    float4 blueNoise = pass.blueNoise.Load(int3(int2(noisePixel), 0));
    uint diffuseCycle = uint(data.frameIndex) >> 6u;
    float kernelRotation = frac(
        blueNoise.r + float(diffuseCycle) * 0.6180339887) * TAU;
    float blueNoiseMarch = blueNoise.b;
    float marchJitter = frac(blueNoiseMarch + float(uint(data.frameIndex)) * 0.6180339887);

    // Diffuse rgb contains visible directional sky and bounced surface
    // radiance. Alpha is retained only as a diagnostic visibility output.
    float3 diffuseWorldDir;
    float4 diffuseResult = traceDiffuseCones(
        startPosition, n, maxDistance, tracePixel, marchJitter,
        kernelRotation, 0u, diffuseWorldDir);
    float3 diffuse = diffuseResult.rgb;

    // Voxel specular is now only the off-screen/occluded fallback. Screen-space
    // reflection runs after deferred lighting in ScreenSpaceReflectionRenderer,
    // where it can sample the actual completed HDR scene color.
    float3 reflectDirection = reflect(-v, detailNormal);
    float3 specular = 0.0;
    bool voxelSpecularEnabled = data.voxelSpecularEnabled > 0.5;

    if (voxelSpecularEnabled)
    {
        float specularApertureTan = max(roughness * roughness, 0.06);
        // Sky fallback stays off: in open scenes most reflection rays escape
        // every clipmap unoccluded, so the two-tone prefiltered sky would
        // dominate the fallback while visibly disagreeing with the detailed
        // atmosphere that SSR and the primary sky pass reflect. Misses keep
        // only the surface bounce gathered from occupied voxels.
        specular = traceCone(
            startPosition, reflectDirection, specularApertureTan, maxDistance, 0.0, 0.5, marchJitter, false).rgb;
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
    float4 previousDiffuse = 0.0;
    float4 previousAld = 0.0;

    if (data.historyValid)
    {
        float4 previousClip = mul(
            data.viewProjectionPrev, float4(worldPosition, 1.0));
        if (previousClip.w > 0.0)
        {
            float2 previousNdc = previousClip.xy / previousClip.w;
            float2 previousUv = float2(
                previousNdc.x * 0.5 + 0.5,
                0.5 - previousNdc.y * 0.5);
            if (all(previousUv >= 0.0) && all(previousUv <= 1.0))
            {
                // Reconstruct the previous-frame history with a custom 2x2
                // bilinear footprint. Each tap receives an independent
                // depth/normal validity weight before normalization, so a
                // foreground tap cannot invalidate (or contaminate) valid
                // background history during sub-pixel camera motion.
                float2 previousTexel =
                    previousUv * float2(traceResolution) - 0.5;
                int2 previousBasePixel = int2(floor(previousTexel));
                float2 previousFraction = frac(previousTexel);
                float expectedPreviousDepth = abs(previousClip.w);
                float3 previousViewDirection = normalize(
                    data.cameraPosition.xyz - worldPosition);
                float previousViewFacing = abs(dot(
                    previousViewDirection, geometryNormal));
                // Face-on receivers use a tight threshold; grazing surfaces
                // need more room because one trace pixel spans a much larger
                // view-depth interval there.
                float depthThreshold = lerp(
                    0.08, 0.02, previousViewFacing);
                float historyWeight = 0.0;
                float sampleCountSum = 0.0;

                [unroll]
                for (int historyY = 0; historyY < 2; historyY++)
                {
                    [unroll]
                    for (int historyX = 0; historyX < 2; historyX++)
                    {
                        int2 historyPixel = previousBasePixel
                            + int2(historyX, historyY);
                        bool historyInBounds = all(historyPixel >= int2(0, 0))
                            && all(historyPixel < int2(traceResolution));
                        if (!historyInBounds)
                        {
                            continue;
                        }

                        float bilinearWeight =
                            (historyX != 0
                                ? previousFraction.x
                                : 1.0 - previousFraction.x)
                            * (historyY != 0
                                ? previousFraction.y
                                : 1.0 - previousFraction.y);
                        float4 previousMetadata = pass.giHistoryMetadata.Load(int3(
                            historyPixel
                                + int2((int)traceResolution.x * 5, 0),
                            0));
                        if (previousMetadata.x <= 0.0)
                        {
                            continue;
                        }

                        float depthRatio = abs(
                            expectedPreviousDepth
                                / max(previousMetadata.x, 0.0001)
                            - 1.0);
                        float depthWeight = 1.0 - smoothstep(
                            depthThreshold * 0.5,
                            depthThreshold,
                            depthRatio);
                        float3 previousNormal = normalize(
                            previousMetadata.yzw * 2.0 - 1.0);
                        float normalWeight = smoothstep(
                            0.75, 0.95,
                            dot(geometryNormal, previousNormal));

                        float4 previousRawSpecular = pass.traceHistory.Load(int3(
                            historyPixel
                                + int2((int)traceResolution.x, 0),
                            0));
                        float sampleCount =
                            saturate(previousRawSpecular.a) * 64.0;
                        float tapWeight = bilinearWeight
                            * depthWeight
                            * normalWeight
                            * (sampleCount > 0.5 ? 1.0 : 0.0);
                        if (tapWeight <= 0.0)
                        {
                            continue;
                        }

                        previousDiffuse += pass.traceHistory.Load(int3(
                            historyPixel, 0)) * tapWeight;
                        previousAld += pass.traceHistory.Load(int3(
                            historyPixel
                                + int2((int)traceResolution.x * 2, 0),
                            0)) * tapWeight;
                        sampleCountSum += sampleCount * tapWeight;
                        historyWeight += tapWeight;
                    }
                }

                rawHistoryValid = historyWeight > 0.0001;
                if (rawHistoryValid)
                {
                    float inverseHistoryWeight = rcp(historyWeight);
                    previousDiffuse *= inverseHistoryWeight;
                    previousAld *= inverseHistoryWeight;
                    previousSampleCount =
                        sampleCountSum * inverseHistoryWeight;
                }
            }
        }
    }

    // Newly visible or disoccluded surfaces have no matching raw history. A
    // single randomly rotated cone is too noisy there, especially beside a
    // sharp sun/RSM bounce. Bootstrap four well-separated kernel directions
    // only for those pixels. Stable pixels keep the normal one-cone cost.
    float currentSampleCount = 1.0;
    if (!rawHistoryValid)
    {
        float4 bootstrapDiffuseSum = temporalDiffuse;
        float4 bootstrapAldSum = temporalAld;

        [unroll]
        for (uint bootstrapIndex = 1u;
             bootstrapIndex < DIFFUSE_BOOTSTRAP_SAMPLE_COUNT;
             bootstrapIndex++)
        {
            float3 bootstrapWorldDir;
            float bootstrapMarchJitter = frac(
                marchJitter + float(bootstrapIndex) * 0.25);
            float4 bootstrapDiffuse = traceDiffuseCones(
                startPosition, n, maxDistance, tracePixel,
                bootstrapMarchJitter, kernelRotation,
                bootstrapIndex * 16u, bootstrapWorldDir);
            float bootstrapBrightness = length(bootstrapDiffuse.rgb);
            bootstrapDiffuseSum += bootstrapDiffuse;
            bootstrapAldSum += float4(
                bootstrapWorldDir * bootstrapBrightness,
                bootstrapBrightness);
        }

        float inverseBootstrapCount =
            rcp(float(DIFFUSE_BOOTSTRAP_SAMPLE_COUNT));
        temporalDiffuse = bootstrapDiffuseSum * inverseBootstrapCount;
        temporalAld = bootstrapAldSum * inverseBootstrapCount;
        currentSampleCount = float(DIFFUSE_BOOTSTRAP_SAMPLE_COUNT);
    }

    if (rawHistoryValid)
    {
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
        : currentSampleCount;
    float rawHistoryAge = nextSampleCount / 64.0;

    pass.indirectGI[tracePixel] = temporalDiffuse;
    pass.indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] =
        float4(specular, rawHistoryAge);
    pass.indirectGI[uint2(tracePixel.x + traceResolution.x * 2, tracePixel.y)] =
        temporalAld;
}
