#include "Shaders/Libs/Core.hlsli"

struct Constants {
    float gamma;
};

DEFINE_TEX2D_SAMPLE(0, _texture);
PUSH_CONSTANT Constants constants;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  output.uv = input.uv;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    // Sample the bloom texture (this is the processed bloom result from upsampling)
    float4 bloomColor = SAMPLE_TEX2D(_texture, input.uv);

    float a = max(0.0, bloomColor.a); 
    a = pow(a, 1.0 / constants.gamma);
    // Apply intensity and return the bloom color for additive blending
    return float4(bloomColor.rgb, a);
}