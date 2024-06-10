
#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read


DEFINE_TEX2D_SAMPLE(0, _texture); 

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

V2F vs_main(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 0.0f, 1.0f);
  output.uv = input.uv;
  return output;
}


float4 fs_main(V2F input) : SV_TARGET {
  return SAMPLE_TEX2D(_texture, input.uv);
}