#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState Additive
#pragma DepthStencilState None

struct Vertex2D {
  float3 position : POSITION;
};

struct V2F {
  float4 position : SV_POSITION;
};

struct Constants {
  float4x4 model;
  float4 color;
};

DEFINE_STRUCT(0, _camera) { float4x4 viewProjection; };

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
  return color;
}