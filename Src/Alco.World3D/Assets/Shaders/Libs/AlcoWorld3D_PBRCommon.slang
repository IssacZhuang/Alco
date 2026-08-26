// file "AlcoWorld3D_PBRCommon.slang" - module "AlcoWorld3D_PBRCommon"
//
// Shared PBR definitions for the deferred lighting passes. Each resource
// group is one ParameterBlock — the compiler assigns each block its own
// descriptor set in declaration order; members resolve by bare field name
// and the engine binds everything by name. Lib declarations are public so
// importing modules see them.

#language slang 2025
module AlcoWorld3D_PBRCommon;

import AlcoRendering_Core;
import AlcoWorld3D_Atmosphere;

// Shared PBR functions used by both the deferred lighting pass and the
// forward glass pass. The C# side (PBRSceneEnvironment) writes members by
// name through UniformGraphicsBuffer (reflection-driven offsets), so this
// layout needs no CPU twin.

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
    public bool   shadowEnabled;        // sun shadow map bound and sampled
    public uint   numPointLights;       // active point lights in the StructuredBuffer
    public float  shadowMapSize;        // per-cascade texel count (the atlas is 2x this)
    public bool   sunDiscEnabled;       // procedural sky renders the sun disc
    public float4 cascadeSplits;        // radial end distance of each cascade; beyond w there is no shadow
    public float4 cascadeTexelSizes;    // world units per shadow texel of each cascade
    public float  shadowTightness;      // 0 = linear PCF average, 1 = full penumbra power curve
    public uint   viewportWidth;        // render target size in pixels
    public uint   viewportHeight;
    public bool   giEnabled;            // diffuse GI input wired and enabled
    public float  giDiffuseStrength;
    public float  giSpecularStrength;
    public float  sunDiscSize;          // cosine angular threshold, higher = smaller disc
    public float  sunDiscBrightness;    // HDR visual brightness independent of lighting intensity
    public bool   volumetricLightEnabled;
    public float  volumetricLightIntensity; // radiance multiplier (the gate above reads the enabled flag)
    public float  fogDensity;               // volumetric light scattering density
    public float  heightScaleHeight;        // height-falloff model (constant model ignores)
    public float  phaseG;                   // Henyey-Greenstein anisotropy
    public float  cloudShadowStrength;  // 0 = off; cloudShadow texture holds the coverage
    public float  cloudShadowPlaneAltitude; // meters, mid-slab
    public float  cloudShadowExtent;    // half extent of the coverage grid in meters
    public bool   cloudShadowEnabled;
};

// ── scene-environment blocks ───────────────────────────────────────────────
// Marks the uniform block mirroring PBRSceneEnvironment's shared per-frame
// lighting state (sun/sky/shadow/GI/volumetric constants). The engine finds
// the block by this marker, not by variable name — same convention as the
// material system's [MaterialParams], owned here (not in AlcoRendering_Core)
// because the scene environment is a World3D concern. Public so a composed
// environment library can import and reuse it.
[__AttributeUsage(_AttributeTargets.Var)]
public struct SceneEnvironmentParams {};

[SceneEnvironmentParams]
public ParameterBlock<PbrData> data;


// Point light storage buffer element.
public struct PointLightData {

    public float4 positionRange;    // xyz = world-space position, w = cutoff radius
    public float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)

};

public struct LightsAndShadow
{
    public RWStructuredBuffer<PointLightData> pointLights;
    public DepthTexture2D shadowMap;
};

public ParameterBlock<LightsAndShadow> lightsAndShadow;

public float distributionGGX(float ndotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = ndotH * ndotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-6);
}

public float geometrySchlickGGX(float ndotX, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    return ndotX / (ndotX * (1.0 - k) + k + 1e-6);
}

public float3 fresnelSchlick(float3 f0, float vdotH)
{
    return f0 + (1.0 - f0) * pow(1.0 - vdotH, 5.0);
}

