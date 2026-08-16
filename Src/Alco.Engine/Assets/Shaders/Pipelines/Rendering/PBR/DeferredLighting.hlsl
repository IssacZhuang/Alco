#include "Shaders/Libs/Core.hlsli"

// Deferred lighting pass shader for the PBR pipeline.
// Samples the G-buffer, evaluates a GGX PBR BRDF with a directional sun
// (shadow mapped, hardware PCF), dynamic point lights from a StructuredBuffer,
// an ambient term (sky/probe baseline modulated by voxel visibility plus
// traced bounce light) and a physically-based procedural sky (single
// scattering atmosphere plus sun disc and stars) for empty pixels.
// Shared PBR functions, cbuffer, point-light buffer and shadow map are in
// PBRCommon.hlsli (included after the pass-specific declarations below).

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

// Pass-specific G-buffer textures (set 1). The shared _data cbuffer,
// _pointLights buffer and _shadowMap texture live in PBRCommon.hlsli.
DEFINE_TEX2D_SAMPLE(1, _albedo);
DEFINE_TEX2D_SAMPLE(1, _normal);
DEFINE_TEX2D_SAMPLE(1, _mrAO);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
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
// Cloud shadow coverage from the volumetric clouds plugin (r = 1 - column
// transmittance at the cloud slab, camera-centered world grid; white when no
// clouds plugin is active).
DEFINE_TEX2D_SAMPLE(1, _cloudShadow);
// Shadowed point-light diffuse irradiance from the point light shadow plugin
// (full-resolution, temporally resolved; rgb = irradiance). Divided by the
// locally evaluated unshadowed irradiance to reconstruct a per-pixel
// visibility. Black and flagged off (params2.z) when the feature is inactive —
// the inline unshadowed loop is used instead.
DEFINE_TEX2D_SAMPLE(1, _pointLightShadowed);

#include "Shaders/Pipelines/Rendering/PBR/PBRCommon.hlsli"

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

// Shared PBR functions (DistributionGGX, EvaluatePBR, shadow sampling, sky,
// EnvBRDFApprox, EvaluateDiffuseSky, GeometricSpecularAA, EvaluatePointLights)
// are provided by PBRCommon.hlsli included above.

// PCSS-shadowed point lights. The plugin's screen chain stores the atlas-sampled,
// temporally resolved shadowed diffuse irradiance; dividing it by the unshadowed
// irradiance evaluated here (same lights, same attenuation, full-resolution
// normal) reconstructs a per-pixel visibility. Applying that visibility to the
// full-resolution PBR keeps NdotL terminators and GGX highlights pixel-sharp —
// only the shadow signal itself is trace-resolution.
float3 EvaluatePointLightsShadowed(
    float3 N,
    float3 V,
    float3 worldPosition,
    float3 albedo,
    float metallic,
    float roughness,
    float3 shadowedIrradiance)
{
    float3 Lo = 0.0;
    float3 unshadowedIrradiance = 0.0;
    uint lightCount = (uint)pbrParams.y;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = _pointLights[i].positionRange;
        float4 colInt = _pointLights[i].colorIntensity;
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
        float NdotL = saturate(dot(N, L));
        float3 lightColor = colInt.rgb * colInt.w * attenuation;
        unshadowedIrradiance += lightColor * NdotL;
        Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness) * lightColor;
    }

    // Per-channel ratio preserves colored shadows; clamping to 1 keeps the
    // result bounded by the unshadowed evaluation (trace pixels whose normal
    // grazes the light can report more irradiance than this pixel).
    float3 visibility = min(shadowedIrradiance / max(unshadowedIrradiance, 1e-5), 1.0);
    return Lo * visibility;
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

        // Volumetric cloud shadows: dim the direct sun by the cloud column
        // coverage where the receiver's sun ray pierces the cloud slab. The
        // coverage texture is baked by the clouds plugin around the camera.
        if (cloudShadow.w > 0.5 && L.z > 0.02)
        {
            float3 hit = worldPosition + L * ((cloudShadow.y - worldPosition.z) / L.z);
            float2 uv = (hit.xy - cameraPosition.xy) / cloudShadow.z + 0.5;
            sunShadow *= 1.0 - cloudShadow.x * SAMPLE_TEX2D(_cloudShadow, uv).r;
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

    // Point lights. When the point light shadow plugin is active (params2.z >
    // 0.5), its output carries the atlas-sampled, temporally resolved shadowed
    // irradiance; EvaluatePointLightsShadowed turns it into a per-pixel
    // visibility applied to the full-resolution PBR (diffuse + specular).
    // Otherwise fall back to the inline unshadowed loop.
    if (params2.z > 0.5)
    {
        float3 shadowedIrradiance = SAMPLE_TEX2D(_pointLightShadowed, input.uv).rgb;
        Lo += EvaluatePointLightsShadowed(
            N, V, worldPosition, albedo, metallic, roughness, shadowedIrradiance);
    }
    else
    {
        Lo += EvaluatePointLights(N, V, worldPosition, albedo, metallic, roughness);
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
