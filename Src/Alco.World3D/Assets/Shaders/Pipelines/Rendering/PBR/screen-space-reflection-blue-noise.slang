// file "screen-space-reflection-blue-noise.slang" - module "screen_space_reflection_blue_noise"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionBlueNoise.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module screen_space_reflection_blue_noise;

import alco_rendering_core;

// One-time bake of the SSR blue-noise lookup into a persistent 128x128 RGBA8
// target (drawn on the first frame, then reused every frame). Channel mapping
// (the trace pass's stochastic dimensions):
//     R/G = glossy disk angle + radius pair
//     B   = march step jitter
//     A   = temporally stable value (blur-kernel rotation)
// The trace pass advances R/G/B with per-frame Cranley-Patterson rotations.
//
// Algorithm: neighborhood-rank construction. Each channel's value is the rank
// of the pixel's hash within its wrapped 3x3 neighborhood's hashes, plus a
// sub-stratum jitter from an independent hash. Forcing every 3x3
// neighborhood to hold a near-uniform spread of values anti-correlates
// neighbors (blue-ish spectrum) while keeping the marginal exactly uniform,
// measured slightly better on neighbor delta / block variance than Heitz's
// optimized 1spp scrambling tile from FidelityFX-SSSR. Unlike IGN the result
// is unstructured (hash-seeded), so no fixed pattern can show up in smooth
// glossy reflections.

static const uint SSR_BLUE_NOISE_SIZE = 128u;

uint SsrBlueNoiseHash(uint x, uint y, uint dimension)
{
    uint value = x * 0x9E3779B9u;
    value ^= y * 0x85EBCA6Bu;
    value ^= dimension * 0xC2B2AE35u;
    value ^= value >> 16;
    value *= 0x7FEB352Du;
    value ^= value >> 15;
    value *= 0x846CA68Bu;
    value ^= value >> 16;
    return value >> 8;
}

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
  int2 pixel = int2(saturate(input.uv) * SSR_BLUE_NOISE_SIZE);
  uint2 tile = uint2((pixel + (int)SSR_BLUE_NOISE_SIZE) % (int)SSR_BLUE_NOISE_SIZE);

  float4 result;
  [unroll]
  for (uint dimension = 0; dimension < 4; dimension++)
  {
    uint center = SsrBlueNoiseHash(tile.x, tile.y, dimension);
    uint rank = 0;
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
      [unroll]
      for (int dx = -1; dx <= 1; dx++)
      {
        int2 neighbor = (int2(tile) + int2(dx, dy) + (int)SSR_BLUE_NOISE_SIZE)
            % (int)SSR_BLUE_NOISE_SIZE;
        rank += SsrBlueNoiseHash(neighbor.x, neighbor.y, dimension) < center;
      }
    }
    uint subJitter = SsrBlueNoiseHash(tile.x, tile.y, dimension + 4u);
    result[dimension] = (float(rank) + float(subJitter) * (1.0 / 16777216.0)) / 9.0;
  }
  return result;
}
