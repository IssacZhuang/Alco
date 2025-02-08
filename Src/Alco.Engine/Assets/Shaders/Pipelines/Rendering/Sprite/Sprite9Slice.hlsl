#include "Shaders/Libs/Core.hlsli"

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint vertexId : SV_VertexID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct Constants
{
    float4x4 model;
    float4 color;
    float4 uvRect;
    float4 sliceOffsets;//in bounds of x[-0.5,0.5] y[-0.5,0.5]
};


DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_TEX2D_SAMPLE(1, _texture);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    const float4 sliceMultiplier[16] = {
        float4(0, 0, 0, 0), //[0,0] top left, static
        float4(1, 0, 0, 0), //[1,0] offset left
        float4(0, 1, 0, 0), //[2,0] offset right
        float4(0, 0, 0, 0), //[3,0] top right, static
        float4(0, 0, 1, 0), //[0,1] offset top
        float4(1, 0, 1, 0), //[1,1] offset top left
        float4(0, 1, 1, 0), //[2,1] offset top right
        float4(0, 0, 1, 0), //[3,1] offset top
        float4(0, 0, 0, 1), //[0,2] offset bottom
        float4(1, 0, 0, 1), //[1,2] offset bottom left
        float4(0, 1, 0, 1), //[2,2] offset bottom right
        float4(0, 0, 0, 1), //[3,2] offset bottom
        float4(0, 0, 0, 0), //[0,3] bottom right, static
        float4(1, 0, 0, 0), //[1,3] offset left
        float4(0, 1, 0, 0), //[2,3] offset right
        float4(0, 0, 0, 0), //[3,3] bottom left, static
    };

    V2F output = (V2F)0;
    float4 sliceOffset = sliceMultiplier[input.vertexId] * constants.sliceOffsets;
    float3 position = input.position;
    position.x += sliceOffset.x + sliceOffset.y;//offset in left and right
    position.y += sliceOffset.z + sliceOffset.w;//offset in top and bottom

    output.position = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(viewProjection, output.position);
    output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
    return output;

}

[shader("pixel")]
float4 MainPS(V2F input)
    : SV_TARGET
{
    float4 color = _texture.Sample(_textureSampler, input.uv) * constants.color;
#if defined(ALPHA_TEST)
    if (color.a < 0.01f)
    {
        discard;
    }
#endif
    return color;
}
