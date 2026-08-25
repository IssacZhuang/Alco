// file "alco-world3d-pbr-common.slang" - module "alco_world3d_pbr_common"
//
// Shared PBR definitions for the deferred lighting passes. Each resource
// group is one ParameterBlock — the compiler assigns each block its own
// descriptor set in declaration order; members resolve by bare field name
// and the engine binds everything by name. Lib declarations are public so
// importing modules see them.

#language slang 2025
module alco_world3d_pbr_common;

import alco_rendering_core;
import alco_world3d_atmosphere;

// Shared PBR functions used by both the deferred lighting pass and the
// forward glass pass. The cbuffer layout must match the C# side's frame
// data layout exactly.

public struct PbrData
{
    public float4x4 invViewProjection;
    public float4x4 sunViewProjection[4];
    public float4 cameraPosition;
    public float4 sunDirection;         // normalized direction the sun light travels
    public float4 sunColorAndIntensity; // rgb + intensity
    // Atmosphere parameters, see alco-world3d-atmosphere.
    public float4 skyParams;            // x=rayleighScale y=mieScale z=miePhaseG w=exposure
    public float4 skyParams2;           // x=starIntensity y=nightFloor z=sunRadianceScale w=ambientFloor
    public float4 skyHorizonColor;      // azimuthally filtered physical sky at the horizon
    public float4 skyZenithColor;       // filtered physical sky at the zenith
    public float4 pbrParams;            // x=shadowEnabled y=numPointLights z=shadowMapSize w=sunDiscEnabled
    public float4 cascadeSplits;        // radial end distance of each cascade; beyond w there is no shadow
    public float4 cascadeTexelSizes;    // world units per shadow texel of each cascade
    public float4 params2;              // x=shadowTightness (0=linear, 1=full penumbra power curve); y/z/w unused (debug views are the DebugView specialization axis)
    public float4 viewportSize;         // xy = render target size in pixels
    public float4 params3;              // x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength; w unused (GI debug views are the DebugView specialization axis)
    public float4 params4;              // x=sunDiscSize(cosine threshold, higher=smaller) y=sunDiscBrightness z=1/GI trace width w=1/GI trace height (0 when GI is off)
    public float4 vlParams;             // x=enabled(>0) y=fogDensity z=heightScaleHeight(constant model ignores) w=phaseG(Henyey-Greenstein anisotropy)
    public float4 cloudShadow;          // x=strength y=shadow plane altitude m z=half extent m w=enabled(>0; _cloudShadow texture holds the coverage)
};

public ParameterBlock<PbrData> _data;


// Point light storage buffer element.
public struct PointLightData {

    public float4 positionRange;    // xyz = world-space position, w = cutoff radius
    public float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)

};

public struct LightsAndShadow
{
    public RWStructuredBuffer<PointLightData> _pointLights;
    public DepthTexture2D _shadowMap;
    public SamplerComparisonState _shadowMapSampler;
};

public ParameterBlock<LightsAndShadow> _lightsAndShadow;

public float DistributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-6);
}

public float GeometrySchlickGGX(float NdotX, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    return NdotX / (NdotX * (1.0 - k) + k + 1e-6);
}

public float3 FresnelSchlick(float3 F0, float VdotH)
{
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
}

// Returns (diffuse + specular) * NdotL for one light.
public float3 EvaluatePBR(float3 N, float3 V, float3 L, float3 albedo, float metallic, float roughness)
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
public int SelectCascade(float viewDistance)
{
    if (viewDistance < _data.cascadeSplits.x) return 0;
    if (viewDistance < _data.cascadeSplits.y) return 1;
    if (viewDistance < _data.cascadeSplits.z) return 2;
    if (viewDistance < _data.cascadeSplits.w) return 3;
    return -1;
}

