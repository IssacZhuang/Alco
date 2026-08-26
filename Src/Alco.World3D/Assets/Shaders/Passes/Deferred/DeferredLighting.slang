// file "DeferredLighting.slang" - module "DeferredLighting"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/DeferredLighting.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module DeferredLighting;

import AlcoRendering_Core;
import AlcoWorld3D_ReversedDepth;
import AlcoWorld3D_PBRCommon;

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

// Pass-specific G-buffer textures: one ParameterBlock (its set is the
// compiler-assigned declaration order; sets before it are AlcoWorld3D_PBRCommon's).
// Members resolve by bare field name; the engine binds them by name.
public struct LightingParams
{
    public Texture2D albedo;
    public Texture2D normal;
    public Texture2D mrAO;
    public DepthTexture2D gbufferDepth;
    public Texture2D emissive;
// Indirect GI textures from the GI render plugin (full-resolution):
// diffuse irradiance with ALD directional modulation pre-applied, and
// specular radiance. White/black fallbacks are bound by the pipeline when
// no GI plugin is active.
    Texture2D giDiffuse;
    Texture2D giSpecular;
// Screen-space ambient occlusion from an AO render plugin (full-resolution,
// white = unoccluded). The pipeline binds white when no AO plugin is active.
    Texture2D aoTexture;
// Cloud shadow coverage from the volumetric clouds plugin (r = 1 - column
// transmittance at the cloud slab, camera-centered world grid; white when no
// clouds plugin is active).
    public Texture2D cloudShadow;
};