// Returns (diffuse + specular) * ndotL for one light.
public float3 evaluatePBR(float3 n, float3 v, float3 l, float3 albedo, float metallic, float roughness)
{
    float3 h = normalize(v + l);
    float ndotL = max(dot(n, l), 0.0);
    float ndotV = max(dot(n, v), 0.0);
    float ndotH = max(dot(n, h), 0.0);
    float vdotH = max(dot(v, h), 0.0);

    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 f = fresnelSchlick(f0, vdotH);
    float d = distributionGGX(ndotH, roughness);
    float g = geometrySchlickGGX(ndotL, roughness) * geometrySchlickGGX(ndotV, roughness);

    float3 specular = d * g * f / (4.0 * ndotL * ndotV + 1e-6);
    float3 diffuse = (1.0 - f) * (1.0 - metallic) * albedo / PI;

    return (diffuse + specular) * ndotL;
}

// Pick the shadow cascade for a radial camera distance; -1 when beyond the last split.
public int selectCascade(float viewDistance)
{
    if (viewDistance < data.cascadeSplits.x) return 0;
    if (viewDistance < data.cascadeSplits.y) return 1;
    if (viewDistance < data.cascadeSplits.z) return 2;
    if (viewDistance < data.cascadeSplits.w) return 3;
    return -1;
}

// Interleaved gradient noise remains shared by the volumetric passes. Sun-shadow
// PCF itself is deterministic and no longer uses this value to rotate its taps.
public float interleavedGradientNoise(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

// Separable 5x5 PCF with the [1 3 4 3 1] kernel. Hardware bilinear comparison
// sampling combines each adjacent pair, reducing 25 comparisons to 9 while the
// sub-texel-dependent weights keep the filter stable as the receiver moves.
public float sampleShadowMapPCF5x5(
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
                * lightsAndShadow.shadowMap.SampleCmpLevelZero(
                    samplers.depthComparison, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 144.0);
}

// Separable 7x7 PCF with the [1 4 7 8 7 4 1] kernel. The same bilinear
// reduction evaluates the 49-texel footprint with 16 comparisons.
public float sampleShadowMapPCF7x7(
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
                * lightsAndShadow.shadowMap.SampleCmpLevelZero(
                    samplers.depthComparison, uv, compareDepth);
        }
    }

    return shadow * (1.0 / 1024.0);
}

