#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    float Exposure;
    float Gamma;
};

static const float3x3 ACESInputMat = {
    {0.59719, 0.35458, 0.04823},
    {0.07600, 0.90834, 0.01566},
    {0.02840, 0.13383, 0.83777}
};

static const float3x3 ACESOutputMat = {
    { 1.60475, -0.53108, -0.07367},
    {-0.10208,  1.10813, -0.00605},
    {-0.00327, -0.07276,  1.07602}
};

float3 RttAndOdtFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("pixel")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    v = mul(ACESInputMat, v);
    v = RttAndOdtFit(v);
    v = mul(ACESOutputMat, v);
    v = saturate(v);
    v = pow(v, 1.0 / Gamma);
    return float4(v, 1.0);
}

