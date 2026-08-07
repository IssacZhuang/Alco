#include "Shaders/Libs/Core.hlsli"

// Shadow map depth pass shader for cutout/alpha-test materials in the deferred
// PBR pipeline. Same as ShadowDepth.hlsl but samples the albedo texture in the
// pixel shader and discards transparent fragments, so meshes with alpha-masked
// foliage, fences, etc. cast correctly shaped shadows.
// The vertex layout must match Alco.Rendering.VertexPositionNormalTexture exactly.

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct Constants
{
    float4x4 model;
    float4 params_; // x = shadow cascade index, y = alphaCutoff, z = baseColorAlpha
};

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
DEFINE_UNIFORM(0, _data)
{
    float4x4 lightViewProjections[4];
};

DEFINE_TEX2D_SAMPLE(1, _albedoTexture);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(lightViewProjections[(uint)constants.params_.x], worldPosition);
    output.uv = input.uv;
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
    float alphaCutoff = constants.params_.y;
    if (alphaCutoff > 0.0)
    {
        float alpha = SAMPLE_TEX2D(_albedoTexture, input.uv).a;
        clip(alpha * constants.params_.z - alphaCutoff);
    }
}
