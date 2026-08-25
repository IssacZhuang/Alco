// file "blit-with-color.slang" - module "blit_with_color"
//
// Slang module converted from Shaders/Pipelines/Utils/BlitWithColor.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module blit_with_color;

import alco_rendering_core;

struct Constants {
  float4 color;
};

public struct PassParams
{
    public Texture2D _texture;
    public SamplerState _textureSampler;
};

public ParameterBlock<PassParams> _pass;
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
  return _pass._texture.Sample(_pass._textureSampler, input.uv) * constants.color;
}

