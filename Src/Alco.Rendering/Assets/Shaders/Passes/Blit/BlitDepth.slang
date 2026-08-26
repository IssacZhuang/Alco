// file "BlitDepth.slang" - module "BlitDepth"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BlitDepth;

import AlcoRendering_Core;

public struct TextureParams
{
    public DepthTexture2D texture;
};

public ParameterBlock<TextureParams> texture;

public struct DataParams
{
    public float2 canvasSize;
    public float2 dynamicRange;
};

public ParameterBlock<DataParams> data;
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
    float2 c = input.uv * data.canvasSize;
    int2 position = int2(c);
    // Reversed depth (near = 1, far = 0) already follows the visualization
    // convention of near = white, far = black — no flip needed.
    float depth = texture.texture.Load(int3(position, 0));
    // Renormalize depth: map [dynamicRange.x, dynamicRange.y] to [0, 1]
    depth = (depth - data.dynamicRange.x) / (data.dynamicRange.y - data.dynamicRange.x);

    return float4(depth, depth, depth, 1);
}
