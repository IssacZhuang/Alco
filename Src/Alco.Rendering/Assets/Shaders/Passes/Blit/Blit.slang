// file "Blit.slang" - module "Blit"
//
// Slang module converted from Shaders/Pipelines/Utils/Blit.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module Blit;


import AlcoRendering_Core;

public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;

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
  return pass.texture.Sample(samplers.linearClamp, input.uv);
}