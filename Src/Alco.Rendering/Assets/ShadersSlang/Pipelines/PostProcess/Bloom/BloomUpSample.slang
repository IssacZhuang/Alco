// file "BloomUpSample.slang" - module "bloom_up_sample"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Bloom/BloomUpSample.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module bloom_up_sample;

import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

struct Constants {
    float2 invTextureSize;
    float spread;
};

[[vk::binding(0, 0)]] Texture2D _previousTexture;
[[vk::binding(1, 0)]] SamplerState _previousTextureSampler;
[[vk::binding(0, 1)]] Texture2D _currentTexture;
[[vk::binding(1, 1)]] SamplerState _currentTextureSampler;
[[vk::push_constant]] Constants constants;

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

[shader("fragment")]
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
