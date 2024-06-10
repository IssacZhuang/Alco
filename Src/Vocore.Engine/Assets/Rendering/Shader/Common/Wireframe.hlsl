#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState Additive
#pragma DepthStencilState None
#pragma PrimitiveTopology LineList

struct Vertex2D {
  float3 position : POSITION; // already in world space
  float4 color : COLOR;
};

struct V2F {
  float4 position : SV_POSITION;
  float4 color : COLOR;
};


DEFINE_STRUCT(0, _camera) { float4x4 viewProjection; };


V2F vs_main(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(viewProjection, float4(input.position, 1.0f));
  output.color = input.color;
  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
  return input.color;
}