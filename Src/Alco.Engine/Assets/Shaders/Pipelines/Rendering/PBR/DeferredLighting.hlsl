#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Atmosphere.hlsli"

// Deferred lighting pass shader for the PBR pipeline.
// Samples the G-buffer, evaluates a GGX PBR BRDF with a directional sun
// (shadow mapped, hardware PCF), up to four point lights, an ambient term
// (sky/probe baseline modulated by voxel visibility plus traced bounce light)
// and a physically-based procedural sky (single
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
    float4 pointLight0Position;
    float4 pointLight0Color;     // rgb + intensity
    float4 pointLight1Position;
    float4 pointLight1Color;     // rgb + intensity
    float4 pointLight2Position;
    float4 pointLight2Color;     // rgb + intensity
    float4 pointLight3Position;
    float4 pointLight3Color;     // rgb + intensity
    float4 pbrParams;            // x=shadowEnabled y=pointLightEnabled z=shadowMapSize w=sunDiscEnabled
    float4 cascadeSplits;        // radial end distance of each cascade; beyond w there is no shadow
    float4 cascadeTexelSizes;    // world units per shadow texel of each cascade
    float4 params2;              // x=cascadeDebugTint, y=shadowFactorView, z=unused, w=aoDebugView
    float4 viewportSize;         // xy = render target size in pixels
    float4 params3;              // x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility)
    float4 params4;              // x=sunDiscSize(cosine threshold, higher=smaller) y=sunDiscBrightness z=1/GI trace width w=1/GI trace height (0 when GI is off)
};

DEFINE_TEX2D_SAMPLE(1, _albedo);
DEFINE_TEX2D_SAMPLE(2, _normal);
DEFINE_TEX2D_SAMPLE(3, _mrAO);
DEFINE_TEX2D_DEPTH(4, _gbufferDepth);
DEFINE_TEX2D_DEPTH_SAMPLE(5, _shadowMap);
DEFINE_TEX2D_SAMPLE(6, _emissive);
// Indirect GI atlas from the voxel cone tracing resolve: three times the
// trace width. Sections: diffuse near layer and diffuse far layer (rgb =
// irradiance, a = layer view-linear depth), then specular radiance (rgb;
// alpha carries the selected diffuse visibility for the debug view). The
// lighting pass upscales the two diffuse layers with CE5 UpScalePS's 5-tap
// depth-weighted kernel at full resolution, keeping occlusion edges sharp at
// reduced trace resolutions.
DEFINE_TEX2D_SAMPLE(7, _indirectGI);

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

// Hardware 3x3 PCF against the shadow map cascade atlas (comparison sampler).
// Each SampleCmpLevelZero tap compares (ndc.z - bias) <= texelDepth and, with the
// linear comparison sampler, already blends the four nearest texels.
float SampleShadowMap(float3 worldPosition, float3 N, float3 L, int cascade)
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

    // PCF offsets in atlas UV; clamp taps inside the quadrant so they never
    // bleed into a neighboring cascade.
    float texelAtlas = 0.5 / pbrParams.z;
    float2 quadrantMin = quadrantOffset + texelAtlas * 0.5;
    float2 quadrantMax = quadrantOffset + 0.5 - texelAtlas * 0.5;
    float shadow = 0.0;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            float2 uv = clamp(shadowUV + float2(dx, dy) * texelAtlas, quadrantMin, quadrantMax);
            shadow += SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, uv, compareDepth);
        }
    }
    return shadow / 9.0;
}

