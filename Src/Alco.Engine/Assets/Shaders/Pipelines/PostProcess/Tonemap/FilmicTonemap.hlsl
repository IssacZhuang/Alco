#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    float Exposure;
    float Gamma;
};

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 Filmic(float3 x) {
  float3 X = max(float3(0,0,0), x - 0.004);
  float3 result = (X * (6.2 * X + 0.5)) / (X * (6.2 * X + 1.7) + 0.06);
  return result;
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("pixel")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    float3 ldr = Filmic(v);
    ldr = pow(ldr, 1.0 / Gamma);
    return float4(ldr, 1.0);
}

