// file "volumetric-clouds.slang" - module "volumetric_clouds"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricClouds.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module volumetric_clouds;

import alco_rendering_core;
import alco_world3d_reversed_depth;
import alco_world3d_pbr_common;
import alco_world3d_clouds;
import alco_world3d_atmosphere;

// Volumetric clouds march pass for the deferred PBR pipeline: a half-
// resolution full-screen pass that ray-marches a cloud slab (Perlin-Worley
// base shape reshaped by coverage + height gradient, Worley-detail erosion)
// against the shared G-buffer depth, and outputs premultiplied HDR scattering
// in RGB with opacity in A. VolumetricCloudsComposite.hlsl then upsamples the
// result depth-aware and blends it over the completed HDR scene color.
//
// Performance pillars:
//   - half-resolution march with a per-pixel IGN-dithered start
//   - empty-space skipping: a cheap base-shape sample gates the expensive
//     detail-erosion sample, and empty space advances at double step length
//   - perspective step growth (step length scales with distance) so far
//     clouds cost as few samples as their screen footprint warrants
//   - early-out once transmittance is exhausted, and the march is clamped to
//     the scene depth and a maximum range with an aerial-perspective fade
//     into the (already rendered) horizon sky.
//
// The cloud lighting is adapted to the procedural sky: the sun energy is the
// atmosphere's own solar radiance scale attenuated by an analytic sun-
// transmittance march evaluated at cloud height (so sunset clouds stay lit
// and redden naturally after the ground light has faded), and the ambient is
// the CPU-filtered sky gradient (horizon at the slab base, zenith above).
//
// Quality knobs arrive as uniforms (no shader variants): the march step count
// is capped by cloudFade.w-ish (see _cloudData below).

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// This pass's resources form two ParameterBlocks after alco_world3d_pbr_common's
// blocks: the shared G-buffer depth with the cloud parameters, and the noise
// textures below.


// Cloud parameters, assembled by RGNode_VolumetricClouds. Layout must match
// the VolumetricCloudsData struct in RGNode_VolumetricClouds.cs exactly.
public struct CloudDataParams
{
    public float4 cloudParams;  // x=coverage(0..1) y=density multiplier z=bottom altitude km w=slab thickness km
    public float4 cloudParams2; // x=detailStrength y=extinction 1/km z=march resolution scale w=max march steps
    public float4 cloudWind;    // xy=accumulated wind offset km z=time seconds w=detail drift phase
    public float4 cloudLight;   // x=ambient strength y=sun strength z=aerial fade start km w=aerial fade end km
    public float4 cloudDebug;   // x=opacity debug view (grayscale, composite pass) y=shadow bake half extent km zw=shadow bake center (world xz, km)

    public DepthTexture2D _gbufferDepth;
};

public ParameterBlock<CloudDataParams> _cloudData;



// Cloud noise (generated once by volumetric-cloud-noise.slang), sampled
// through the shared sampler bank's linear-repeat member.
public struct CloudNoiseParams
{
    public Texture3D _cloudBaseNoise;
    public Texture3D _cloudDetailNoise;
};

public ParameterBlock<CloudNoiseParams> _cloudNoise;


// World periods of the noise textures (one texture repeat per period), km.
static const float CLOUD_BASE_SCALE = 1.0 / 24.0;
static const float CLOUD_COVERAGE_SCALE = 1.0 / 72.0;
static const float CLOUD_DETAIL_SCALE = 1.0 / 2.0;

// Hard cap on the march length past the slab entry (km). Rays near the
// horizon traverse megameters through the slab; beyond this the clouds
// dissolve into the horizon sky (see the aerial fade at the end).
static const float CLOUD_MAX_MARCH_KM = 42.0;

// March step bounds (km): steps grow with distance at the perspective rate
// until this cap; empty space advances at double length. The cap stays well
// below the slab thickness so mid-range clouds integrate over enough segments
// to look soft once the per-pixel jitter decorrelates the sample sequence.
static const float CLOUD_DT_MIN = 0.06;
static const float CLOUD_DT_MAX = 0.45;
static const float CLOUD_STEP_SCALE = 0.045;

// Sun-march offsets (km): fixed growing gaps concentrate samples inside the
// slab (the march leaves the slab toward the sun in a few kilometers).
static const float CLOUD_SUN_MARCH_OFFSETS[6] = {
    0.15, 0.45, 1.05, 2.25, 4.5, 9.0 };

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// Base-shape density (no detail erosion) at a cloud-slab position in km.
// Cheap enough for empty-space gating and the sun light march.
float CloudBaseDensity(float3 posKm, float bottomKm, float thicknessKm, float coverage)
{
    float h = saturate((posKm.z - bottomKm) / thicknessKm);
    float3 winded = posKm + float3(_cloudData.cloudWind.xy, 0.0);
    float lowFreq = _cloudNoise._cloudBaseNoise.SampleLevel(_samplers._linearRepeat, winded * CLOUD_COVERAGE_SCALE, 0).r;
    float4 baseNoise = _cloudNoise._cloudBaseNoise.SampleLevel(_samplers._linearRepeat, winded * CLOUD_BASE_SCALE, 0);
    float coverageMask = CloudCoverageMask(lowFreq, coverage);
    float heightGradient = CloudHeightGradient(h, coverage);
    return CloudBaseShape(baseNoise, coverageMask, heightGradient);
}

