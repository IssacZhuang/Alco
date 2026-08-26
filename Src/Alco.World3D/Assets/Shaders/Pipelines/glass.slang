// Forward-lit glass pass template for surface materials of the PBR deferred
// pipeline's transparency pass. Slang counterpart of Shaders/Pipelines/
// Rendering/PBR/ForwardGlass.hlsl.
//
// A pass template is a self-describing module: it owns the [shader] entry
// points, generic over the surface type; the material compiler composes this
// module with a material's surface module (composite + link-time
// specialization with the surface's conforming type, discovered by the
// compiler - no type names, no generated wrapper code). Slang type-checks the
// surface against ISurface at instantiation.
//
// Renders semi-transparent glass objects after deferred lighting, blending
// onto the lit HDR scene: hardware depth testing against the opaque scene
// (the pipeline pre-fills the forward RT's depth from the G-buffer), alpha
// blending with Max on alpha (no sorting). Opacity is driven by the
// transmission factor (texture alpha only raises it further), so glass stays
// visible even when the albedo texture's alpha channel is zero. Lighting
// functions and the shared data cbuffer come from alco_world3d_pbr_common;
// the material textures live in the surface module (set 2).

#language slang 2025
module glass;

import alco_world3d_surface;
import alco_world3d_pbr_common;

public struct GlassVertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
}

public struct GlassV2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 tangent : TEXCOORD2; // xyz = world tangent, w = bitangent sign
    float3 worldPosition : TEXCOORD3;
}

// Push constant payload: per-draw model and factors (non-instanced draw path).
// Layout must match the constants the forward node records exactly.
struct GlassConstants
{
    float4x4 model;
    float4 baseColor;
    float4 metallicRoughnessAO; // x=metallic y=roughness z=ambientOcclusion
    float4 params;             // x=transmissionFactor (0=opaque, 1=fully transparent)
    float4 emissive;            // rgb = emissive factor
}

// Sets owned by alco_world3d_pbr_common and the composed surface module come
// first; the camera takes this template's own ParameterBlock (its set is the
// compiler-assigned declaration order). The engine binds everything by name.
public struct GlassCamera
{
    public float4x4 viewProjection;
};

public ParameterBlock<GlassCamera> camera;

[[vk::push_constant]]
cbuffer constants
{
    GlassConstants glassConstants;
}

[shader("vertex")]
public GlassV2F MainVS<T : ISurface>(GlassVertex input)
{
    GlassV2F output;
    float3 worldPosition = mul(glassConstants.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)glassConstants.model, input.normal);
    float3 worldTangent = mul((float3x3)glassConstants.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically.
    T surface = T();
    surface.modifyVertex(worldPosition, worldNormal, input.uv);
    output.position = mul(camera.viewProjection, float4(worldPosition, 1.0f));
    output.worldPosition = worldPosition;
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
public float4 MainPS<T : ISurface>(GlassV2F input) : SV_TARGET
{
    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    SurfaceInput surfaceInput;
    surfaceInput.worldPos = input.worldPosition;
    surfaceInput.normalWS = n;
    surfaceInput.tangentWS = float4(t, input.tangent.w);
    surfaceInput.uv = input.uv;
    surfaceInput.baseColorFactor = glassConstants.baseColor;
    surfaceInput.metallicRoughnessAO = glassConstants.metallicRoughnessAO;
    surfaceInput.emissiveFactor = glassConstants.emissive;
    surfaceInput.alphaCutoff = 0.0f; // glass does not alpha-test

    T surface = T();
    float4 baseColor = surface.getBaseColor(surfaceInput);
    float3 mrAO = surface.getMetallicRoughnessAO(surfaceInput);
    float3 normalTS = surface.getNormalTS(surfaceInput);

    float3 albedo = baseColor.rgb;
    float alpha = baseColor.a;
    float metallic = mrAO.x;
    float roughness = mrAO.y;
    float ao = mrAO.z;

    float3 nWorld = normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);

    float3 worldPosition = input.worldPosition;
    float3 v = normalize(data.cameraPosition.xyz - worldPosition);

    float3 lo = 0.0;

    // Directional sun light with cascaded shadows.
    {
        float3 l = normalize(-data.sunDirection.xyz);
        float sunNdotL = dot(nWorld, l);
        float sunShadow = 1.0;
        float viewDistance = length(worldPosition - data.cameraPosition.xyz);
        int cascade = selectCascade(viewDistance);
        if (data.shadowEnabled && sunNdotL > 0.0)
        {
            sunShadow = sampleSunShadow(worldPosition, nWorld, l, input.position.xy, viewDistance, cascade);
        }

        lo += evaluatePBR(nWorld, v, l, albedo, metallic, roughness)
            * data.sunColorAndIntensity.rgb
            * data.sunColorAndIntensity.w
            * sunShadow;
    }

    // Point lights (shared loop from alco_world3d_pbr_common).
    lo += evaluatePointLights(nWorld, v, worldPosition, albedo, metallic, roughness);

    // Ambient / environment lighting.
    float3 skyAmbient = evaluateDiffuseSky(nWorld);
    float upDot = saturate(nWorld.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = data.skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 ambient = diffuseIrradiance * albedo * (1.0 - metallic) * ao;

    // Emissive (surface = emissive texture times the push-constant factor).
    float3 emissive = surface.getEmissive(surfaceInput);

    float3 color = lo + ambient + emissive;

    // Output alpha: blend factor onto the lit scene. Opacity comes from the
    // transmission factor and the texture alpha (whichever is larger — the
    // Bistro glass textures carry a zero alpha channel, so transmission must
    // not rely on it).
    float transmission = glassConstants.params.x;
    float outputAlpha = saturate(max(alpha, 1.0 - transmission));

    return float4(color, outputAlpha);
}
