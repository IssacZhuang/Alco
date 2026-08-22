// file "BlitRegion.slang" - module "blit_region"
//
// Slang module converted from Shaders/Pipelines/Utils/BlitRegion.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module blit_region;

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

// Full-screen blit that samples a centered sub-region of the source texture,
// applying both crop and downscale in a single pass via bilinear sampling.
// `uvScale`/`uvOffset` select the source window; the GPU handles the downscale
// because the source region is larger than the destination quad.
struct Constants {
  float2 uvOffset; // Top-left (min) corner of the sampled source window, in [0,1].
  float2 uvScale;  // Size of the sampled source window, in [0,1] on each axis.
};

[[vk::binding(0, 0)]] Texture2D _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler;
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
  // Map the full-screen quad's [0,1] UVs into the cropped source window.
  output.uv = input.uv * constants.uvScale + constants.uvOffset;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  return SAMPLE_TEX2D(_texture, input.uv);
}
