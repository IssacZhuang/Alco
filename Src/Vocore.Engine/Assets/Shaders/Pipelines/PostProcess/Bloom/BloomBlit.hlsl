#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, texture); 

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 0.0f, 1.0f);
  output.uv = input.uv;
  return output;
}

float grayScale(float3 color) {
  return dot(color, float3(0.299, 0.587, 0.114));
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  // use gray scale as alpha
  float4 source = SAMPLE_TEX2D(texture, input.uv);
  float intensityBase = 2;

  return float4(source.rgb * intensityBase, grayScale(source.rgb));
}