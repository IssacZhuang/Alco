#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

struct Constants {
  float4x4 model;
  float4 color;
  float4 uvRect;
};

DEFINE_STRUCT(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 0.0f, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
  return _texture.Sample(_textureSampler, input.uv) * constants.color;
}