public ParameterBlock<LightingParams> lighting;


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
// rejected by isSkyDepth before their reconstruction is used; the clamp only
// keeps the view ray finite when a reconstruction slips past a sentinel.
float3 reconstructWorldPosition(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = max(getPixelDepth2D(lighting.gbufferDepth,
        int2(input.uv * uint2(data.viewportWidth, data.viewportHeight))), PBR_SKY_DEPTH_EPSILON);
    float4 world = mul(data.invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// View-linear depth of the current pixel from the homogeneous w produced by
// the inverse view-projection (fourth matrix row only). Matches the metric
// stored in the GI atlas layer depths.
float reconstructLinearDepth(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = max(getPixelDepth2D(lighting.gbufferDepth,
        int2(input.uv * uint2(data.viewportWidth, data.viewportHeight))), PBR_SKY_DEPTH_EPSILON);
    float reciprocalClipW = dot(data.invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

// Shared PBR functions (distributionGGX, evaluatePBR, shadow sampling, sky,
// envBRDFApprox, evaluateDiffuseSky, geometricSpecularAA, evaluatePointLights)
// are provided by PBRCommon.hlsli included above.

[shader("fragment")]
float4 MainPS<let debugView : int>(V2F input) : SV_TARGET
{
    // debugView is a generic value specialization (plan D3): 0 = normal
    // rendering, 1 = combined AO, 2 = sun shadow factor, 3 = cascade tint,
    // 4 = GI diffuse, 5 = GI specular, 6 = GI visibility. Dead branches fold
    // away at compile time, so the non-debug variant carries no branch cost.
    float depth = getPixelDepth2D(lighting.gbufferDepth,
        int2(input.uv * uint2(data.viewportWidth, data.viewportHeight)));

    float3 worldPosition = reconstructWorldPosition(input);
    float3 viewDirection = normalize(worldPosition - data.cameraPosition.xyz);

    // Debug: visualize the combined ambient occlusion (material × screen-space).
    if (debugView == 1)
    {
        float3 mrAO = lighting.mrAO.Sample(samplers.linearClamp, input.uv).xyz;
        float matAO = mrAO.z;
        float ssaoVal = lighting.aoTexture.Sample(samplers.linearClamp, input.uv).r;
        float combined = matAO * ssaoVal;
        return float4(combined, combined, combined, 1.0);
    }

    if (isSkyDepth(depth))
    {
        return float4(getSkyColor(viewDirection), 1.0);
    }

    float3 albedo = decodeSRGB(lighting.albedo.Sample(samplers.linearClamp, input.uv).rgb);
    float3 normalRT = lighting.normal.Sample(samplers.linearClamp, input.uv).xyz;
    float4 mrAO = lighting.mrAO.Sample(samplers.linearClamp, input.uv);

    float3 n = normalize(normalRT * 2.0 - 1.0);
    float metallic = mrAO.x;
    float roughness = geometricSpecularAA(n, mrAO.y);
    // Material AO from the G-buffer, screen-space AO from the AO plugin.
    float materialAO = mrAO.z;
    float ssao = lighting.aoTexture.Sample(samplers.linearClamp, input.uv).r;
    float ao = materialAO * ssao;
    float3 v = -viewDirection; // surface to camera

    float3 lo = 0.0;

    // Directional sun light (cascaded shadow map).
    float viewDistance = length(worldPosition - data.cameraPosition.xyz);
    int cascade = selectCascade(viewDistance);
    float sunShadow = 1.0;
    {
        float3 l = normalize(-data.sunDirection.xyz);
        // Skip shadow sampling for back-facing pixels: evaluatePBR already
        // zeroes their contribution via max(ndotL, 0), so the PCF taps are
        // pure waste on roughly 30–50 % of screen-space pixels.
        float sunNdotL = dot(n, l);
        if (data.shadowEnabled && sunNdotL > 0.0)
        {
            sunShadow = sampleSunShadow(worldPosition, n, l, input.position.xy, viewDistance, cascade);
        }

        // Volumetric cloud shadows: dim the direct sun by the cloud column
        // coverage where the receiver's sun ray pierces the cloud slab. The
        // coverage texture is baked by the clouds plugin around the camera.
        if (data.cloudShadowEnabled && l.z > 0.02)
        {
            float3 hit = worldPosition + l * ((data.cloudShadowPlaneAltitude - worldPosition.z) / l.z);
            float2 uv = (hit.xy - data.cameraPosition.xy) / data.cloudShadowExtent + 0.5;
            sunShadow *= 1.0 - data.cloudShadowStrength * lighting.cloudShadow.Sample(samplers.linearClamp, uv).r;
        }

        lo += evaluatePBR(n, v, l, albedo, metallic, roughness)
            * data.sunColorAndIntensity.rgb
            * data.sunColorAndIntensity.w
            * sunShadow;
    }

    // Debug: visualize the raw sun shadow factor (white = lit, black = shadowed).
    if (debugView == 2)
    {
        return float4(sunShadow, sunShadow, sunShadow, 1.0);
    }

    // Point lights (shared loop from PBRCommon.hlsli).
    lo += evaluatePointLights(n, v, worldPosition, albedo, metallic, roughness);

    // Build the diffuse environment baseline independently of voxel GI. This is
    // the diffuse environment-probe accumulation: shadows only remove direct
    // sun and never remove this low-frequency illumination.
    float3 skyAmbient = evaluateDiffuseSky(n);
    float upDot = saturate(n.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = data.skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 indirectSpecularTerm = 0.0;

    if (data.giEnabled)
    {
        // Full-resolution textures from the GI plugin's upsample pass. The
        // directional modulation (ALD), near/far layer blend and depth-weighted
        // upsampling have all been applied by the plugin already.
        float4 giDiffuseSample = lighting.giDiffuse.Sample(samplers.linearClamp, input.uv);
        float3 giDiffuseColor = giDiffuseSample.rgb;
        float giVisibility = giDiffuseSample.a;
        float3 giSpecularColor = lighting.giSpecular.Sample(samplers.linearClamp, input.uv).rgb;

        // Debug: visualize bounce radiance, specular radiance, or voxel
        // environment visibility (white=open, black=occluded).
        if (debugView == 4)
        {
            return float4(giDiffuseColor, 1.0);
        }
        if (debugView == 5)
        {
            return float4(giSpecularColor, 1.0);
        }
        if (debugView == 6)
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
            * data.giDiffuseStrength + ambientFloor;

        float ndotV = max(dot(n, v), 0.0);
        float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
        indirectSpecularTerm = giSpecularColor
            * envBRDFApprox(f0, roughness, ndotV)
            * data.giSpecularStrength;
    }

    // Material AO and screen-space AO affect indirect/environment illumination
    // only. The HBAO strength is reduced by the caller while voxel GI is active
    // so the two independent occlusion estimates do not crush the same corners.
    float3 ambient = (diffuseIrradiance * albedo * (1.0 - metallic)
        + indirectSpecularTerm) * ao;

    // Emissive is added unshaded (stored linear in the G-buffer).
    float3 emissive = lighting.emissive.Sample(samplers.linearClamp, input.uv).rgb;

    float3 color = lo + ambient + emissive;

    // Debug: tint each shadow cascade (0=red 1=green 2=blue 3=yellow).
    if (debugView == 3 && cascade >= 0)
    {
        float3 cascadeTints[4] = {
            float3(1.0, 0.35, 0.35), float3(0.4, 1.0, 0.4),
            float3(0.4, 0.6, 1.0), float3(1.0, 1.0, 0.4) };
        color *= cascadeTints[cascade];
    }

    return float4(color, 1.0);
}