// Interleaved gradient noise remains shared by the volumetric passes. Sun-shadow
// PCF itself is deterministic and no longer uses this value to rotate its taps.
public float InterleavedGradientNoise(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

// Separable 5x5 PCF with the [1 3 4 3 1] kernel. Hardware bilinear comparison
// sampling combines each adjacent pair, reducing 25 comparisons to 9 while the
// sub-texel-dependent weights keep the filter stable as the receiver moves.
public float SampleShadowMapPCF5x5(
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
                * _lightsAndShadow._shadowMap.SampleCmpLevelZero(
                    _lightsAndShadow._shadowMapSampler, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 144.0);
}

// Separable 7x7 PCF with the [1 4 7 8 7 4 1] kernel. The same bilinear
// reduction evaluates the 49-texel footprint with 16 comparisons.
public float SampleShadowMapPCF7x7(
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
                * _lightsAndShadow._shadowMap.SampleCmpLevelZero(
                    _lightsAndShadow._shadowMapSampler, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 1024.0);
}

// Deterministic optimized PCF against the shadow map cascade atlas. Cascade 0
// receives the wider 7x7 kernel for close contacts; the remaining cascades use
// 5x5 to keep their cost and distant penumbra width under control.
public float SampleShadowMap(float3 worldPosition, float3 N, float3 L, int cascade)
{
    float texelWorld = _data.cascadeTexelSizes[cascade];
    float3 biasedWorld = worldPosition + N * texelWorld;

    float4 clip = mul(_data.sunViewProjection[cascade], float4(biasedWorld, 1.0));
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

    float texelAtlas = 0.5 / _data.pbrParams.z;
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
    float exponent = cascade == 0 ? lerp(1.0, 3.0, _data.params2.z) : lerp(1.0, 2.0, _data.params2.z);
    return pow(shadow, exponent);
}

// Sun shadow with cascade blending.
public float SampleSunShadow(float3 worldPosition, float3 N, float3 L, float2 screenPos, float viewDistance, int cascade)
{
    if (cascade < 0)
    {
        return 1.0;
    }

    float shadow = SampleShadowMap(worldPosition, N, L, cascade);

    float splitEnd = _data.cascadeSplits[cascade];
    float splitStart = cascade == 0 ? 0.0 : _data.cascadeSplits[cascade - 1];
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
public float3 GetSkyColor(float3 direction)
{
    float3 dirToSun = normalize(-_data.sunDirection.xyz);
    float3 sky = AtmosphereSkyRadiance(direction, dirToSun, _data.skyParams, _data.skyParams2, 16, 8);
    sky += AtmosphereStars(direction, dirToSun, _data.skyParams2.x);

    if (_data.pbrParams.w > 0.5)
    {
        float sunDot = dot(normalize(direction), dirToSun);
        float cosRadius = _data.params4.x;
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
        sky += _data.sunColorAndIntensity.rgb * _data.params4.y * (disc + corona * 0.025) * horizon;
    }
    return sky;
}

// sRGB to linear RGB decoding.
public float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Geometric specular antialiasing (Karis).
public float GeometricSpecularAA(float3 N, float roughness)
{
    float3 dNdx = ddx(N);
    float3 dNdy = ddy(N);
    float variance = (dot(dNdx, dNdx) + dot(dNdy, dNdy)) * 0.5;
    float kernelRoughness2 = min(2.0 * variance, 0.18);
    return saturate(roughness + sqrt(kernelRoughness2));
}

// Analytic approximation of the split-sum BRDF integral (Lazarov).
public float3 EnvBRDFApprox(float3 F0, float roughness, float NdotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return F0 * AB.x + AB.y;
}

// Unit-albedo Lambert response for the CPU-filtered sky gradient.
public float3 EvaluateDiffuseSky(float3 normal)
{
    float3 sideResponse = _data.skyHorizonColor.rgb * 0.218505
        + _data.skyZenithColor.rgb * 0.281495;
    float3 upResponse = _data.skyHorizonColor.rgb * 0.230769
        + _data.skyZenithColor.rgb * 0.769231;
    float upFacing = saturate(normal.z);
    float downFacing = saturate(-normal.z);
    return lerp(sideResponse, upResponse, upFacing) * (1.0 - downFacing);
}

// Evaluate point lights from the StructuredBuffer.
public float3 EvaluatePointLights(float3 N, float3 V, float3 worldPosition,
    float3 albedo, float metallic, float roughness)
{
    float3 Lo = 0.0;
    uint lightCount = (uint)_data.pbrParams.y;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = _lightsAndShadow._pointLights[i].positionRange;
        float4 colInt   = _lightsAndShadow._pointLights[i].colorIntensity;
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
