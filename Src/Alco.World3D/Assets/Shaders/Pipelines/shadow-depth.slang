// Shadow map depth-only pass template for surface materials of the deferred
// PBR pipeline. Slang counterpart of Shaders/Pipelines/Rendering/PBR/
// ShadowDepth.hlsl: the material compiler #includes this file into a generated
// wrapper translation unit (pass defines apply - see SHADOW_CUTOUT below) and
// instantiates the generic pass functions with the material's surface type.
//
// Renders into a depth-only render texture from the light's point of view.
// The vertex layout must match Alco.Rendering.VertexPBR exactly. All material
// evaluation lives in the surface module (contract: surface.slang); this
// template owns the entry points, the depth write and the cascade bindings.
// Per-instance data (model matrix, cutout scalars) lives in the _instances
// storage buffer and is fetched by SV_VulkanInstanceID; the push constant carries
// only the cascade index so per-cascade render bundles never re-record.
//
// Compile with SHADOW_CUTOUT defined to enable alpha testing: the pixel
// shader evaluates the surface's base color alpha (GetBaseColor - the only
// surface function this pass consumes) and discards fragments below the
// cutoff, so cutout meshes (foliage, fences, etc.) cast correctly shaped
// shadows. Without the define the pixel shader is empty (zero-overhead
// opaque depth write).

#language slang 2025
module shadow_depth;

import surface;
import pbr_instance;

public struct ShadowVertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
    uint instanceID : SV_VulkanInstanceID;
}

public struct ShadowV2F
{
    float4 position : SV_POSITION;
#if defined(SHADOW_CUTOUT)
    float3 normal : TEXCOORD0;
    float4 tangent : TEXCOORD1; // xyz = world tangent, w = bitangent sign
    float3 worldPos : TEXCOORD2;
    float2 uv : TEXCOORD3;
    uint instanceID : TEXCOORD4;
#else
    uint instanceID : TEXCOORD0;
#endif
}

// Push constant payload: only the cascade index remains per-draw (static per
// cascade bundle). Layout must match the CascadeConstants struct in
// ShadowRenderer.cs exactly.
struct ShadowConstants
{
    // x = shadow cascade index, yzw unused
    float4 params_;
}

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
cbuffer _data : register(b0, space0)
{
    float4x4 lightViewProjections[4];
}

cbuffer _draw : register(b0, space1)
{
    RWStructuredBuffer<PbrInstance> _instances;
}

[[vk::push_constant]]
cbuffer constants
{
    ShadowConstants shadowConstants;
}

public ShadowV2F ShadowMainVS<T : ISurface>(ShadowVertex input)
{
    ShadowV2F output;
    PbrInstance inst = _instances[input.instanceID];
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so shadows match the G-buffer silhouette.
    T surface = T();
    surface.ModifyVertex(worldPos, worldNormal, input.uv);
    output.position = mul(lightViewProjections[(uint)shadowConstants.params_.x], float4(worldPos, 1.0f));
#if defined(SHADOW_CUTOUT)
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.worldPos = worldPos;
    output.uv = input.uv;
#endif
    output.instanceID = input.instanceID;
    return output;
}

public void ShadowMainPS<T : ISurface>(ShadowV2F input)
{
#if defined(SHADOW_CUTOUT)
    PbrInstance inst = _instances[input.instanceID];
    float alphaCutoff = inst.params_.x;
    if (alphaCutoff > 0.0)
    {
        // TBN frame: re-orthogonalize the interpolated tangent against the normal.
        float3 n = normalize(input.normal);
        float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
        t = normalize(t);

        SurfaceInput surfaceInput;
        surfaceInput.worldPos = input.worldPos;
        surfaceInput.normalWS = n;
        surfaceInput.tangentWS = float4(t, input.tangent.w);
        surfaceInput.uv = input.uv;
        surfaceInput.baseColorFactor = inst.baseColor;
        surfaceInput.metallicRoughnessAO = inst.metallicRoughnessAO;
        surfaceInput.emissiveFactor = inst.emissive;
        surfaceInput.alphaCutoff = alphaCutoff;

        T surface = T();
        clip(surface.GetBaseColor(surfaceInput).a - alphaCutoff);
    }
#endif
}
