#include "Shaders/Libs/Core.hlsli"

#pragma EntryVertex MainVS
#pragma EntryFragment MainPS

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


DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

[shader("vertex")]
V2F MainVS(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(viewProjection, float4(input.position, 1.0f));
  output.color = input.color;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  return input.color;
}