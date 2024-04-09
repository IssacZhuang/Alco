#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

DEFINE_TEX2D_SAMPLE(0, texture); // should be HDR image

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
  float4 source = SAMPLE_TEX2D(texture, input.uv);

  float luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
  float3 hdrColor = source.rgb / luminance;
  float3 ldrColor = hdrColor / (1.0f + hdrColor);

  return float4(ldrColor, 1.0);
}