// Deterministic optimized PCF against the shadow map cascade atlas. Cascade 0
// receives the wider 7x7 kernel for close contacts; the remaining cascades use
// 5x5 to keep their cost and distant penumbra width under control.
public float sampleShadowMap(float3 worldPosition, float3 n, float3 l, int cascade)
{
    float texelWorld = data.cascadeTexelSizes[cascade];
    float3 biasedWorld = worldPosition + n * texelWorld;

    float4 clip = mul(data.sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;

    float ndotL = saturate(dot(n, l));
    float bias = 0.0003 + 0.0015 * (1.0 - ndotL);
    float compareDepth = ndc.z - bias;

    float texelAtlas = 0.5 / data.shadowMapSize;
    float2 quadrantMin = quadrantOffset + texelAtlas * 0.5;
    float2 quadrantMax = quadrantOffset + 0.5 - texelAtlas * 0.5;

    float shadow;
    [branch]
    if (cascade == 0)
    {
        shadow = sampleShadowMapPCF7x7(
            shadowUV, compareDepth, texelAtlas, quadrantMin, quadrantMax);
    }
    else
    {
        shadow = sampleShadowMapPCF5x5(
            shadowUV, compareDepth, texelAtlas, quadrantMin, quadrantMax);
    }

    // Power-curve remap of the PCF average (per cascade, before cascade
    // blending). A plain few-tap average pulls the penumbra towards grey and
    // softens contacts; exponentiating keeps the umbra dark and shortens the
    // lit-to-shadow transition, buying a contact-hardening look for free.
    // Cascade 0 (contacts) gets the stronger curve. params2.z is the strength:
    // 0 = linear average (previous behavior), 1 = full effect.
    float exponent = cascade == 0 ? lerp(1.0, 3.0, data.shadowTightness) : lerp(1.0, 2.0, data.shadowTightness);
    return pow(shadow, exponent);
}

// Sun shadow with cascade blending.
public float sampleSunShadow(float3 worldPosition, float3 n, float3 l, float2 screenPos, float viewDistance, int cascade)
{
    if (cascade < 0)
    {
        return 1.0;
    }

    float shadow = sampleShadowMap(worldPosition, n, l, cascade);

    float splitEnd = data.cascadeSplits[cascade];
    float splitStart = cascade == 0 ? 0.0 : data.cascadeSplits[cascade - 1];
    float blendWidth = (splitEnd - splitStart) * 0.1;
    float blend = saturate((viewDistance - (splitEnd - blendWidth)) / blendWidth);
    if (blend > 0.0)
    {
        float nextShadow = cascade < 3 ? sampleShadowMap(worldPosition, n, l, cascade + 1) : 1.0;
        shadow = lerp(shadow, nextShadow, blend);
    }
    return shadow;
}

// Physically-based procedural sky: single-scattering atmosphere with sun disc and stars.
public float3 getSkyColor(float3 direction)
{
    float3 dirToSun = normalize(-data.sunDirection.xyz);
    float3 sky = atmosphereSkyRadiance(direction, dirToSun, data.skyParams, data.skyParams2, 16, 8);
    sky += atmosphereStars(direction, dirToSun, data.skyParams2.x);

    if (data.sunDiscEnabled)
    {
        float sunDot = dot(normalize(direction), dirToSun);
        float cosRadius = data.sunDiscSize;
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
        sky += data.sunColorAndIntensity.rgb * data.sunDiscBrightness * (disc + corona * 0.025) * horizon;
    }
    return sky;
}

// sRGB to linear rgb decoding.
public float3 decodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Geometric specular antialiasing (Karis).
public float geometricSpecularAA(float3 n, float roughness)
{
    float3 dNdx = ddx(n);
    float3 dNdy = ddy(n);
    float variance = (dot(dNdx, dNdx) + dot(dNdy, dNdy)) * 0.5;
    float kernelRoughness2 = min(2.0 * variance, 0.18);
    return saturate(roughness + sqrt(kernelRoughness2));
}

// Analytic approximation of the split-sum BRDF integral (Lazarov).
public float3 envBRDFApprox(float3 f0, float roughness, float ndotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndotV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

// Unit-albedo Lambert response for the CPU-filtered sky gradient.
public float3 evaluateDiffuseSky(float3 normal)
{
    float3 sideResponse = data.skyHorizonColor.rgb * 0.218505
        + data.skyZenithColor.rgb * 0.281495;
    float3 upResponse = data.skyHorizonColor.rgb * 0.230769
        + data.skyZenithColor.rgb * 0.769231;
    float upFacing = saturate(normal.z);
    float downFacing = saturate(-normal.z);
    return lerp(sideResponse, upResponse, upFacing) * (1.0 - downFacing);
}

// Evaluate point lights from the StructuredBuffer.
public float3 evaluatePointLights(float3 n, float3 v, float3 worldPosition,
    float3 albedo, float metallic, float roughness)
{
    float3 lo = 0.0;
    uint lightCount = data.numPointLights;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = lightsAndShadow.pointLights[i].positionRange;
        float4 colInt   = lightsAndShadow.pointLights[i].colorIntensity;
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

        float3 l = toLight / max(dist, 1e-6);
        lo += evaluatePBR(n, v, l, albedo, metallic, roughness)
            * colInt.rgb
            * colInt.w
            * attenuation;
    }
    return lo;
}
