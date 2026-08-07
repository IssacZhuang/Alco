#include "Shaders/Libs/Atmosphere.hlsli"

// Shared PBR functions used by both DeferredLighting.hlsl and ForwardGlass.hlsl.
// The including shader MUST declare the _data cbuffer (DEFINE_UNIFORM(0, _data)),
// the PointLightData struct, the _pointLights storage buffer
// (DEFINE_STORAGE(1, PointLightData, _pointLights)), and the _shadowMap depth
// texture (DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap)) BEFORE including this file,
// so the globals referenced below are visible.

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

// Interleaved Gradient Noise (Jorge Jimenez, "Next Generation Post-Processing
// in Call of Duty: Advanced Warfare", 2014).
float InterleavedGradientNoise(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

static const float2 poissonDisk[4] = {
    float2(-0.94201624, -0.39906216),
    float2( 0.94558609, -0.76890725),
    float2(-0.09418410, -0.92938870),
    float2( 0.34495938,  0.29387733),
};

// 4-tap rotated Poisson disk PCF against the shadow map cascade atlas.
float SampleShadowMap(float3 worldPosition, float3 N, float3 L, float2 screenPos, int cascade)
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

    float angle = InterleavedGradientNoise(screenPos) * 6.2831853;
    float s, c;
    sincos(angle, s, c);
    float2x2 rotation = float2x2(c, -s, s, c);

    static const float spread = 1.5;
    float shadow = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 offset = mul(rotation, poissonDisk[i]) * texelAtlas * spread;
        float2 uv = clamp(shadowUV + offset, quadrantMin, quadrantMax);
        shadow += SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, uv, compareDepth);
    }
    return shadow * 0.25;
}

// Sun shadow with cascade blending.
float SampleSunShadow(float3 worldPosition, float3 N, float3 L, float2 screenPos, float viewDistance, int cascade)
{
    if (cascade < 0)
    {
        return 1.0;
    }

    float shadow = SampleShadowMap(worldPosition, N, L, screenPos, cascade);

    float splitEnd = cascadeSplits[cascade];
    float splitStart = cascade == 0 ? 0.0 : cascadeSplits[cascade - 1];
    float blendWidth = (splitEnd - splitStart) * 0.1;
    float blend = saturate((viewDistance - (splitEnd - blendWidth)) / blendWidth);
    if (blend > 0.0)
    {
        float nextShadow = cascade < 3 ? SampleShadowMap(worldPosition, N, L, screenPos, cascade + 1) : 1.0;
        shadow = lerp(shadow, nextShadow, blend);
    }
    return shadow;
}

// Physically-based procedural sky: single-scattering atmosphere with sun disc and stars.
float3 GetSkyColor(float3 direction)
{
    float3 dirToSun = normalize(-sunDirection.xyz);
    float3 sky = AtmosphereSkyRadiance(direction, dirToSun, skyParams, skyParams2, 16, 8);
    sky += AtmosphereStars(direction, dirToSun, skyParams2.x);

    if (pbrParams.w > 0.5)
    {
        float sunDot = dot(normalize(direction), dirToSun);
        float cosRadius = params4.x;
        float edgeWidth = max((1.0 - cosRadius) * 0.2, 1e-7);
        float disc = smoothstep(cosRadius - edgeWidth, cosRadius, sunDot);
        float coronaRange = (1.0 - cosRadius) * 3.5;
        float corona = smoothstep(1.0 - coronaRange, cosRadius, sunDot) - disc;
        sky += sunColorAndIntensity.rgb * params4.y * (disc + corona * 0.08);
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
