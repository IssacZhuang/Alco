#include "Shaders/Libs/Core.hlsli"

// Shadow map depth-only pass shader for tangent-bearing meshes of the deferred
// PBR pipeline. Renders into a depth-only render texture from the light's point
// of view. The vertex layout must match
// Alco.Rendering.VertexPositionNormalTextureTangent exactly.
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
};

struct V2F
{
    float4 position : SV_POSITION;
#ifdef SHADOW_CUTOUT
    float2 uv : TEXCOORD0;
#endif
};

struct Constants
{
    float4x4 model;
    // x = shadow cascade index
    // y = alphaCutoff (cutout only, 0 disables the test)
    // z = baseColorAlpha (cutout only)
    float4 params_;
};

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
DEFINE_UNIFORM(0, _data)
{
    float4x4 lightViewProjections[4];
};

#ifdef SHADOW_CUTOUT
DEFINE_TEX2D_SAMPLE(1, _albedoTexture);
#endif

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(lightViewProjections[(uint)constants.params_.x], worldPosition);
#ifdef SHADOW_CUTOUT
    output.uv = input.uv;
#endif
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
#ifdef SHADOW_CUTOUT
    float alphaCutoff = constants.params_.y;
    if (alphaCutoff > 0.0)
    {
        float alpha = SAMPLE_TEX2D(_albedoTexture, input.uv).a;
        clip(alpha * constants.params_.z - alphaCutoff);
    }
#endif
}
