// file "bloom-blit.slang" - module "bloom_blit"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Bloom/BloomBlit.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module bloom_blit;

import alco_rendering_core;

struct Constants {
    float gamma;
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

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    // Sample the bloom texture (this is the processed bloom result from upsampling)
    float4 bloomColor = pass.texture.Sample(samplers.linearClamp, input.uv);

    float a = max(0.0, bloomColor.a); 
    a = pow(a, 1.0 / constants.gamma);
    // Apply intensity and return the bloom color for additive blending
    return float4(bloomColor.rgb, a);
}