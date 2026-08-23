// Reflective shadow map (RSM) pass template for the voxel GI's sun-bounce
// injection (CRYENGINE SVOTI style). Slang counterpart of
// Shaders/Pipelines/Rendering/PBR/Rsm.hlsl: the material compiler #includes
// this file into a generated wrapper translation unit (pass defines apply)
// and instantiates the generic pass functions with the material's surface type.
//
// Renders the scene from the sun's point of view - the selected CSM cascade -
// into albedo + world-normal color targets, so the GI cone trace can sample
// shadow-map-resolution sun radiance where its march touches geometry. This
// pass consumes base color and the tangent-space normal, nothing else. The
// vertex layout must match Alco.Rendering.VertexPBR exactly. The push
// constant carries only the RSM cascade index (per-pass constant).
//
// The pass reuses the shadow pass's per-cascade view-projection uniform (the
// matrices folded into the 2x2 atlas quadrants by RGNode_ShadowPass) and
// unfolds the quadrant back to full NDC here, so no separate matrix upload
// exists for this pass.

#language slang 2025
module rsm;

import surface;
import pbr_instance;

public struct RsmVertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
    uint instanceID : SV_VulkanInstanceID;
}

public struct RsmV2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float4 tangent : TEXCOORD1; // xyz = world tangent, w = bitangent sign
    float3 worldPos : TEXCOORD2;
    float2 uv : TEXCOORD3;
    uint instanceID : TEXCOORD4;
}

// Push constant payload: only the RSM cascade index remains per-draw
// (per-pass constant). Layout must match the CascadeConstants struct in
// ShadowRenderer.cs exactly.
struct RsmConstants
{
    // x = shadow cascade index (the RSM cascade), yzw unused
    float4 params_;
}

// Per-cascade light view-projection matrices, updated per frame on the CPU and
// shared with shadow-depth.slang (reference semantics keep render bundles
// valid while the camera-fitted cascades move).
[[vk::binding(0, 0)]] cbuffer _data
{
    float4x4 lightViewProjections[4];
}

[[vk::binding(0, 1)]] RWStructuredBuffer<PbrInstance> _instances;

[[vk::push_constant]]
cbuffer constants
{
    RsmConstants rsmConstants;
}

// Linear RGB to sRGB encoding (the albedo target is RGBA8Unorm, matching the
// G-buffer's albedo path; the GI trace decodes it back).
float3 EncodeSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = 1.055 * pow(max(color, 0.0), float3(1.0 / 2.4)) - 0.055;
    return lerp(hi, lo, step(color, float3(0.0031308, 0.0031308, 0.0031308)));
}

public RsmV2F RsmMainVS<T : ISurface>(RsmVertex input)
{
    RsmV2F output;
    PbrInstance inst = _instances[input.instanceID];
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so the RSM matches the G-buffer silhouette.
    T surface = T();
    surface.ModifyVertex(worldPos, worldNormal, input.uv);
    uint cascade = (uint)rsmConstants.params_.x;
    float4 folded = mul(lightViewProjections[cascade], float4(worldPos, 1.0f));
    // RGNode_ShadowPass folds the cascade into its atlas quadrant with
    // ndc' = ndc * 0.5 + offset, offset = ((cascade % 2) - 0.5, 0.5 - cascade / 2).
    // The RSM covers a full target, so unfold back to [-1, 1] here.
    float2 quadrantOffset = float2(
        (float)(cascade % 2u) - 0.5f,
        0.5f - (float)(cascade / 2u));
    output.position = float4((folded.xy - quadrantOffset) * 2.0f, folded.zw);
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.worldPos = worldPos;
    output.uv = input.uv;
    output.instanceID = input.instanceID;
    return output;
}

public void RsmMainPS<T : ISurface>(
    RsmV2F input,
    out float4 albedoRT,
    out float4 normalRT)
{
    PbrInstance inst = _instances[input.instanceID];

    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    SurfaceInput surfaceInput;
    surfaceInput.worldPos = input.worldPos;
    surfaceInput.normalWS = n;
    surfaceInput.tangentWS = float4(t, input.tangent.w);
    surfaceInput.uv = input.uv;
    surfaceInput.baseColorFactor = inst.baseColor;
    surfaceInput.metallicRoughnessAO = inst.metallicRoughnessAO;
    surfaceInput.emissiveFactor = inst.emissive;
    surfaceInput.alphaCutoff = inst.params_.x;

    T surface = T();
    float4 baseColor = surface.GetBaseColor(surfaceInput);

    // Alpha test (mirrors gbuffer.slang): cutout meshes keep correctly shaped
    // bounce light, not the alpha-quantized silhouette.
    if (surfaceInput.alphaCutoff > 0.0 && baseColor.a < surfaceInput.alphaCutoff)
    {
        discard;
    }

    albedoRT = float4(EncodeSRGB(baseColor.rgb), 1.0);
    float3 normalTS = surface.GetNormalTS(surfaceInput);
    float3 worldNormal = normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
    normalRT = float4(worldNormal * 0.5 + 0.5, 1.0);
}
