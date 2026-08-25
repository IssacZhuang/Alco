// file "blit-depth.slang" - module "blit_depth"
//
// Slang module converted from Shaders/Pipelines/Utils/BlitDepth.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module blit_depth;

import alco_rendering_core;

public struct TextureParams
{
    public DepthTexture2D _texture;
};

public ParameterBlock<TextureParams> _texture;

public struct DataParams
{
    public float2 canvasSize;
    public float2 dynamicRange;
};

public ParameterBlock<DataParams> _data;
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
    float2 c = input.uv * _data.canvasSize;
    int2 position = int2(c);
    // Reversed depth (near = 1, far = 0) already follows the visualization
    // convention of near = white, far = black — no flip needed.
    float depth = _texture._texture.Load(int3(position, 0));
    // Renormalize depth: map [dynamicRange.x, dynamicRange.y] to [0, 1]
    depth = (depth - _data.dynamicRange.x) / (_data.dynamicRange.y - _data.dynamicRange.x);

    return float4(depth, depth, depth, 1);
}
