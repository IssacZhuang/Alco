#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma DepthStencilState None

DEFINE_TEX2D_SAMPLE(0, texture);
DEFINE_STRUCT(1, data) { 
    float2 InvTextureSize;
    float Threshold; 
};

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
  float2 invTextureSize = InvTextureSize;
  float4 sum = float4(0, 0, 0, 0);
  float weights[5] = {0.07027, 0.316216, 0.227027, 0.316216,
                      0.07027}; // Gaussian weights for a 5x5 kernel

  // Apply the weights from the Gaussian kernel
  for (int i = -2; i <= 2; ++i) {
    for (int j = -2; j <= 2; ++j) {
      float weight = weights[i + 2] * weights[j + 2];
      sum += weight *
             SAMPLE_TEX2D(texture, input.uv + float2(i, j) * invTextureSize);
    }
  }

  float3 clamped = max(0, sum.rgb - Threshold);
  return float4(clamped*2, sum.a);
}