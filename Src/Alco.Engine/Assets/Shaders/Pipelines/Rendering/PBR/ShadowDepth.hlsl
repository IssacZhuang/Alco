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
    float4x4 lightViewProjection; // combined model * light view * projection
};

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = mul(constants.lightViewProjection, float4(input.position, 1.0f));
    return output;
}

[shader("pixel")]
void MainPS(V2F input)
{
}
