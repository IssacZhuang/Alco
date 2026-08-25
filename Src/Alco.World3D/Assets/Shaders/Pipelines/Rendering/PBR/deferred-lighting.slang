// file "deferred-lighting.slang" - module "deferred_lighting"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/DeferredLighting.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module deferred_lighting;

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

// Pass-specific G-buffer textures: one block on this pass's own set 2
// (sets 0 and 1 are alco_world3d_pbr_common's). Bindings within the block are
// compiler-assigned in declaration order; the engine binds members by name.
cbuffer _lighting : register(b0, space2)
{
    Texture2D _albedo;
    SamplerState _albedoSampler;
    Texture2D _normal;
    SamplerState _normalSampler;
    Texture2D _mrAO;
    SamplerState _mrAOSampler;
    DepthTexture2D _gbufferDepth;
    Texture2D _emissive;
    SamplerState _emissiveSampler;
// Indirect GI textures from the GI render plugin (full-resolution):
// diffuse irradiance with ALD directional modulation pre-applied, and
// specular radiance. White/black fallbacks are bound by the pipeline when
// no GI plugin is active.
    Texture2D _giDiffuse;
    SamplerState _giDiffuseSampler;
    Texture2D _giSpecular;
    SamplerState _giSpecularSampler;
// Screen-space ambient occlusion from an AO render plugin (full-resolution,
// white = unoccluded). The pipeline binds white when no AO plugin is active.
    Texture2D _aoTexture;
    SamplerState _aoTextureSampler;
// Cloud shadow coverage from the volumetric clouds plugin (r = 1 - column
// transmittance at the cloud slab, camera-centered world grid; white when no
// clouds plugin is active).
    Texture2D _cloudShadow;
    SamplerState _cloudShadowSampler;
}


[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// Reversed infinite camera depth (near = 1, sky = 0): back-projecting depth 0
// would divide by a homogeneous w of 0, so the clamps below map sky to a finite
// far point (~near / PBR_SKY_DEPTH_EPSILON metres). Sky pixels are still
// rejected by IS_SKY_DEPTH before their reconstruction is used; the clamp only
// keeps the view ray finite when a reconstruction slips past a sentinel.
float3 ReconstructWorldPosition(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = max(GetPixelDepth2D(_gbufferDepth,
        int2(input.uv * viewportSize.xy)), PBR_SKY_DEPTH_EPSILON);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// View-linear depth of the current pixel from the homogeneous w produced by
// the inverse view-projection (fourth matrix row only). Matches the metric
// stored in the GI atlas layer depths.
float ReconstructLinearDepth(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = max(GetPixelDepth2D(_gbufferDepth,
        int2(input.uv * viewportSize.xy)), PBR_SKY_DEPTH_EPSILON);
    float reciprocalClipW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

// Shared PBR functions (DistributionGGX, EvaluatePBR, shadow sampling, sky,
// EnvBRDFApprox, EvaluateDiffuseSky, GeometricSpecularAA, EvaluatePointLights)
// are provided by PBRCommon.hlsli included above.

[shader("fragment")]
float4 MainPS<let DebugView : int>(V2F input) : SV_TARGET
{
    // DebugView is a generic value specialization (plan D3): 0 = normal
    // rendering, 1 = combined AO, 2 = sun shadow factor, 3 = cascade tint,
    // 4 = GI diffuse, 5 = GI specular, 6 = GI visibility. Dead branches fold
    // away at compile time, so the non-debug variant carries no branch cost.
    float depth = GetPixelDepth2D(_gbufferDepth,
        int2(input.uv * viewportSize.xy));

    float3 worldPosition = ReconstructWorldPosition(input);
    float3 viewDirection = normalize(worldPosition - cameraPosition.xyz);

    // Debug: visualize the combined ambient occlusion (material × screen-space).
    if (DebugView == 1)
    {
        float3 mrAO = SAMPLE_TEX2D(_mrAO, input.uv).xyz;
        float matAO = mrAO.z;
        float ssaoVal = SAMPLE_TEX2D(_aoTexture, input.uv).r;
        float combined = matAO * ssaoVal;
        return float4(combined, combined, combined, 1.0);
    }

    if (IS_SKY_DEPTH(depth))
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
    if (DebugView == 2)
    {
        return float4(sunShadow, sunShadow, sunShadow, 1.0);
    }

    // Point lights (shared loop from PBRCommon.hlsli).
    Lo += EvaluatePointLights(N, V, worldPosition, albedo, metallic, roughness);

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
        if (DebugView == 4)
        {
            return float4(giDiffuseColor, 1.0);
        }
        if (DebugView == 5)
        {
            return float4(giSpecularColor, 1.0);
        }
        if (DebugView == 6)
        {
            return float4(giVisibility, giVisibility, giVisibility, 1.0);
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
    if (DebugView == 3 && cascade >= 0)
    {
        float3 cascadeTints[4] = {
            float3(1.0, 0.35, 0.35), float3(0.4, 1.0, 0.4),
            float3(0.4, 0.6, 1.0), float3(1.0, 1.0, 0.4) };
        color *= cascadeTints[cascade];
    }

    return float4(color, 1.0);
}
