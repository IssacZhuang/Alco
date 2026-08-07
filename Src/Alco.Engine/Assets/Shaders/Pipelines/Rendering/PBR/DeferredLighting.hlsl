#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Atmosphere.hlsli"

// Deferred lighting pass shader for the PBR pipeline.
// Samples the G-buffer, evaluates a GGX PBR BRDF with a directional sun
// (shadow mapped, hardware PCF), dynamic point lights from a StructuredBuffer,
// an ambient term (sky/probe baseline modulated by voxel visibility plus
// traced bounce light) and a physically-based procedural sky (single
// scattering atmosphere plus sun disc and stars) for empty pixels.

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

// Bind groups: set 0 is the per-frame lighting constants; set 1 packs every
// per-pass input of the lighting pass (G-buffer, shadow map, GI atlas) at
// distinct bindings, so the pass needs two of the eight available sets.
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
    float4 params2;              // x=cascadeDebugTint, y=shadowFactorView, z=unused, w=aoDebugView
    float4 viewportSize;         // xy = render target size in pixels
    float4 params3;              // x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility)
    float4 params4;              // x=sunDiscSize(cosine threshold, higher=smaller) y=sunDiscBrightness z=1/GI trace width w=1/GI trace height (0 when GI is off)
};

DEFINE_TEX2D_SAMPLE(1, _albedo);

// Point lights stored in a StructuredBuffer (not cbuffer) so the count is
// bounded by GPU memory, not by cbuffer size limits. xyz = position, w = range.
struct PointLightData
{
    float4 positionRange;    // xyz = world-space position, w = cutoff radius
    float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)
};
DEFINE_STORAGE(1, PointLightData, _pointLights);

DEFINE_TEX2D_SAMPLE(1, _normal);
DEFINE_TEX2D_SAMPLE(1, _mrAO);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);
DEFINE_TEX2D_SAMPLE(1, _emissive);
// Indirect GI textures from the GI render plugin (full-resolution):
// diffuse irradiance with ALD directional modulation pre-applied, and
// specular radiance. White/black fallbacks are bound by the pipeline when
// no GI plugin is active.
DEFINE_TEX2D_SAMPLE(1, _giDiffuse);
DEFINE_TEX2D_SAMPLE(1, _giSpecular);
// Screen-space ambient occlusion from an AO render plugin (full-resolution,
// white = unoccluded). The pipeline binds white when no AO plugin is active.
DEFINE_TEX2D_SAMPLE(1, _aoTexture);

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

