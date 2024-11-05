#include "Rendering/ShaderLib/Core.hlsli"

#pragma EntryVertex MainVS
#pragma EntryFragment MainPS

#pragma DepthStencilState Write

struct Vertex2D {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

struct Constants {
  float4x4 model;
  float4 color;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

PUSH_CONSTANT Constants constants;

V2F MainVS(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv;
  return output;
}

float4 MainPS(V2F input) : SV_TARGET {
  return _texture.Sample(_textureSampler, input.uv) * constants.color;
}