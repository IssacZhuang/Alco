#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PbrInstance.hlsli"
#include "Shaders/Libs/Surface.hlsli"
#include "Shaders/Materials/PbrStandard.hlsli" // @SURFACE@ default; the material composer swaps this line for a custom surface.

// Shadow map depth-only pass template for surface materials of the deferred
// PBR pipeline. Renders into a depth-only render texture from the light's
// point of view. The vertex layout must match Alco.Rendering.VertexPBR exactly.
// All material evaluation lives in the surface shader included above
// (contract: Shaders/Libs/Surface.hlsli); this template owns the entry points,
// the depth write and the cascade bindings.
// Per-instance data (model matrix, cutout scalars) lives in the _instances
// storage buffer and is fetched by SV_InstanceID; the push constant carries
// only the cascade index so per-cascade render bundles never re-record.
//
// Compile with SHADOW_CUTOUT defined to enable alpha testing: the pixel shader
// evaluates the surface (PASS_SHADOW permutation: alpha only in PbrStandard)
// and discards fragments below the cutoff, so cutout meshes (foliage, fences,
// etc.) cast correctly shaped shadows. Without the define the pixel shader is
// empty (zero-overhead opaque depth write).

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
    uint instanceId : SV_InstanceID;
};

struct V2F
{
    float4 position : SV_POSITION;
#if defined(SHADOW_CUTOUT)
    float3 normal : TEXCOORD0;
    float4 tangent : TEXCOORD1; // xyz = world tangent, w = bitangent sign
    float3 worldPos : TEXCOORD2;
    float2 uv : TEXCOORD3;
    uint instanceId : TEXCOORD4;
#else
    uint instanceId : TEXCOORD0;
#endif
};

// Push constant payload: only the cascade index remains per-draw (static per
// cascade bundle). Layout must match the CascadeConstants struct in
// ShadowRenderer.cs exactly.
struct ShadowConstants
{
    // x = shadow cascade index, yzw unused
    float4 params_;
};

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
DEFINE_UNIFORM(0, _data)
{
    float4x4 lightViewProjections[4];
};

DEFINE_STORAGE(1, PbrInstance, _instances);

PUSH_CONSTANT ShadowConstants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    PbrInstance inst = _instances[input.instanceId];
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so shadows match the G-buffer silhouette.
    ModifyVertex(worldPos, worldNormal, input.uv, 0.0f /* time: no global time buffer yet */);
    output.position = mul(lightViewProjections[(uint)constants.params_.x], float4(worldPos, 1.0f));
#if defined(SHADOW_CUTOUT)
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.worldPos = worldPos;
    output.uv = input.uv;
#endif
    output.instanceId = input.instanceId;
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
#if defined(SHADOW_CUTOUT)
    PbrInstance inst = _instances[input.instanceId];
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
        surfaceInput.time = 0.0f; // no global time buffer yet

        clip(EvaluateSurface(surfaceInput).alpha - alphaCutoff);
    }
#endif
}
