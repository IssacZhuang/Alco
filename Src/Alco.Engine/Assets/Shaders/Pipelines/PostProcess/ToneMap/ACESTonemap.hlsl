#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    float A; // 2.51 (default)
    float B; // 0.03
    float C; // 2.43
    float D; // 0.59
    float E; // 0.14
    float Exposure;
    float Gamma;
};

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 ACES(float3 x) {
  return clamp((x * (A * x + B)) / (x * (C * x + D) + E), 0.0, 1.0);
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("pixel")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    float3 ldr = ACES(v);
    ldr = pow(ldr, 1.0 / Gamma);
    return float4(ldr, 1.0);
}

