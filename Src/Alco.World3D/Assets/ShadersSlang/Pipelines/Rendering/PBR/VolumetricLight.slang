// file "VolumetricLight.slang" - module "volumetric_light"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricLight.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module volumetric_light;

import alco_rendering_core;
import alco_world3d_reversed_depth;
import alco_world3d_pbr_common;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Volumetric light (god rays / atmospheric scattering) pass for the deferred
// PBR pipeline. A full-screen pass rendered after deferred lighting; the
// accumulated in-scattered radiance is additively blended into the HDR target.
//
// Algorithm: ray march along the view ray; at each sample point project into
// the shadow map to test sun visibility, apply a Henyey-Greenstein phase
// function, and accumulate in-scattered radiance with Beer-Lambert extinction.
// Single-tap shadow sampling per step (hardware 2x2 PCF) — the multiple march
// steps themselves provide softening that makes multi-tap PCF unnecessary.
//
// Density model: exponential height falloff (denser near ground, thinner
// at altitude). Scale height H is controlled via vlParams.z.
//
// Quality (sample count) is set via VL_SAMPLES define (default 24).

#ifndef VL_SAMPLES
    #define VL_SAMPLES 24
#endif

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

// G-buffer depth (for reconstructing world position and determining ray length).
[[vk::binding(0, 1)]] DepthTexture2D _gbufferDepth;


[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// Henyey-Greenstein phase function for the Mie-like forward scattering of the
// sun through the atmosphere. g in [-1, 1]: 0 = isotropic, >0 = forward.
float HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * PI * max(pow(denom, 1.5), 1e-7));
}

// Single-tap shadow visibility for a world-space point. Uses the same atlas
// layout and bias as SampleShadowMap in PBRCommon.hlsli but does only one
// hardware-PCF comparison (the ray march provides spatial softening).
float SampleShadowVL(float3 worldPosition, int cascade, float2 screenPos)
{
    if (cascade < 0)
    {
        return 1.0;
    }

    float4 clip = mul(sunViewProjection[cascade], float4(worldPosition, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;

    float compareDepth = ndc.z - 0.0008;
    return SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, shadowUV, compareDepth);
}

// Fog density at a world-space point: exponential falloff relative to the
// camera height. This keeps volumetric light visible regardless of terrain
// elevation — the density profile follows the camera rather than the world
// origin. sigma = sigma0 * exp(-|dh| / H) where dh = samplePos.z - cameraPos.z.
float VolumetricDensity(float3 worldPos)
{
    float heightAboveCamera = worldPos.z - cameraPosition.z;
    float scaleHeight = vlParams.z;
    return vlParams.y * exp(-abs(heightAboveCamera) / max(scaleHeight, 1e-4));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    // Early-out when volumetric light is disabled.
    if (vlParams.x <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // Reconstruct world position and view ray. The clamp keeps the homogeneous
    // w finite for sky pixels (depth 0 under reversed infinite depth); the ray
    // direction is unchanged since any depth along the ray yields it.
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    int2 pixelCoord = int2(input.uv * viewportSize.xy);
    float depth = GetPixelDepth2D(_gbufferDepth, pixelCoord);
    float4 world = mul(invViewProjection, float4(ndc, max(depth, PBR_SKY_DEPTH_EPSILON), 1.0));
    float3 worldPosition = world.xyz / world.w;

    float3 rayOrigin = cameraPosition.xyz;
    float3 rayDir = normalize(worldPosition - rayOrigin);

    // Determine ray march bounds.
    // Beyond the last cascade there is no shadow information, so marching
    // farther would add unshadowed (flat) radiance — clamp to the last split.
    float maxDistance = cascadeSplits.w;
    bool isSky = IS_SKY_DEPTH(depth);
    float sceneDistance = isSky ? maxDistance : length(worldPosition - rayOrigin);
    float rayEnd = min(sceneDistance, maxDistance);

    if (rayEnd < 1.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // Phase function.
    float3 dirToSun = normalize(-sunDirection.xyz);
    float cosTheta = dot(rayDir, dirToSun);
    float phaseG = vlParams.w;
    float phase = HenyeyGreenstein(cosTheta, phaseG);

    // Per-pixel dither to break up banding (IGN — deterministic per pixel).
    float dither = InterleavedGradientNoise(input.position.xy);

    // Ray march.
    float3 sunColor = sunColorAndIntensity.rgb * sunColorAndIntensity.w;
    float3 radiance = 0.0;
    float transmittance = 1.0;
    float prevDist = 0.0;

    [loop]
    for (int i = 0; i < VL_SAMPLES; i++)
    {
        // Non-linear sample distribution: concentrate samples near the camera.
        float t = ((float)i + 0.5 + dither) / (float)VL_SAMPLES;
        float sampleDist = pow(t, 2.0) * rayEnd;

        float dt = sampleDist - prevDist;
        if (dt <= 0.0)
        {
            continue;
        }

        float3 samplePos = rayOrigin + rayDir * sampleDist;

        // Shadow visibility at this point.
        int cascade = SelectCascade(sampleDist);
        float visibility = SampleShadowVL(samplePos, cascade, input.position.xy);

        // Participating-medium density and extinction.
        float sigma = VolumetricDensity(samplePos);
        float opticalDepth = sigma * dt;
        float sampleTransmittance = exp(-opticalDepth);

        // In-scattered radiance: sunColor * phase * visibility * sigma * dt,
        // attenuated by the transmittance from the camera to this sample.
        radiance += sunColor * phase * visibility * sigma * transmittance * dt;

        transmittance *= sampleTransmittance;
        prevDist = sampleDist;

        // Early-out when the medium becomes opaque.
        if (transmittance < 0.01)
        {
            break;
        }
    }

    // Apply intensity multiplier.
    radiance *= vlParams.x;

    // Output: RGB holds the additively blended radiance; alpha = 1 so the
    // blend state (SrcAlpha, One) adds radiance directly to the target.
    return float4(radiance, 1.0);
}