float3 ReconstructWorldPosition(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(input.uv * viewportSize.xy));
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// View-linear depth of the current pixel from the homogeneous w produced by
// the inverse view-projection (fourth matrix row only). Matches the metric
// stored in the GI atlas layer depths.
float ReconstructLinearDepth(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(input.uv * viewportSize.xy));
    float reciprocalClipW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

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
// in Call of Duty: Advanced Warfare", 2014) — cheap deterministic hash that is
// temporally stable per-pixel, ideal for per-pixel rotation of the Poisson disk.
float InterleavedGradientNoise(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

// 4-tap Poisson disk (first four taps of the 16-tap set from GPU Gems Ch. 12),
// rotated per-pixel by IGN so neighbouring pixels see different arrangements,
// dithering the regular-grid aliasing of a fixed kernel.
static const float2 poissonDisk[4] = {
    float2(-0.94201624, -0.39906216),
    float2( 0.94558609, -0.76890725),
    float2(-0.09418410, -0.92938870),
    float2( 0.34495938,  0.29387733),
};

// 4-tap rotated Poisson disk PCF against the shadow map cascade atlas.
// Each SampleCmpLevelZero tap compares (ndc.z - bias) <= texelDepth and, with the
// linear comparison sampler, already blends the four nearest texels — so 4 taps
// effectively cover 16 texels, matching the old 9-tap 3×3 grid at half the cost.
float SampleShadowMap(float3 worldPosition, float3 N, float3 L, float2 screenPos, int cascade)
{
    // Normal offset bias: push the receiver along its normal by one world texel
    // of this cascade, which removes most acne without peter-panning.
    float texelWorld = cascadeTexelSizes[cascade];
    float3 biasedWorld = worldPosition + N * texelWorld;

    float4 clip = mul(sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    // Map the base UV into the cascade's atlas quadrant (cascade c occupies
    // quadrant ((c%2), (c/2)) of the 2x2 atlas).
    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;

    // Slope-scaled depth bias on top of the normal offset.
    float NdotL = saturate(dot(N, L));
    float bias = 0.0003 + 0.0015 * (1.0 - NdotL);
    float compareDepth = ndc.z - bias;

    // Rotated 4-tap Poisson disk with IGN dithering. Clamp taps inside the
    // quadrant so they never bleed into a neighbouring cascade.
    float texelAtlas = 0.5 / pbrParams.z;
    float2 quadrantMin = quadrantOffset + texelAtlas * 0.5;
    float2 quadrantMax = quadrantOffset + 0.5 - texelAtlas * 0.5;

    float angle = InterleavedGradientNoise(screenPos) * 6.2831853;
    float s, c;
    sincos(angle, s, c);
    float2x2 rotation = float2x2(c, -s, s, c);

    static const float spread = 1.5; // Poisson disk radius in texels
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

// Sun shadow with cascade blending: within the last fraction of each cascade
// band the next cascade is cross-faded in (beyond the last split the shadow
// fades to unshadowed). Splits are radial distances anchored to the camera, so
// they sweep across the scene when the camera moves; without the blend, a
// receiver crossing a split hard-switches between two cascades whose texel
// grids and biases disagree, which looks like the shadow jumping.
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

// Physically-based procedural sky: single-scattering atmosphere with a sun
// disc (tinted by the same atmosphere on the C# side) and a star field.
float3 GetSkyColor(float3 direction)
{
    float3 dirToSun = normalize(-sunDirection.xyz);
    float3 sky = AtmosphereSkyRadiance(direction, dirToSun, skyParams, skyParams2, 16, 8);
    sky += AtmosphereStars(direction, dirToSun, skyParams2.x);

    if (pbrParams.w > 0.5)
    {
        float sunDot = dot(normalize(direction), dirToSun);
        // Sun disc visual size (params4.x) and brightness (params4.y) are
        // independent of the scene lighting intensity so the visible sun can
        // be tuned without affecting PBR shading.
        float cosRadius = params4.x;
        float edgeWidth = max((1.0 - cosRadius) * 0.2, 1e-7);
        // Core disc with a soft anti-aliased edge.
        float disc = smoothstep(cosRadius - edgeWidth, cosRadius, sunDot);
        // Faint atmospheric corona extending ~3.5x the disc radius.
        float coronaRange = (1.0 - cosRadius) * 3.5;
        float corona = smoothstep(1.0 - coronaRange, cosRadius, sunDot) - disc;
        sky += sunColorAndIntensity.rgb * params4.y * (disc + corona * 0.08);
    }
    return sky;
}

// sRGB to linear RGB decoding (the albedo target is RGBA8Unorm, manually encoded).
float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Geometric specular antialiasing (Karis): the screen-space variance of the
// G-buffer normal approximates the sub-pixel normal distribution; widening the
// GGX lobe by the corresponding kernel roughness removes the specular sparkle
// that appears on normal-mapped surfaces at a distance.
float GeometricSpecularAA(float3 N, float roughness)
{
    float3 dNdx = ddx(N);
    float3 dNdy = ddy(N);
    float variance = (dot(dNdx, dNdx) + dot(dNdy, dNdy)) * 0.5;
    float kernelRoughness2 = min(2.0 * variance, 0.18);
    return saturate(roughness + sqrt(kernelRoughness2));
}

// Analytic approximation of the split-sum BRDF integral (Lazarov), used to
// weight the cone-traced indirect specular without a BRDF LUT texture.
float3 EnvBRDFApprox(float3 F0, float roughness, float NdotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return F0 * AB.x + AB.y;
}

// Unit-albedo Lambert response for the CPU-filtered sky gradient. The
// coefficients integrate L(z) = lerp(horizon, zenith, z^0.6) over the visible
// upper hemisphere and divide by PI. Unlike a raw sky lookup at the normal,
// this is low frequency and cannot make diffuse normal maps reflect the sky.
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

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(input.uv * viewportSize.xy));

    float3 worldPosition = ReconstructWorldPosition(input);
    float3 viewDirection = normalize(worldPosition - cameraPosition.xyz);

    // Debug: visualize the combined ambient occlusion (material × screen-space).
    if (params2.w > 0.5)
    {
        float3 mrAO = SAMPLE_TEX2D(_mrAO, input.uv).xyz;
        float matAO = mrAO.z;
        float ssaoVal = SAMPLE_TEX2D(_aoTexture, input.uv).r;
        float combined = matAO * ssaoVal;
        return float4(combined, combined, combined, 1.0);
    }

    if (depth >= 0.9999)
    {
        return float4(GetSkyColor(viewDirection), 1.0);
    }

    float3 albedo = DecodeSRGB(SAMPLE_TEX2D(_albedo, input.uv).rgb);
    float3 normalRT = SAMPLE_TEX2D(_normal, input.uv).xyz;
    float4 mrAO = SAMPLE_TEX2D(_mrAO, input.uv);

    float3 N = normalize(normalRT * 2.0 - 1.0);
    float metallic = mrAO.x;
    float roughness = GeometricSpecularAA(N, mrAO.y);
    // Material AO from the G-buffer, screen-space AO from the AO plugin.
    float materialAO = mrAO.z;
    float ssao = SAMPLE_TEX2D(_aoTexture, input.uv).r;
    float ao = materialAO * ssao;
    float3 V = -viewDirection; // surface to camera

    float3 Lo = 0.0;

    // Directional sun light (cascaded shadow map).
    float viewDistance = length(worldPosition - cameraPosition.xyz);
    int cascade = SelectCascade(viewDistance);
    float sunShadow = 1.0;
    {
        float3 L = normalize(-sunDirection.xyz);
        // Skip shadow sampling for back-facing pixels: EvaluatePBR already
        // zeroes their contribution via max(NdotL, 0), so the PCF taps are
        // pure waste on roughly 30–50 % of screen-space pixels.
        float sunNdotL = dot(N, L);
        if (pbrParams.x > 0.5 && sunNdotL > 0.0)
        {
            sunShadow = SampleSunShadow(worldPosition, N, L, input.position.xy, viewDistance, cascade);
        }

        Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
            * sunColorAndIntensity.rgb
            * sunColorAndIntensity.w
            * sunShadow;
    }

    // Debug: visualize the raw sun shadow factor (white = lit, black = shadowed).
    if (params2.y > 0.5)
    {
        return float4(sunShadow, sunShadow, sunShadow, 1.0);
    }

    // Point lights (StructuredBuffer with per-light range / smooth attenuation).
    {
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

            // Smooth inverse-square falloff with range-based cutoff.
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
    }

    // Build the diffuse environment baseline independently of voxel GI. This is
    // the diffuse environment-probe accumulation: shadows only remove direct
    // sun and never remove this low-frequency illumination.
    float3 skyAmbient = EvaluateDiffuseSky(N);
    float upDot = saturate(N.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 indirectSpecularTerm = 0.0;

    if (params3.x > 0.5)
    {
        // Full-resolution textures from the GI plugin's upsample pass. The
        // directional modulation (ALD), near/far layer blend and depth-weighted
        // upsampling have all been applied by the plugin already.
        float4 giDiffuseSample = SAMPLE_TEX2D(_giDiffuse, input.uv);
        float3 giDiffuseColor = giDiffuseSample.rgb;
        float giVisibility = giDiffuseSample.a;
        float3 giSpecularColor = SAMPLE_TEX2D(_giSpecular, input.uv).rgb;

        // Debug: visualize bounce radiance, specular radiance, or voxel
        // environment visibility (white=open, black=occluded).
        if (params3.w > 0.5)
        {
            if (params3.w < 1.5)
            {
                return float4(giDiffuseColor, 1.0);
            }
            if (params3.w < 2.5)
            {
                return float4(giSpecularColor, 1.0);
            }
            if (params3.w < 3.5)
            {
                return float4(giVisibility, giVisibility, giVisibility, 1.0);
            }
            return float4(giDiffuseColor, 1.0);
        }

        // Cone tracing has already integrated sky radiance independently
        // along every visible direction and added bounced surface radiance.
        // The ambientFloor is still added so deeply occluded areas where cone
        // tracing returns near-zero radiance never go pitch-black. The floor
        // is small (~0.05–0.15) and still multiplied by AO below, so
        // GI-driven AO contrast is preserved.
        diffuseIrradiance = max(giDiffuseColor, 0.0)
            * params3.y + ambientFloor;

        float NdotV = max(dot(N, V), 0.0);
        float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
        indirectSpecularTerm = giSpecularColor
            * EnvBRDFApprox(F0, roughness, NdotV)
            * params3.z;
    }

    // Material AO and screen-space AO affect indirect/environment illumination
    // only. The HBAO strength is reduced by the caller while voxel GI is active
    // so the two independent occlusion estimates do not crush the same corners.
    float3 ambient = (diffuseIrradiance * albedo * (1.0 - metallic)
        + indirectSpecularTerm) * ao;

    // Emissive is added unshaded (stored linear in the G-buffer).
    float3 emissive = SAMPLE_TEX2D(_emissive, input.uv).rgb;

    float3 color = Lo + ambient + emissive;

    // Debug: tint each shadow cascade (0=red 1=green 2=blue 3=yellow).
    if (params2.x > 0.5 && cascade >= 0)
    {
        float3 cascadeTints[4] = {
            float3(1.0, 0.35, 0.35), float3(0.4, 1.0, 0.4),
            float3(0.4, 0.6, 1.0), float3(1.0, 1.0, 0.4) };
        color *= cascadeTints[cascade];
    }

    return float4(color, 1.0);
}