// Full density: base shape eroded by the animated detail octaves.
float CloudFullDensity(float3 posKm, float bottomKm, float thicknessKm, float coverage, float detailStrength)
{
    float base = CloudBaseDensity(posKm, bottomKm, thicknessKm, coverage);
    if (base <= 0.0 || detailStrength <= 0.0)
    {
        return base;
    }
    float h = saturate((posKm.z - bottomKm) / thicknessKm);
    float3 winded = posKm + float3(_cloudData.cloudWind.xy, 0.0);
    float3 drift = float3(0.7, 0.31, 0.49) * _cloudData.cloudWind.w;
    float3 detailNoise = _cloudNoise._cloudDetailNoise.SampleLevel(_samplers._linearRepeat, winded * CLOUD_DETAIL_SCALE + drift, 0).rgb;
    return CloudErodeShape(base, detailNoise, detailStrength, h);
}

// Sun optical depth (extinction integral) toward the sun from a slab position,
// via the cheap base density. Returns the optical depth (unitless).
float CloudSunOpticalDepth(float3 posKm, float3 dirToSun, float bottomKm, float thicknessKm,
    float coverage, float extinction)
{
    float od = 0.0;
    float prev = 0.0;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float offset = CLOUD_SUN_MARCH_OFFSETS[i];
        float3 p = posKm + dirToSun * offset;
        od += CloudBaseDensity(p, bottomKm, thicknessKm, coverage) * (offset - prev);
        prev = offset;
    }
    return od * extinction;
}

