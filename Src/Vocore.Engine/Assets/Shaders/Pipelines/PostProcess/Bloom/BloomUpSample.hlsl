#include "Shaders/Libs/Core.hlsli"

struct Constants {
  float2 invTextureSize;
};

DEFINE_TEX2D_SAMPLE(0, _previousTexture);
DEFINE_TEX2D_SAMPLE(1, _currentTexture);
PUSH_CONSTANT Constants constants;

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 0.0f, 1.0f);
  output.uv = input.uv;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  float2 invTextureSize = constants.invTextureSize;
  float4 sum = float4(0, 0, 0, 0);
  // float weights[3] = {0.227027, 0.316216, 0.227027}; // Gaussian weights for a 5x5 kernel

  // // Apply the weights from the Gaussian kernel
  // for (int i = -1; i <= 1; ++i) {
  //   for (int j = -1; j <= 1; ++j) {
  //     float weight = weights[i + 1] * weights[j + 1];
  //     sum += weight * SAMPLE_TEX2D(_previousTexture, input.uv + float2(i, j) * invTextureSize);
  //   }
  // }

  float weights[5] = {0.07027, 0.316216, 0.227027, 0.316216, 0.07027}; // Gaussian weights for a 5x5 kernel

  // Apply the weights from the Gaussian kernel
  // for (int i = -2; i <= 2; ++i) {
  //   for (int j = -2; j <= 2; ++j) {
  //     float weight = weights[i + 2] * weights[j + 2];
  //     sum += weight * SAMPLE_TEX2D(_previousTexture, input.uv + float2(i, j) * invTextureSize);
  //   }
  // }

  sum +=  SAMPLE_TEX2D(_previousTexture, input.uv);

  float4 final = SAMPLE_TEX2D(_currentTexture, input.uv)*0.2 + sum;

  return  float4(final.rgb, 1); // 1.6854393 is the sum of the [5x5 guassion weights]/[3x3 guassion weights]
}