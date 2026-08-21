#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PbrInstance.hlsli"

// Shadow map depth-only pass shader for the deferred PBR pipeline. Renders into
// a depth-only render texture from the light's point of view. The vertex layout
// must match Alco.Rendering.VertexPBR exactly.
// Per-instance data (model matrix, cutout scalars) lives in the _instances
// storage buffer and is fetched by SV_InstanceID; the push constant carries
// only the cascade index so per-cascade render bundles never re-record.
//
// Compile with SHADOW_CUTOUT defined to enable alpha testing: the pixel shader
// samples _albedoTexture and discards fragments below the cutoff, so cutout
// meshes (foliage, fences, etc.) cast correctly shaped shadows. Without the
// define the pixel shader is empty (zero-overhead opaque depth write).

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
    float2 uv : TEXCOORD0;
    uint instanceId : TEXCOORD1;
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

#if defined(SHADOW_CUTOUT)
DEFINE_TEX2D_SAMPLE(2, _albedoTexture);
#endif

DEFINE_STORAGE(1, PbrInstance, _instances);

PUSH_CONSTANT ShadowConstants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    PbrInstance inst = _instances[input.instanceId];
    float4 worldPosition = mul(inst.model, float4(input.position, 1.0f));
    output.position = mul(lightViewProjections[(uint)constants.params_.x], worldPosition);
#if defined(SHADOW_CUTOUT)
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
        float alpha = SAMPLE_TEX2D(_albedoTexture, input.uv).a;
        clip(alpha * inst.baseColor.a - alphaCutoff);
    }
#endif
}
