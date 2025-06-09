
#include "Shaders/Libs/Core.hlsli"

SLOT(0, 0) Texture2D<float> _texture;

DEFINE_UNIFORM(1, _data) {
  float2 canvasSize;
}

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
    float2 c = input.uv * canvasSize;
    int2 position = int2(c);
    float depth = GET_PIXEL_TEX2D(_texture, position);
    depth = (1.0 - depth);
    return float4(depth, depth, depth, 1);
}