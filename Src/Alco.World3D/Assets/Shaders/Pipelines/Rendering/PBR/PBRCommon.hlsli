#ifndef PBR_COMMON_HLSLI
#define PBR_COMMON_HLSLI

#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Atmosphere.hlsli"

// Shared PBR functions used by both DeferredLighting.hlsl and ForwardGlass.hlsl.
// The including shader MUST declare the _data cbuffer (DEFINE_UNIFORM(0, _data)),
// the PointLightData struct, the _pointLights storage buffer
// (DEFINE_STORAGE(1, PointLightData, _pointLights)), and the _shadowMap depth
// texture (DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap)) BEFORE including this file,
// so the globals referenced below are visible.

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 sunViewProjection[4];
    float4 cameraPosition;
    float4 sunDirection;         // normalized direction the sun light travels
    float4 sunColorAndIntensity; // rgb + intensity
    // Atmosphere parameters, see Shaders/Libs/Atmosphere.hlsli.
    float4 skyParams;            // x=rayleighScale y=mieScale z=miePhaseG w=exposure
    float4 skyParams2;           // x=starIntensity y=nightFloor z=sunRadianceScale w=ambientFloor
    float4 skyHorizonColor;      // azimuthally filtered physical sky at the horizon
    float4 skyZenithColor;       // filtered physical sky at the zenith
    float4 pbrParams;            // x=shadowEnabled y=numPointLights z=shadowMapSize w=sunDiscEnabled
    float4 cascadeSplits;        // radial end distance of each cascade; beyond w there is no shadow
    float4 cascadeTexelSizes;    // world units per shadow texel of each cascade
    float4 params2;              // x=cascadeDebugTint, y=shadowFactorView, z=shadowTightness (0=linear, 1=full penumbra power curve), w=aoDebugView
    float4 viewportSize;         // xy = render target size in pixels
    float4 params3;              // x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility)
    float4 params4;              // x=sunDiscSize(cosine threshold, higher=smaller) y=sunDiscBrightness z=1/GI trace width w=1/GI trace height (0 when GI is off)
    float4 vlParams;             // x=enabled(>0) y=fogDensity z=heightScaleHeight(constant model ignores) w=phaseG(Henyey-Greenstein anisotropy)
    float4 cloudShadow;          // x=strength y=shadow plane altitude m z=half extent m w=enabled(>0; _cloudShadow texture holds the coverage)
};

// Point light storage buffer element.
struct PointLightData
{
    float4 positionRange;    // xyz = world-space position, w = cutoff radius
    float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)
};

DEFINE_STORAGE(1, PointLightData, _pointLights);

DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);

float DistributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-6);
}

float GeometrySchlickGGX(float NdotX, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    return NdotX / (NdotX * (1.0 - k) + k + 1e-6);
}

float3 FresnelSchlick(float3 F0, float VdotH)
{
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
}

// Returns (diffuse + specular) * NdotL for one light.
float3 EvaluatePBR(float3 N, float3 V, float3 L, float3 albedo, float metallic, float roughness)
{
    float3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);

    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 F = FresnelSchlick(F0, VdotH);
    float D = DistributionGGX(NdotH, roughness);
    float G = GeometrySchlickGGX(NdotL, roughness) * GeometrySchlickGGX(NdotV, roughness);

    float3 specular = D * G * F / (4.0 * NdotL * NdotV + 1e-6);
    float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;

    return (diffuse + specular) * NdotL;
}

// Pick the shadow cascade for a radial camera distance; -1 when beyond the last split.
int SelectCascade(float viewDistance)
{
    if (viewDistance < cascadeSplits.x) return 0;
    if (viewDistance < cascadeSplits.y) return 1;
    if (viewDistance < cascadeSplits.z) return 2;
    if (viewDistance < cascadeSplits.w) return 3;
    return -1;
}