// Entry / exit distances (km) of the ray with the cloud slab; false when the
// ray never crosses it. tEntry is clamped to zero (camera inside the slab).
bool CloudSlabIntersect(float3 originKm, float3 dir, float bottomKm, float topKm,
    out float tEntry, out float tExit)
{
    tEntry = 0.0;
    tExit = 0.0;
    if (abs(dir.z) > 1e-4)
    {
        float tBottom = (bottomKm - originKm.z) / dir.z;
        float tTop = (topKm - originKm.z) / dir.z;
        tEntry = max(min(tBottom, tTop), 0.0);
        tExit = max(tBottom, tTop);
    }
    else
    {
        // Vertical ray: inside the slab the whole way, outside it never hits.
        if (originKm.z < bottomKm || originKm.z > topKm)
        {
            return false;
        }
        tExit = 1e6;
    }
    return tExit > tEntry;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    // Reconstruct the view ray and the opaque-scene distance for this pixel.
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = GetPixelDepth2D(_cloudData._gbufferDepth, int2(input.uv * _data.viewportSize.xy));
    float3 originKm = _data.cameraPosition.xyz / 1000.0;
    float3 dir;
    float sceneDistanceKm;
    if (IS_SKY_DEPTH(depth))
    {
        // Any depth along the ray yields the same perspective direction; 1.0 is
        // the near plane under reversed depth and numerically the safest pick.
        float4 farWorld = mul(_data.invViewProjection, float4(ndc, 1.0, 1.0));
        dir = normalize(farWorld.xyz / farWorld.w - _data.cameraPosition.xyz);
        sceneDistanceKm = 1e6;
    }
    else
    {
        float4 world = mul(_data.invViewProjection, float4(ndc, depth, 1.0));
        float3 worldPosition = world.xyz / world.w;
        dir = normalize(worldPosition - _data.cameraPosition.xyz);
        sceneDistanceKm = max(length(worldPosition - _data.cameraPosition.xyz) / 1000.0, 0.05);
    }

    float bottomKm = _cloudData.cloudParams.z;
    float thicknessKm = _cloudData.cloudParams.w;
    float topKm = bottomKm + thicknessKm;
    float coverage = _cloudData.cloudParams.x;
    float densityScale = _cloudData.cloudParams.y;
    float detailStrength = _cloudData.cloudParams2.x;
    float extinction = _cloudData.cloudParams2.y * densityScale;
    int maxSteps = (int)_cloudData.cloudParams2.w;

    float tEntry;
    float tExit;
    if (!CloudSlabIntersect(originKm, dir, bottomKm, topKm, tEntry, tExit))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // Clamp the march to the opaque scene and the maximum cloud range.
    tExit = min(min(tExit, sceneDistanceKm), tEntry + CLOUD_MAX_MARCH_KM);
    if (tExit <= tEntry)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // ── Per-pixel lighting terms (constant along the march) ──
    float3 dirToSun = normalize(-_data.sunDirection.xyz);

    // Sun energy: the atmosphere's solar radiance driver attenuated by an
    // analytic transmittance march evaluated at the slab's mid height. This
    // is the same physics the sky uses, so cloud and sky always agree — and
    // clouds keep receiving reddened sun after the ground light has faded
    // (the horizon dips ~2° from 3 km altitude).
    float3 sunEnergy = 0.0;
    if (dirToSun.z > -0.2)
    {
        float3 probeKm = float3(0.0, 0.0, 6360.0 + bottomKm + thicknessKm * 0.5);
        float2 sunDensity = AtmosphereSunDensityIntegral(probeKm, dirToSun, 6);
        if (sunDensity.x >= 0.0)
        {
            float3 tau = ATM_BETA_RAYLEIGH * _data.skyParams.x * sunDensity.x
                + (ATM_BETA_MIE * 1.1 * _data.skyParams.y) * sunDensity.y
                + ATM_BETA_OZONE * sunDensity.x;
            sunEnergy = exp(-tau) * _data.skyParams2.z * _cloudData.cloudLight.y;
        }
    }

    // Ambient: the CPU-filtered sky gradient, horizon-weighted at the slab
    // base (light arrives from the whole lower hemisphere there) and zenith at
    // the tops. It already contains the night floor, so clouds never go fully
    // black after dark.
    float3 ambientBottom = _data.skyHorizonColor.rgb * 0.65 + _data.skyZenithColor.rgb * 0.35;
    float3 ambientTop = _data.skyZenithColor.rgb * 0.85 + _data.skyHorizonColor.rgb * 0.15;
    float3 ambientEnergy = lerp(ambientBottom, ambientTop, 0.5) * _cloudData.cloudLight.x;

    float cosTheta = dot(dir, dirToSun);
    float phase = CloudPhaseDual(cosTheta);
    float dither = InterleavedGradientNoise(input.position.xy);

    // ── The march ──
    float3 scattering = 0.0;
    float transmittance = 1.0;
    // Jitter the start by up to one step: neighbouring pixels then sample the
    // slab on decorrelated sequences, so the large steps average into smooth
    // gradients (and the half-res upsample blurs the rest into fine grain)
    // instead of leaving coherent bands — visible as "slices" — in the clouds.
    float t = tEntry + dither * clamp(tEntry * CLOUD_STEP_SCALE, CLOUD_DT_MIN, CLOUD_DT_MAX);
    float dt = CLOUD_DT_MIN;
    [loop]
    for (int i = 0; i < maxSteps && t < tExit && transmittance > 0.015; i++)
    {
        dt = clamp(t * CLOUD_STEP_SCALE, CLOUD_DT_MIN, CLOUD_DT_MAX);

        // Sample the segment midpoint rather than its start: the integration
        // sees an effectively halved spacing, which softens the remaining
        // banding without paying for extra samples.
        float3 posKm = originKm + dir * (t + dt * 0.5);

        // Cheap gate: no base density means no detail either — skip ahead at
        // double length without paying for the erosion sample.
        float cheap = CloudBaseDensity(posKm, bottomKm, thicknessKm, coverage);
        if (cheap <= 0.02)
        {
            t += dt * 2.0;
            continue;
        }

        float density = CloudFullDensity(posKm, bottomKm, thicknessKm, coverage, detailStrength)
            * densityScale;
        if (density <= 0.01)
        {
            t += dt;
            continue;
        }

        // Sun extinction integral through the cloud toward the sun, widened
        // into a cheap multiple-scattering approximation with the powder term
        // (dark crevices, bright silver edges).
        float odSun = CloudSunOpticalDepth(posKm, dirToSun, bottomKm, thicknessKm,
            coverage, extinction);
        float beer = exp(-odSun);
        float beerMulti = exp(-odSun * 0.25);
        float powder = 1.0 - exp(-odSun * 2.0);
        float powderMulti = 1.0 - exp(-odSun * 0.5);
        float sunVisibility = lerp(beer * powder, beerMulti * powderMulti, 0.55);

        float h = saturate((posKm.z - bottomKm) / thicknessKm);
        float3 ambient = lerp(ambientBottom, ambientTop, h) * _cloudData.cloudLight.x;
        float3 lightEnergy = sunEnergy * phase * sunVisibility + ambient;

        // Energy-conserving integration between the transmittance at the
        // segment entry and exit (Frostbite-style analytic slice).
        float sigma = density * extinction;
        float sampleTransmittance = exp(-sigma * dt);
        float3 scatteredEnergy = lightEnergy * sigma * dt;
        scattering += transmittance
            * (scatteredEnergy - scatteredEnergy * sampleTransmittance) / max(sigma * dt, 1e-6);
        transmittance *= sampleTransmittance;

        t += dt;
    }

    float alpha = 1.0 - transmittance;
    if (alpha <= 1e-3)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // Aerial perspective: first dissolve the premultiplied energy and opacity
    // of the farthest banks so the maximum-range cutoff near the horizon stays
    // invisible, then blend what remains toward the horizon sky radiance (the
    // scattering stays a valid premultiplied color: horizon * alpha).
    float midT = (tEntry + tExit) * 0.5;
    float rangeFade = 1.0 - smoothstep(0.75, 1.0, midT / CLOUD_MAX_MARCH_KM);
    scattering *= rangeFade;
    alpha *= rangeFade;
    float hazeT = smoothstep(_cloudData.cloudLight.z, _cloudData.cloudLight.w, midT);
    scattering = lerp(scattering, _data.skyHorizonColor.rgb * alpha, hazeT);

    return float4(scattering, alpha);
}
