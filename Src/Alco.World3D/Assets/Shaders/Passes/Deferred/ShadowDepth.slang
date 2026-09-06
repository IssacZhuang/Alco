// Shadow map depth-only pass template for surface materials of the deferred
// PBR pipeline.
//
// A pass template is a self-describing module: it owns the [shader] entry
// points, generic over the surface type; the material compiler composes this
// module with a material's surface module (composite + link-time
// specialization), no generated wrapper code.
//
// Renders into a depth-only render texture from the light's point of view.
// The vertex layout must match Alco.Rendering.VertexPBR exactly. All material
// evaluation lives in the surface module (contract: AlcoWorld3D_Surface);
// this template owns the entry
// points, the depth write and the cascade bindings. Per-instance data (model
// matrix, cutout scalars) lives in the instances storage buffer and is fetched
// by SV_VulkanInstanceID; the push constant carries only the cascade index so
// per-cascade render bundles never re-record.
//
// Alpha cutout is an AlphaTest value specialization: when on, the pixel
// shader evaluates the surface's base color alpha
// (getBaseColor - the only surface function this pass consumes) and discards
// fragments below the cutoff, so cutout meshes (foliage, fences, etc.) cast
// correctly shaped shadows. When off, specialization folds the pixel shader
// away to an empty body (zero-overhead opaque depth write); the interpolants
// stay in ShadowV2F but are never read.

#language slang 2025
module ShadowDepth;

import AlcoWorld3D_Surface;
import AlcoWorld3D_PbrInstance;

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
    float3 normal : TEXCOORD0;
    float4 tangent : TEXCOORD1; // xyz = world tangent, w = bitangent sign
    float3 worldPos : TEXCOORD2;
    float2 uv : TEXCOORD3;
    uint instanceID : TEXCOORD4;
}

// Push constant payload: only the cascade index remains per-draw (static per
// cascade bundle). Layout must match the CascadeConstants struct in
// ShadowRenderer.cs exactly.
struct ShadowConstants
{
    // x = shadow cascade index, yzw unused
    float4 params;
}

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
public struct ShadowData
{
    public float4x4 lightViewProjections[4];
};

public ParameterBlock<ShadowData> data;

public struct ShadowDrawParams
{
    public RWStructuredBuffer<PbrInstance> instances;
};

public ParameterBlock<ShadowDrawParams> draw;

[[vk::push_constant]]
cbuffer constants
{
    ShadowConstants shadowConstants;
}

[shader("vertex")]
public ShadowV2F MainVS<T : ISurface>(ShadowVertex input)
{
    ShadowV2F output;
    PbrInstance inst = draw.instances[input.instanceID];
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so shadows match the G-buffer silhouette.
    T surface = T();
    float3 surfaceWorldPos = surface.getWorldPosition(worldPos, worldNormal, input.uv);
    worldNormal = surface.getWorldNormal(worldPos, worldNormal, input.uv);
    worldPos = surfaceWorldPos;
    output.position = mul(data.lightViewProjections[(uint)shadowConstants.params.x], float4(worldPos, 1.0f));
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.worldPos = worldPos;
    output.uv = input.uv;
    output.instanceID = input.instanceID;
    return output;
}

[shader("fragment")]
public void MainPS<T : ISurface, let AlphaTest : bool>(ShadowV2F input)
{
    if (AlphaTest)
    {
        PbrInstance inst = draw.instances[input.instanceID];
        float alphaCutoff = inst.params.x;
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
            clip(surface.getBaseColor(surfaceInput).a - alphaCutoff);
        }
    }
}
