// file "BloomDownsample.slang" - module "BloomDownsample"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BloomDownsample;

import AlcoRendering_Core;

struct Constants {
    float2 invTextureSize;
};

public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
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

// High-quality downsample: four bilinear taps on the diagonal texel corners,
// averaging to a 4x4 box footprint. Negative results are clamped away so the
// bloom chain never feeds NaNs or negative energy downward.
[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float2 texel = constants.invTextureSize;

  float3 color = pass.texture.Sample(samplers.linearClamp, input.uv + texel * float2(-1, -1)).rgb;
  color += pass.texture.Sample(samplers.linearClamp, input.uv + texel * float2(1, -1)).rgb;
  color += pass.texture.Sample(samplers.linearClamp, input.uv + texel * float2(-1, 1)).rgb;
  color += pass.texture.Sample(samplers.linearClamp, input.uv + texel * float2(1, 1)).rgb;

  return float4(max(color * 0.25, 0.0), 1.0);
}