// Interleaved gradient noise remains shared by the volumetric passes. Sun-shadow
// PCF itself is deterministic and no longer uses this value to rotate its taps.
float InterleavedGradientNoise(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

// Separable 5x5 PCF with the [1 3 4 3 1] kernel. Hardware bilinear comparison
// sampling combines each adjacent pair, reducing 25 comparisons to 9 while the
// sub-texel-dependent weights keep the filter stable as the receiver moves.
float SampleShadowMapPCF5x5(
    float2 shadowUV,
    float compareDepth,
    float texelAtlas,
    float2 quadrantMin,
    float2 quadrantMax)
{
    float2 texelPosition = shadowUV / texelAtlas;
    float2 centerTexel = floor(texelPosition + 0.5);
    float2 st = texelPosition + 0.5 - centerTexel;
    float2 baseUV = (centerTexel - 0.5) * texelAtlas;

    float uWeights[3] = { 4.0 - 3.0 * st.x, 7.0, 1.0 + 3.0 * st.x };
    float vWeights[3] = { 4.0 - 3.0 * st.y, 7.0, 1.0 + 3.0 * st.y };
    float uOffsets[3] = {
        (3.0 - 2.0 * st.x) / uWeights[0] - 2.0,
        (3.0 + st.x) / uWeights[1],
        st.x / uWeights[2] + 2.0
    };
    float vOffsets[3] = {
        (3.0 - 2.0 * st.y) / vWeights[0] - 2.0,
        (3.0 + st.y) / vWeights[1],
        st.y / vWeights[2] + 2.0
    };

    float shadow = 0.0;
    [unroll]
    for (int y = 0; y < 3; y++)
    {
        [unroll]
        for (int x = 0; x < 3; x++)
        {
            float2 uv = clamp(
                baseUV + float2(uOffsets[x], vOffsets[y]) * texelAtlas,
                quadrantMin,
                quadrantMax);
            shadow += uWeights[x] * vWeights[y]
                * SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 144.0);
}

// Separable 7x7 PCF with the [1 4 7 8 7 4 1] kernel. The same bilinear
// reduction evaluates the 49-texel footprint with 16 comparisons.
float SampleShadowMapPCF7x7(
    float2 shadowUV,
    float compareDepth,
    float texelAtlas,
    float2 quadrantMin,
    float2 quadrantMax)
{
    float2 texelPosition = shadowUV / texelAtlas;
    float2 centerTexel = floor(texelPosition + 0.5);
    float2 st = texelPosition + 0.5 - centerTexel;
    float2 baseUV = (centerTexel - 0.5) * texelAtlas;

    float uWeights[4] = {
        5.0 - 4.0 * st.x,
        15.0 - 4.0 * st.x,
        11.0 + 4.0 * st.x,
        1.0 + 4.0 * st.x
    };
    float vWeights[4] = {
        5.0 - 4.0 * st.y,
        15.0 - 4.0 * st.y,
        11.0 + 4.0 * st.y,
        1.0 + 4.0 * st.y
    };
    float uOffsets[4] = {
        (4.0 - 3.0 * st.x) / uWeights[0] - 3.0,
        (8.0 - st.x) / uWeights[1] - 1.0,
        (4.0 + 3.0 * st.x) / uWeights[2] + 1.0,
        st.x / uWeights[3] + 3.0
    };
    float vOffsets[4] = {
        (4.0 - 3.0 * st.y) / vWeights[0] - 3.0,
        (8.0 - st.y) / vWeights[1] - 1.0,
        (4.0 + 3.0 * st.y) / vWeights[2] + 1.0,
        st.y / vWeights[3] + 3.0
    };

    float shadow = 0.0;
    [unroll]
    for (int y = 0; y < 4; y++)
    {
        [unroll]
        for (int x = 0; x < 4; x++)
        {
            float2 uv = clamp(
                baseUV + float2(uOffsets[x], vOffsets[y]) * texelAtlas,
                quadrantMin,
                quadrantMax);
            shadow += uWeights[x] * vWeights[y]
                * SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 1024.0);
}

// Deterministic optimized PCF against the shadow map cascade atlas. Cascade 0
// receives the wider 7x7 kernel for close contacts; the remaining cascades use
// 5x5 to keep their cost and distant penumbra width under control.
float SampleShadowMap(float3 worldPosition, float3 N, float3 L, int cascade)
{
    float texelWorld = cascadeTexelSizes[cascade];
    float3 biasedWorld = worldPosition + N * texelWorld;

    float4 clip = mul(sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;

    float NdotL = saturate(dot(N, L));
    float bias = 0.0003 + 0.0015 * (1.0 - NdotL);
    float compareDepth = ndc.z - bias;

    float texelAtlas = 0.5 / pbrParams.z;
    float2 quadrantMin = quadrantOffset + texelAtlas * 0.5;
    float2 quadrantMax = quadrantOffset + 0.5 - texelAtlas * 0.5;

    float shadow;
    [branch]
    if (cascade == 0)
    {
        shadow = SampleShadowMapPCF7x7(
            shadowUV, compareDepth, texelAtlas, quadrantMin, quadrantMax);
    }
    else
    {
        shadow = SampleShadowMapPCF5x5(
            shadowUV, compareDepth, texelAtlas, quadrantMin, quadrantMax);
    }

    // Power-curve remap of the PCF average (per cascade, before cascade
    // blending). A plain few-tap average pulls the penumbra towards grey and
    // softens contacts; exponentiating keeps the umbra dark and shortens the
    // lit-to-shadow transition, buying a contact-hardening look for free.
    // Cascade 0 (contacts) gets the stronger curve. params2.z is the strength:
    // 0 = linear average (previous behavior), 1 = full effect.
    float exponent = cascade == 0 ? lerp(1.0, 3.0, params2.z) : lerp(1.0, 2.0, params2.z);
    return pow(shadow, exponent);
}

// Sun shadow with cascade blending.
float SampleSunShadow(float3 worldPosition, float3 N, float3 L, float2 screenPos, float viewDistance, int cascade)
{
    if (cascade < 0)
    {
        return 1.0;
    }

    float shadow = SampleShadowMap(worldPosition, N, L, cascade);

    float splitEnd = cascadeSplits[cascade];
    float splitStart = cascade == 0 ? 0.0 : cascadeSplits[cascade - 1];
    float blendWidth = (splitEnd - splitStart) * 0.1;
    float blend = saturate((viewDistance - (splitEnd - blendWidth)) / blendWidth);
    if (blend > 0.0)
    {
        float nextShadow = cascade < 3 ? SampleShadowMap(worldPosition, N, L, cascade + 1) : 1.0;
        shadow = lerp(shadow, nextShadow, blend);
    }
    return shadow;
}

// Physically-based procedural sky: single-scattering atmosphere with sun disc and stars.
float3 GetSkyColor(float3 direction)
{
    float3 dirToSun = normalize(-sunDirection.xyz);
#if defined(__SLANG__)
    // Slang's direct SPIR-V representation of the nested 16x8 atmosphere
    // march can time out current Vulkan drivers. The CPU already evaluates
    // the same physical model into horizon/zenith radiance every frame for
    // ambient lighting, so use that stable low-frequency representation for
    // the Slang path while retaining the procedural stars and sun disc below.
    float elevation = saturate(direction.z);
    float skyBlend = pow(elevation, 0.35);
    float3 sky = lerp(skyHorizonColor.rgb, skyZenithColor.rgb, skyBlend);
    float groundBlend = smoothstep(0.0, 1.0, saturate(-direction.z / 0.27));
    sky = lerp(sky, skyHorizonColor.rgb * 0.55, groundBlend);
#else
    float3 sky = AtmosphereSkyRadiance(direction, dirToSun, skyParams, skyParams2, 16, 8);
#endif
    sky += AtmosphereStars(direction, dirToSun, skyParams2.x);

    if (pbrParams.w > 0.5)
    {
        float sunDot = dot(normalize(direction), dirToSun);
        float cosRadius = params4.x;
        float edgeWidth = max((1.0 - cosRadius) * 0.2, 1e-7);
        float disc = smoothstep(cosRadius - edgeWidth, cosRadius, sunDot);
        float coronaRange = (1.0 - cosRadius) * 3.5;
        float corona = smoothstep(1.0 - coronaRange, cosRadius, sunDot) - disc;
        // Fade the disc out as the sun sets (0 at 0.1 below the horizon,
        // smoothstepped to 1 at the horizon) so it never clips through the
        // ground haze (Complementary Unbound's GetHorizonFactor).
        float horizon = saturate((dirToSun.z + 0.1) * 10.0);
        horizon *= horizon;
        horizon = horizon * horizon * (3.0 - 2.0 * horizon);
        // Keep the disc clearly separated from the atmospheric glare.  The old
        // 8% corona was already 1.44 HDR at the default disc brightness, so it
        // clipped together with the Mie lobe before bloom was even applied.
        sky += sunColorAndIntensity.rgb * params4.y * (disc + corona * 0.025) * horizon;
    }
    return sky;
}

// sRGB to linear RGB decoding.
float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Geometric specular antialiasing (Karis).
float GeometricSpecularAA(float3 N, float roughness)
{
    float3 dNdx = ddx(N);
    float3 dNdy = ddy(N);
    float variance = (dot(dNdx, dNdx) + dot(dNdy, dNdy)) * 0.5;
    float kernelRoughness2 = min(2.0 * variance, 0.18);
    return saturate(roughness + sqrt(kernelRoughness2));
}

// Analytic approximation of the split-sum BRDF integral (Lazarov).
float3 EnvBRDFApprox(float3 F0, float roughness, float NdotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return F0 * AB.x + AB.y;
}

// Unit-albedo Lambert response for the CPU-filtered sky gradient.
float3 EvaluateDiffuseSky(float3 normal)
{
    float3 sideResponse = skyHorizonColor.rgb * 0.218505
        + skyZenithColor.rgb * 0.281495;
    float3 upResponse = skyHorizonColor.rgb * 0.230769
        + skyZenithColor.rgb * 0.769231;
    float upFacing = saturate(normal.z);
    float downFacing = saturate(-normal.z);
    return lerp(sideResponse, upResponse, upFacing) * (1.0 - downFacing);
}

// Evaluate point lights from the StructuredBuffer. The including shader must
// have declared _pointLights via DEFINE_STORAGE.
float3 EvaluatePointLights(float3 N, float3 V, float3 worldPosition,
    float3 albedo, float metallic, float roughness)
{
    float3 Lo = 0.0;
    uint lightCount = (uint)pbrParams.y;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = _pointLights[i].positionRange;
        float4 colInt   = _pointLights[i].colorIntensity;
        if (colInt.w <= 0.0)
        {
            continue;
        }

        float3 toLight = posRange.xyz - worldPosition;
        float dist = length(toLight);
        if (posRange.w > 0.0 && dist > posRange.w)
        {
            continue;
        }

        float attenuation = 1.0 / (dist * dist + 1.0);
        if (posRange.w > 0.0)
        {
            float fallOff = saturate(1.0 - dist / posRange.w);
            attenuation *= fallOff * fallOff;
        }

        float3 L = toLight / max(dist, 1e-6);
        Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
            * colInt.rgb
            * colInt.w
            * attenuation;
    }
    return Lo;
}

#endif // PBR_COMMON_HLSLI
