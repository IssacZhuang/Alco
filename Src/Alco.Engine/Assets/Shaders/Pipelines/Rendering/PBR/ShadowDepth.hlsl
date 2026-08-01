#include "Shaders/Libs/Core.hlsli"

// Shadow map depth-only pass shader for the deferred PBR pipeline.
// Renders into a depth-only render texture from the light's point of view.
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
};

struct Constants
{
    float4x4 model;
    float4 params_; // x = shadow cascade index
};

// Per-cascade light view-projection matrices, updated per frame on the CPU.
// Kept in a uniform buffer (reference semantics) instead of push constants so
// recorded render bundles stay valid while the camera-fitted cascades move.
DEFINE_UNIFORM(0, _data)
{
    float4x4 lightViewProjections[4];
};

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(lightViewProjections[(uint)constants.params_.x], worldPosition);
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
}