// Sun shadow with cascade blending: within the last fraction of each cascade
// band the next cascade is cross-faded in (beyond the last split the shadow
// fades to unshadowed). Splits are radial distances anchored to the camera, so
// they sweep across the scene when the camera moves; without the blend, a
// receiver crossing a split hard-switches between two cascades whose texel
// grids and biases disagree, which looks like the shadow jumping.
float SampleSunShadow(float3 worldPosition, float3 N, float3 L, float viewDistance, int cascade)
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

    // Debug: visualize the ambient occlusion channel of the G-buffer (material
    // AO already multiplied by HBAO; white = unoccluded).
    if (params2.w > 0.5)
    {
        float rawAO = SAMPLE_TEX2D(_mrAO, input.uv).z;
        return float4(rawAO, rawAO, rawAO, 1.0);
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
    // The HBAO blur pass already multiplied screen-space AO into this channel.
    float ao = mrAO.z;
    float3 V = -viewDirection; // surface to camera

    float3 Lo = 0.0;

    // Directional sun light (cascaded shadow map).
    float viewDistance = length(worldPosition - cameraPosition.xyz);
    int cascade = SelectCascade(viewDistance);
    float sunShadow = 1.0;
    {
        float3 L = normalize(-sunDirection.xyz);
        if (pbrParams.x > 0.5)
        {
            sunShadow = SampleSunShadow(worldPosition, N, L, viewDistance, cascade);
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

    // Point lights.
    if (pbrParams.y > 0.5)
    {
        float4 pointLightPositions[4] = {
            pointLight0Position, pointLight1Position,
            pointLight2Position, pointLight3Position };
        float4 pointLightColors[4] = {
            pointLight0Color, pointLight1Color,
            pointLight2Color, pointLight3Color };

        for (int i = 0; i < 4; i++)
        {
            float3 lightColor = pointLightColors[i].rgb;
            float lightIntensity = pointLightColors[i].w;
            if (lightIntensity <= 0.0)
            {
                continue;
            }

            float3 toLight = pointLightPositions[i].xyz - worldPosition;
            float distanceSqr = dot(toLight, toLight);
            float attenuation = 1.0 / (distanceSqr + 1.0);

            float3 L = normalize(toLight);
            Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
                * lightColor
                * lightIntensity
                * attenuation;
        }
    }

    // Build the diffuse environment baseline independently of voxel GI. This is
    // the equivalent of CE5's diffuse environment-probe accumulation: shadows
    // only remove direct sun and never remove this low-frequency illumination.
    float3 skyAmbient = EvaluateDiffuseSky(N);
    float upDot = saturate(N.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 indirectSpecularTerm = 0.0;

    if (params3.x > 0.5)
    {
        // The atlas is three times the trace width: the diffuse near layer and
        // far layer (rgb = irradiance, a = layer view-linear depth), then
        // specular (rgb; alpha carries the selected diffuse visibility).
        float2 traceUV = input.uv * float2(1.0 / 3.0, 1.0);
        // CE5 UpScalePS: reconstruct the diffuse term at full resolution with
        // a 5-tap cross kernel over the trace texture. Every tap is bilinearly
        // filtered, blended between its near/far layers at this pixel's depth,
        // and weighted by a soft relative-depth test (center counts four
        // times), so occlusion edges keep full-resolution precision instead
        // of stair-stepping at trace texels.
        float linearDepth = ReconstructLinearDepth(input);
        float2 traceTexel = params4.zw; // one trace texel in segment-local UV
        float2 atlasTexel = float2(traceTexel.x * (1.0 / 3.0), traceTexel.y);
        float4 sampleTM = float4(atlasTexel * 1.5, atlasTexel * 0.25);
        const float2 sampleOffsets[5] =
        {
            float2( 0, -1) * sampleTM.xy - sampleTM.zw,
            float2( 0,  1) * sampleTM.xy - sampleTM.zw,
            float2(-1,  0) * sampleTM.xy - sampleTM.zw,
            float2( 1,  0) * sampleTM.xy - sampleTM.zw,
            float2( 0,  0) * sampleTM.xy - sampleTM.zw,
        };

        float3 indirectDiffuseSum = 0.0;
        float indirectDiffuseWeight = 0.0;
        [unroll]
        for (int s = 0; s < 5; s++)
        {
            float2 tapUV = traceUV + sampleOffsets[s];
            float4 tapDiffuseMin = SAMPLE_TEX2D(_indirectGI, tapUV);
            float4 tapDiffuseMax = SAMPLE_TEX2D(_indirectGI, tapUV + float2(1.0 / 3.0, 0.0));

            // CE5 clamps the layer depths at 4 m ("reduce artifacts around 1p
            // weapon") so near-camera depth ratios cannot explode.
            float tapDepthMin = max(4.0, tapDiffuseMin.a);
            float tapDepthMax = max(4.0, tapDiffuseMax.a);
            float tapLerp = saturate(
                (linearDepth - tapDepthMin) / max(tapDepthMax - tapDepthMin, 0.0001));
            float3 tapDiffuse = lerp(tapDiffuseMin.rgb, tapDiffuseMax.rgb, tapLerp);
            float tapDepth = lerp(tapDepthMin, tapDepthMax, tapLerp);

            // CE5 fDepTest with the 0.25 fDotTest floor (no average light
            // direction is stored in the atlas to steer rejection).
            float depthTest = saturate(
                (0.12 - abs(1.0 - linearDepth / tapDepth)) * 4.0);
            float tapWeight = depthTest * 0.25;
            if (s == 4)
            {
                tapWeight = saturate(tapWeight * 4.0);
            }
            tapWeight += 0.001;

            indirectDiffuseSum += tapDiffuse * tapWeight;
            indirectDiffuseWeight += tapWeight;
        }

        float4 indirectSpecularSection = SAMPLE_TEX2D(_indirectGI, traceUV + float2(2.0 / 3.0, 0.0));
        float4 indirectDiffuse = float4(
            indirectDiffuseSum / indirectDiffuseWeight,
            indirectSpecularSection.a);
        float3 indirectSpecular = indirectSpecularSection.rgb;

        // Debug: visualize bounce radiance, specular radiance, or voxel
        // environment visibility (white=open, black=occluded).
        if (params3.w > 0.5)
        {
            if (params3.w < 1.5)
            {
                return float4(indirectDiffuse.rgb, 1.0);
            }
            if (params3.w < 2.5)
            {
                return float4(indirectSpecular, 1.0);
            }
            if (params3.w < 3.5)
            {
                return float4(indirectDiffuse.aaa, 1.0);
            }
            return float4(indirectDiffuse.rgb, 1.0);
        }

        // CE5 replacement mode: cone tracing has already integrated sky
        // radiance independently along every visible direction and added
        // bounced surface radiance. Reapplying a scalar visibility factor to
        // the independent environment baseline would make an entire sun-shadow
        // region dark whenever direct light is absent.
        diffuseIrradiance = max(indirectDiffuse.rgb, 0.0) * params3.y;

        float NdotV = max(dot(N, V), 0.0);
        float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
        indirectSpecularTerm = indirectSpecular
            * EnvBRDFApprox(F0, roughness, NdotV)
            * params3.z;
    }

    // Material AO and HBAO affect indirect/environment illumination only. The
    // HBAO strength is reduced by the caller while voxel GI is active so the
    // two independent occlusion estimates do not crush the same corners twice.
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
