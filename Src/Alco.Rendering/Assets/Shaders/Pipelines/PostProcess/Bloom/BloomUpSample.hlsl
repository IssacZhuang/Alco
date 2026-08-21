#include "Shaders/Libs/Core.hlsli"

struct Constants {
    float2 invTextureSize;
    float spread;
};

DEFINE_TEX2D_SAMPLE(0, _previousTexture);
DEFINE_TEX2D_SAMPLE(1, _currentTexture);
PUSH_CONSTANT Constants constants;

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
    float2 sampleOffset = constants.invTextureSize * constants.spread;
    float4 sum = float4(0, 0, 0, 0);

  float weights[5] = { 0.06136, 0.24477, 0.38774, 0.24477, 0.06136 }; // Normalized Gaussian weights for a 5x5 kernel

  // Apply the weights from the Gaussian kernel
  for (int i = -2; i <= 2; ++i) {
    for (int j = -2; j <= 2; ++j) {
      float weight = weights[i + 2] * weights[j + 2];
      sum += weight * SAMPLE_TEX2D(_previousTexture, input.uv + float2(i, j) * sampleOffset);
    }
  }

  float3 current = SAMPLE_TEX2D(_currentTexture, input.uv).rgb;

  // Keep a uniform source below 2x energy across the complete pyramid. Fine
  // levels retain the source-sized core while coarser levels decay at every
  // step, forming a visible soft tail without dominating the reconstruction.
  return float4(current * 0.6 + sum.rgb * 0.7, 1.0);
}
