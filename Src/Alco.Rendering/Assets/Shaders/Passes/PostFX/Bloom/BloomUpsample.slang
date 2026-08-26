// file "BloomUpsample.slang" - module "BloomUpsample"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Bloom/BloomUpSample.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BloomUpsample;

import AlcoRendering_Core;

struct Constants {
    float2 invTextureSize;
    float spread;
};

public struct PreviousParams
{
    public Texture2D previousTexture;
};

public ParameterBlock<PreviousParams> previous;

public struct CurrentParams
{
    public Texture2D currentTexture;
};

public ParameterBlock<CurrentParams> current;

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
      sum += weight * previous.previousTexture.Sample(samplers.linearClamp, input.uv + float2(i, j) * sampleOffset);
    }
  }

  float3 current = current.currentTexture.Sample(samplers.linearClamp, input.uv).rgb;

  // Keep a uniform source below 2x energy across the complete pyramid. Fine
  // levels retain the source-sized core while coarser levels decay at every
  // step, forming a visible soft tail without dominating the reconstruction.
  return float4(current * 0.6 + sum.rgb * 0.7, 1.0);
}
