// file "blit-depth.slang" - module "blit_depth"
//
// Slang module converted from Shaders/Pipelines/Utils/BlitDepth.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module blit_depth;

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

[[vk::binding(0, 0)]] DepthTexture2D _texture;

[[vk::binding(0, 1)]] cbuffer _data {
    float2 canvasSize;
    float2 dynamicRange;
};
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
    float2 c = input.uv * canvasSize;
    int2 position = int2(c);
    // Reversed depth (near = 1, far = 0) already follows the visualization
    // convention of near = white, far = black — no flip needed.
    float depth = _texture.Load(int3(position, 0));
    // Renormalize depth: map [dynamicRange.x, dynamicRange.y] to [0, 1]
    depth = (depth - dynamicRange.x) / (dynamicRange.y - dynamicRange.x);

    return float4(depth, depth, depth, 1);
}
